using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using Wander.Core;
using Wander.Core.Diagnostics;
using Wander.Core.Icons;
using Wander.Core.Logging;
using Wander.Core.Preview;
using Wander.Core.Shell;

namespace Wander.Platform.Windows.Icons;

/// <summary>
/// Provides system icons / thumbnails via the Win32 shell.
///
/// Strategy by size:
///  - <b>Small / Normal</b>: <c>SHGetFileInfo</c>. The shell bakes the
///    link-overlay arrow in at these sizes for free
///    (<c>SHGFI_LINKOVERLAY</c>).
///
///  - <b>Medium (96 px)</b>: a real thumbnail for anything the shell can
///    preview (pictures, RAW, video, PDF, and a folder's peek-inside), the
///    plain registered icon for everything else — the split is
///    <c>IsThumbnailable</c>, the same one the large tier uses. This is what
///    the tile view draws, so a folder of source code keeps costing one
///    cheap <c>SHGetFileInfo</c> per <i>extension</i>, not per file.
///
///  - <b>Large (256 px)</b>: a two-step compose, the same way Explorer does
///    its "Large icons" view —
///      1. base image via <c>IShellItemImageFactory.GetImage</c>; this
///         returns the real thumbnail when the file has a thumbnail
///         provider (images, videos, PDFs…) and falls back to the regular
///         icon otherwise.
///      2. overlay (if any) via <c>SHGetFileInfo(SHGFI_OVERLAYINDEX)</c> +
///         <c>IImageList(SHIL_EXTRALARGE).GetOverlayImage / GetIcon</c>.
///         The 48 px arrow is up-scaled to ~80 px and drawn into the
///         bottom-left corner of the 256 px canvas.
///
///    The two-step is required because <c>IShellItemImageFactory.GetImage</c>
///    is documented (in samples / community guidance) to NOT compose
///    overlays — that's the caller's job. The jumbo system image list
///    (<c>SHIL_JUMBO</c>) also doesn't reliably contain overlay icons at
///    256 px; the smaller image lists do.
///
/// Caching is keyed by extension where the image is shared by a whole file
/// type (small / normal, and medium for files with no preview) and per-path
/// where it is that one file's own picture. Only the per-path entries count
/// against the memory budget; only the 256-px ones also go to disk.
/// </summary>
public sealed class SystemIconProvider : IIconProvider {
    /// <summary>
    /// How many per-path thumbnails to keep. Small / Normal icons are keyed
    /// by extension and so bounded by how many file types exist; Large ones
    /// are a unique bitmap per file, and a folder of ten thousand photos
    /// browsed in tile view would otherwise hold every one of them for the
    /// rest of the session. At roughly 100 KB apiece this caps that at tens
    /// of megabytes.
    /// </summary>
    private const int MaxCachedThumbnails = 512;

    private readonly Dictionary<string, byte[]> _cache = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Insertion order of the per-path entries, for evicting the oldest.</summary>
    private readonly Queue<string> _thumbnailOrder = new();
    private readonly object _lock = new();

    /// <summary>
    /// Second tier behind the in-memory one: survives a restart, so the
    /// first walk through a folder of RAW files is slow once instead of
    /// once per launch. Only large (per-file) thumbnails go through it —
    /// the smaller sizes are keyed by extension and cost nothing to rebuild.
    /// </summary>
    private readonly ThumbnailDiskCache? _disk;
    private int _memoryBudget = MaxCachedThumbnails;


    public SystemIconProvider(ThumbnailDiskCache? disk = null) {
        _disk = disk;
    }


    public byte[]? GetIcon(string path, IconSize size) {
        if (string.IsNullOrEmpty(path)) {
            return null;
        }

        var (key, perPath) = BuildCacheKey(path, size);

        lock (_lock) {
            if (_cache.TryGetValue(key, out byte[]? cached)) {
                return cached;
            }
        }

        // Disk tier, for the 256-px thumbnails only. Both tiers are timed:
        // when tiles are slow to fill in, the question is always whether the
        // cache is answering slowly or the shell is being asked at all.
        //
        // Medium deliberately stays memory-only. Its entries are a quarter
        // of the size and quick to rebuild (the shell's own thumbcache
        // answers most of them), while on disk they would compete with the
        // large ones for the same budget — and the disk key has no size in
        // it, so the two would collide outright.
        bool cacheable = size == IconSize.Large && _disk is not null;
        byte[]? fromDisk;
        using (PerfLog.Measure("bg.thumb-disk")) {
            fromDisk = cacheable ? _disk!.TryRead(path) : null;
        }
        if (fromDisk is not null) {
            lock (_lock) {
                Store(key, fromDisk, perPath);
            }
            return fromDisk;
        }

        try {
            byte[]? bytes;
            using (PerfLog.Measure("bg.thumb-shell")) {
                bytes = LoadIcon(path, size);
            }
            if (bytes is not null) {
                lock (_lock) {
                    Store(key, bytes, perPath);
                }
                if (cacheable) {
                    using (PerfLog.Measure("bg.thumb-disk-write")) {
                        _disk!.Write(path, bytes);
                    }
                }
            }
            return bytes;
        } catch {
            return null;
        }
    }


    public void ConfigureCache(ThumbnailCacheOptions options) {
        lock (_lock) {
            _memoryBudget = Math.Max(1, options.MemoryEntries);
            TrimMemory();
        }
        _disk?.Configure(options.DiskEnabled, options.DiskBudgetBytes);
    }


    public void ClearCache() {
        AudioCover.Forget();
        lock (_lock) {
            _cache.Clear();
            _thumbnailOrder.Clear();
        }
        _disk?.Clear();
    }


    public (string? Directory, long SizeBytes) DescribeCache() {
        return _disk is null ? (null, 0) : (_disk.Directory, _disk.CurrentSizeBytes());
    }


    public byte[]? TryGetCachedIcon(string path, IconSize size) {
        if (string.IsNullOrEmpty(path)) {
            return null;
        }

        lock (_lock) {
            return _cache.TryGetValue(BuildCacheKey(path, size).Key, out byte[]? cached) ? cached : null;
        }
    }


    /// <summary>
    /// Caller holds the lock. Evicts oldest-first once the thumbnail budget
    /// is spent. Only per-path entries count against it: the ones keyed by
    /// extension are bounded by how many file types exist on the machine and
    /// cost a few kilobytes each.
    /// </summary>
    private void Store(string key, byte[] bytes, bool perPath) {
        bool isNew = !_cache.ContainsKey(key);
        _cache[key] = bytes;
        if (!perPath || !isNew) {
            return;
        }

        _thumbnailOrder.Enqueue(key);
        TrimMemory();
    }


    /// <summary>Caller holds the lock.</summary>
    private void TrimMemory() {
        while (_thumbnailOrder.Count > _memoryBudget) {
            _cache.Remove(_thumbnailOrder.Dequeue());
        }
    }


    /// <summary>
    /// Where this request lands in the cache, and whether that slot belongs
    /// to this one file (a thumbnail) or is shared by every file of its type
    /// (an icon). The second half is what the memory budget is counted on.
    /// </summary>
    private static (string Key, bool PerPath) BuildCacheKey(string path, IconSize size) {
        // Shell-namespace sentinels (shell:RecycleBinFolder, …) have no real
        // filesystem behind them; key by the full URI lowered so different
        // sentinels stay distinct but caching still works.
        if (IsShellNamespacePath(path)) {
            return ($"shell|{path.ToLowerInvariant()}|{size}", true);
        }

        if (System.IO.Directory.Exists(path)) {
            return ($"dir|{path}|{size}", true);
        }

        string ext = Path.GetExtension(path);

        // Shortcuts (.lnk) get a unique composite per file → cache per path.
        if (ext.Equals(".lnk", StringComparison.OrdinalIgnoreCase)) {
            return ($"lnk|{path}|{size}", true);
        }

        // A book drawn from its own cover is that one book's picture, so it
        // cannot share the per-extension slot the other .fb2 files use. The
        // two small sizes are left out: a cover shrunk to 16 px is a smudge,
        // and the registered icon says "book" more clearly there.
        if (size is IconSize.Medium or IconSize.Large && HasOwnCover(path)) {
            return ($"book|{path}|{size}", true);
        }

        // Large = jumbo path with thumbnails → unique per file.
        if (size == IconSize.Large) {
            return ($"thumb|{path}", true);
        }

        // Medium is a thumbnail only for the files that have one; the rest
        // fall back to the shared per-extension icon, exactly as Normal does.
        if (size == IconSize.Medium && IsThumbnailable(path)) {
            return ($"thumb96|{path}", true);
        }

        if (string.IsNullOrEmpty(ext)) {
            return ($"file|noext|{size}", false);
        }

        return ($"ext|{ext.ToLowerInvariant()}|{size}", false);
    }

    private static byte[]? LoadIcon(string path, IconSize size) {
        // shell:RecycleBinFolder and similar sentinels can't go through
        // SHGetFileInfo by path — those calls just fail because there's
        // no file. Route them through PIDL-based icon lookup instead.
        if (IsShellNamespacePath(path)) {
            return LoadShellNamespaceIcon(path, size);
        }

        return size switch {
            IconSize.Large => LoadJumboImage(path),
            IconSize.Medium => LoadMediumImage(path),
            _ => LoadShellIcon(path, size),
        };
    }


    /// <summary>
    /// The tile-sized thumbnail: real content for anything the shell can
    /// preview, the plain registered icon for everything else.
    ///
    /// <para>
    /// The same split as <see cref="LoadJumboImage"/>, and for the same
    /// reason — asking <c>IShellItemImageFactory</c> about a file with no
    /// thumbnail provider writes an icon into the system's shared
    /// <c>thumbcache</c>, which Explorer then reads back. Files outside
    /// <see cref="IsThumbnailable"/> keep the cheap
    /// <c>SHGetFileInfo</c> path, which is also what makes a folder of
    /// source code scroll at the speed it did before thumbnails existed.
    /// </para>
    ///
    /// <para>
    /// Overlays (the shortcut arrow) are not composed here: at this size the
    /// only files that reach the thumbnail branch are pictures and folders,
    /// and <c>.lnk</c> goes down the icon branch, where the shell bakes the
    /// arrow in itself.
    /// </para>
    /// </summary>
    private static byte[]? LoadMediumImage(string path) {
        if (TryRenderBookCover(path, MediumSize) is { } cover) {
            return cover;
        }

        // A shortcut shows its target's picture, the way Explorer does —
        // a folder of shortcuts to photographs is otherwise a folder of
        // identical arrows.
        string? linkTarget = LinkThumbnailTarget(path);
        string source = linkTarget ?? path;
        if (!IsThumbnailable(source)) {
            return LoadShellIcon(path, IconSize.Normal);
        }

        // Same RAW shortcut as the jumbo tier, for the same reason — the
        // tile view of a folder of photographs is the other place where
        // the shell's per-file cost is felt.
        if (linkTarget is null && RawThumbnail.Render(path, MediumSize) is { } rawThumb) {
            return rawThumb;
        }

        using Bitmap? bmp = LoadShellBitmap(source, MediumSize);
        if (bmp is null) {
            return LoadShellIcon(path, IconSize.Normal);
        }

        // The shell bakes the arrow into the icons it hands out, but not
        // into a thumbnail it was asked for by a different path — so when
        // the picture came from the target, the badge is ours to draw.
        if (linkTarget is not null) {
            DrawLinkOverlay(bmp, path);
        }

        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);

        return ms.ToArray();
    }


    /// <summary>
    /// The file a shortcut's picture should come from: the target, when
    /// <paramref name="path"/> is a <c>.lnk</c> and the target still exists
    /// and has a thumbnail of its own. Null in every other case, which
    /// means "draw this file the ordinary way".
    /// </summary>
    private static string? LinkThumbnailTarget(string path) {
        if (!path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) {
            return null;
        }

        string? target = ResolveShortcut(path);

        return target is not null && File.Exists(target) && IsThumbnailable(target)
            ? target
            : null;
    }

    private static string? ResolveShortcut(string path) {
        if (!ServiceLocator.IsRegistered<IShortcutService>()) {
            return null;
        }

        try {
            string? target = ServiceLocator.Get<IShortcutService>().Resolve(path);

            return string.IsNullOrEmpty(target) ? null : target;
        } catch {
            // A .lnk pointing at a shell namespace, or a malformed one.
            return null;
        }
    }

    /// <summary>Composites the shortcut badge for <paramref name="linkPath"/> onto a bitmap.</summary>
    private static void DrawLinkOverlay(Bitmap canvas, string linkPath) {
        int overlayIndex = GetOverlayIndex(linkPath);
        if (overlayIndex == 0) {
            // Same safety net as the jumbo path: some builds skip overlay
            // computation, and a shortcut with no arrow is a lie.
            overlayIndex = 1;
        }

        using Bitmap? overlay = LoadOverlayBitmap(overlayIndex);
        if (overlay is not null) {
            CompositeOverlay(canvas, overlay);
        }
    }


    // ------------------------------------------------------------------
    // Book covers.
    // ------------------------------------------------------------------

    /// <summary>
    /// Whether this file's tile is drawn from its own cover rather than
    /// from the shell. Books carry one inside them; a PDF's first page
    /// stands in for one.
    /// </summary>
    private static bool HasOwnCover(string path) {
        return BookCover.Supports(path) || PdfPageImage.Supports(path) || AudioCover.Supports(path);
    }

    /// <summary>
    /// The file's own cover, drawn as a plate on the tile, or null when it
    /// has none — the caller then falls back to whatever the shell offers.
    ///
    /// <para>
    /// Three sources, one plate: a book carries a cover, a PDF's first page
    /// stands in for one, and a track has the album art in its tag or in a
    /// picture beside it. The shell answers for none of these the way we
    /// want — it does not read FLAC art at all, and it never looks beside
    /// the file.
    /// </para>
    /// </summary>
    private static byte[]? TryRenderBookCover(string path, int side) {
        if (!HasOwnCover(path)) {
            return null;
        }

        byte[]? bytes;
        using (PerfLog.Measure("bg.book-cover")) {
            bytes = PdfPageImage.Supports(path) ? PdfPageImage.RenderFirstPage(path, side)
                : AudioCover.Supports(path) ? AudioCover.Read(path)
                : BookCover.TryRead(path);
        }
        if (bytes is null) {
            return null;
        }

        try {
            using var buffer = new MemoryStream(bytes);
            using var cover = new Bitmap(buffer);
            using var framed = RenderFramedCover(cover, side);
            using var png = new MemoryStream();
            framed.Save(png, ImageFormat.Png);

            return png.ToArray();
        } catch (Exception ex) when (ex is ArgumentException or OutOfMemoryException) {
            // GDI+ throws both of these for "these bytes are not an image
            // I know" — a cover in a format without a codec, or a truncated
            // one.
            return null;
        }
    }

    /// <summary>
    /// Fits <paramref name="cover"/> into a square canvas as a book plate:
    /// centred, white-backed, thin border, a soft shadow down and to the
    /// right. The frame is what makes a tile read as a book rather than as
    /// a picture that happens to be portrait — and the white backing keeps
    /// a cover with transparency from dissolving into the tile.
    /// </summary>
    private static Bitmap RenderFramedCover(Image cover, int side) {
        var canvas = new Bitmap(side, side, PixelFormat.Format32bppArgb);

        using var g = Graphics.FromImage(canvas);
        g.Clear(Color.Transparent);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.SmoothingMode = SmoothingMode.HighQuality;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        int margin = Math.Max(2, side / 12);
        double scale = Math.Min(
            (double)(side - margin * 2) / cover.Width,
            (double)(side - margin * 2) / cover.Height);
        int w = Math.Max(1, (int)Math.Round(cover.Width * scale));
        int h = Math.Max(1, (int)Math.Round(cover.Height * scale));
        int x = (side - w) / 2;
        int y = (side - h) / 2;

        // Shadow: a few offset rectangles rather than a blur, because at
        // 96 px the difference is invisible and a blur is a filter pass per
        // file in the listing.
        int depth = Math.Max(1, side / 32);
        using (var shadow = new SolidBrush(Color.FromArgb(20, 0, 0, 0))) {
            for (int i = depth; i >= 1; i--) {
                g.FillRectangle(shadow, x + i, y + i, w, h);
            }
        }

        g.FillRectangle(Brushes.White, x, y, w, h);
        g.DrawImage(cover, new Rectangle(x, y, w, h));

        using var border = new Pen(Color.FromArgb(140, 0, 0, 0));
        g.DrawRectangle(border, x, y, w - 1, h - 1);

        return canvas;
    }

    private static bool IsShellNamespacePath(string path) {
        return path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase);
    }


    // ------------------------------------------------------------------
    // Shell-namespace icons (Recycle Bin etc.) — PIDL-driven SHGetFileInfo.
    // ------------------------------------------------------------------
    //
    // shell:RecycleBinFolder is not a filesystem path, so SHGetFileInfo
    // with the URI as pszPath returns nothing. The Windows-documented
    // pattern for "icon of a known folder" is:
    //   1. Resolve the shell URI / FOLDERID → ITEMIDLIST (PIDL).
    //   2. Pass the PIDL to SHGetFileInfo with SHGFI_PIDL.
    //   3. Free the PIDL with ILFree.
    //
    // We try two PIDL-acquisition paths in order:
    //   • SHParseDisplayName — parses the "shell:" URI string directly;
    //     it's what Explorer uses when the user types into the address
    //     bar. Most likely to work for arbitrary shell URIs (Libraries,
    //     OneDrive, Quick access, …) if we add them later.
    //   • SHGetKnownFolderIDList — looks up the GUID. Equivalent result
    //     on healthy systems but a different code path inside shell32.
    //
    // Both fail-paths are logged through IconLog so a user reporting
    // "no icon" can capture the actual HRESULT in the session log.
    //
    // For Large size we fall back to the regular icon; jumbo-size
    // composition for the Recycle Bin would need a different IShellItem
    // path and isn't worth the bytes for v1 (the bookmark tree only
    // asks for Small).

    private static byte[]? LoadShellNamespaceIcon(string path, IconSize size) {
        if (!IsRecycleBinPath(path)) {
            return null;
        }

        byte[]? bytes = TryLoadIconViaParseDisplayName(path, size);
        if (bytes is not null) {
            return bytes;
        }
        return TryLoadIconViaKnownFolder(size);
    }

    private static byte[]? TryLoadIconViaParseDisplayName(string shellPath, IconSize size) {
        IntPtr pidl = IntPtr.Zero;
        try {
            int hr;
            try {
                hr = SHParseDisplayName(shellPath, IntPtr.Zero, out pidl, 0, out _);
            } catch (Exception ex) {
                IconLog($"recycle-icon: SHParseDisplayName threw: {ex.Message}");
                return null;
            }
            if (hr != 0 || pidl == IntPtr.Zero) {
                IconLog($"recycle-icon: SHParseDisplayName hr=0x{hr:X8} pidl={pidl}");
                return null;
            }
            return IconFromPidl(pidl, size, "parse");
        } finally {
            if (pidl != IntPtr.Zero) {
                ILFree(pidl);
            }
        }
    }

    private static byte[]? TryLoadIconViaKnownFolder(IconSize size) {
        IntPtr pidl = IntPtr.Zero;
        try {
            int hr;
            try {
                hr = SHGetKnownFolderIDList(_folderIdRecycleBin, 0, IntPtr.Zero, out pidl);
            } catch (Exception ex) {
                IconLog($"recycle-icon: SHGetKnownFolderIDList threw: {ex.Message}");
                return null;
            }
            if (hr != 0 || pidl == IntPtr.Zero) {
                IconLog($"recycle-icon: SHGetKnownFolderIDList hr=0x{hr:X8} pidl={pidl}");
                return null;
            }
            return IconFromPidl(pidl, size, "kfid");
        } finally {
            if (pidl != IntPtr.Zero) {
                ILFree(pidl);
            }
        }
    }

    private static byte[]? IconFromPidl(IntPtr pidl, IconSize size, string trace) {
        uint flags = SHGFI_PIDL | SHGFI_ICON |
            (size == IconSize.Small ? SHGFI_SMALLICON : SHGFI_LARGEICON);

        var info = new SHFILEINFO();
        IntPtr result;
        try {
            result = SHGetFileInfoPidl(
                pidl, 0, ref info,
                (uint)Marshal.SizeOf<SHFILEINFO>(),
                flags);
        } catch (Exception ex) {
            IconLog($"recycle-icon: SHGetFileInfoPidl ({trace}) threw: {ex.Message}");
            return null;
        }

        if (result == IntPtr.Zero || info.hIcon == IntPtr.Zero) {
            IconLog($"recycle-icon: SHGetFileInfoPidl ({trace}) result={result} hIcon={info.hIcon}");
            return null;
        }

        try {
            byte[]? bytes = HIconToPng(info.hIcon);
            IconLog($"recycle-icon: ({trace}) OK, bytes={bytes?.Length ?? 0}");
            return bytes;
        } finally {
            DestroyIcon(info.hIcon);
        }
    }

    private static bool IsRecycleBinPath(string path) {
        return string.Equals(path, "shell:RecycleBinFolder", StringComparison.OrdinalIgnoreCase);
    }

    // FOLDERID_RecycleBinFolder — {B7534046-3ECB-4C18-BE4E-64FD61466250}
    private static readonly Guid _folderIdRecycleBin = new("B7534046-3ECB-4C18-BE4E-64FD61466250");

    private const uint SHGFI_PIDL = 0x00000008;


    // ------------------------------------------------------------------
    // Small / Normal — straight SHGetFileInfo.
    // ------------------------------------------------------------------

    private static byte[]? LoadShellIcon(string path, IconSize size) {
        uint flags = SHGFI_ICON | (size == IconSize.Small ? SHGFI_SMALLICON : SHGFI_LARGEICON);

        bool exists = File.Exists(path) || System.IO.Directory.Exists(path);
        if (!exists) {
            flags |= SHGFI_USEFILEATTRIBUTES;
        }

        if (path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) {
            flags |= SHGFI_LINKOVERLAY;
        }

        var info = new SHFILEINFO();
        IntPtr result = SHGetFileInfo(
            path,
            FILE_ATTRIBUTE_NORMAL,
            ref info,
            (uint)Marshal.SizeOf<SHFILEINFO>(),
            flags);

        if (result == IntPtr.Zero || info.hIcon == IntPtr.Zero) {
            return null;
        }

        try {
            return HIconToPng(info.hIcon);
        } finally {
            DestroyIcon(info.hIcon);
        }
    }


    // ------------------------------------------------------------------
    // Large — IShellItemImageFactory + manual overlay composition.
    // ------------------------------------------------------------------

    private static byte[]? LoadJumboImage(string path) {
        // Routing: files with a real thumbnail provider (images, videos, PDFs,
        // folders with content peek) go through IShellItemImageFactory so we
        // get true thumbnails. Everything else — text, code, .exe, .lnk — uses
        // the older SHGetFileInfo + SHIL_JUMBO icon-list path, which extracts
        // the registered icon directly from the file's resource without
        // touching the system thumbnail cache (thumbcache_256.db).
        //
        // Why this matters: IShellItemImageFactory.GetImage *does* write to
        // thumbcache_256.db even when shell falls back to an icon for files
        // without a thumbnail provider. That cache entry persists, and Win11
        // Explorer reads from the same cache for its own large-icons view —
        // so a sub-optimal write from us could later make Explorer's display
        // of the same file look blurrier than before Wander ran. Splitting
        // the paths keeps icon-only writes out of the shared cache.
        if (TryRenderBookCover(path, JumboSize) is { } cover) {
            return cover;
        }

        // A shortcut is drawn from its target's thumbnail when the target
        // has one; the arrow is composited below either way.
        string? linkTarget = LinkThumbnailTarget(path);
        string source = linkTarget ?? path;

        // RAW containers carry a display JPEG, and pulling it out beats
        // asking the shell by more than an order of magnitude — see
        // RawThumbnail. Only for the file itself: a .lnk pointing at a RAW
        // still needs its arrow composited, and that happens below on a
        // Bitmap this path never produces. Overlays on the RAW itself are
        // not a case that occurs — the arrow is the only overlay Wander has
        // ever seen — so returning here loses nothing.
        if (linkTarget is null && RawThumbnail.Render(path, JumboSize) is { } rawThumb) {
            return rawThumb;
        }

        Bitmap? baseBmp = IsThumbnailable(source)
            ? LoadShellBitmap(source, JumboSize)
            : LoadIconBitmapJumbo(path);

        if (baseBmp is null) {
            // Couldn't get a 256-px image at all — fall back to the
            // shell's 32-px icon so the user sees *something* instead
            // of a blank tile.
            return LoadShellIcon(path, IconSize.Normal);
        }

        try {
            int overlayIndex = GetOverlayIndex(path);
            bool isLnk = path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase);

            // Defensive fallback: if the shell didn't report an overlay
            // for a .lnk, force index 1 (the well-known "link arrow"
            // slot). Some Windows builds skip overlay computation when
            // SHGFI_ICON isn't part of the call; this is a safety net.
            if (overlayIndex == 0 && isLnk) {
                overlayIndex = 1;
                IconLog($"forced overlay=1 for .lnk (shell reported none): {path}");
            }

            if (isLnk) {
                IconLog($"overlay query: path={path} idx={overlayIndex}");
            }

            if (overlayIndex > 0) {
                using Bitmap? overlayBmp = LoadOverlayBitmap(overlayIndex);
                if (isLnk) {
                    IconLog($"overlay bitmap: loaded={overlayBmp is not null}" +
                            (overlayBmp is not null
                                ? $" size={overlayBmp.Width}x{overlayBmp.Height} fmt={overlayBmp.PixelFormat}"
                                : ""));
                }
                if (overlayBmp is not null) {
                    CompositeOverlay(baseBmp, overlayBmp);
                    if (isLnk) {
                        IconLog($"overlay composited onto base ({baseBmp.Width}x{baseBmp.Height})");
                    }
                }
            }

            using var ms = new MemoryStream();
            baseBmp.Save(ms, ImageFormat.Png);
            return ms.ToArray();
        } finally {
            baseBmp.Dispose();
        }
    }


    // Diagnostic logging — wired through the standard ILogger so the
    // user can read what happened via Debug → Logs. Lazily resolved to
    // avoid a hard dependency: tests register a NullLogger, and an
    // unconfigured ServiceLocator just silently no-ops.
    private static ILogger? _log;
    private static bool _logResolved;
    private static void IconLog(string msg) {
        if (!_logResolved) {
            _logResolved = true;
            _log = ServiceLocator.IsRegistered<ILogger>() ? ServiceLocator.Get<ILogger>() : null;
        }
        _log?.Info("[icon] " + msg);
    }


    /// <summary>
    /// Extensions we believe have a real thumbnail provider — i.e. the
    /// shell can produce a content-based preview. For these, going through
    /// <c>IShellItemImageFactory</c> is the right thing. Folders also
    /// qualify (Win11 content peek).
    ///
    /// Everything outside this list is icon-only and goes through the
    /// older <c>SHIL_JUMBO</c> path so we don't pollute the system
    /// thumbnail cache (see <see cref="LoadJumboImage"/> comment).
    /// </summary>
    private static readonly HashSet<string> _thumbnailableExtensions = new(StringComparer.OrdinalIgnoreCase) {
        // Images
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".tif", ".tiff",
        ".ico", ".heic", ".heif", ".svg",
        // RAW
        ".cr2", ".cr3", ".nef", ".arw", ".dng", ".raf", ".orf", ".rw2",
        // Video
        ".mp4", ".m4v", ".mov", ".wmv", ".avi", ".mkv", ".webm", ".mts", ".m2ts",
        // Documents with shell thumbnail providers
        ".pdf", ".docx", ".xlsx", ".pptx", ".doc", ".xls", ".ppt",
    };

    private static bool IsThumbnailable(string path) {
        if (Directory.Exists(path)) {
            return true;
        }
        return _thumbnailableExtensions.Contains(Path.GetExtension(path));
    }


    /// <summary>
    /// Icon-only path: <c>SHGetFileInfo(SHGFI_SYSICONINDEX)</c> +
    /// <c>SHGetImageList(SHIL_JUMBO)</c> + <c>GetIcon</c>. Returns the
    /// file's registered 256-px icon without going through the modern
    /// thumbnail pipeline, which means no write to <c>thumbcache_*.db</c>.
    /// Used for files we know have no thumbnail provider — .txt, .lnk,
    /// .exe, source code, etc.
    /// </summary>
    private static Bitmap? LoadIconBitmapJumbo(string path) {
        var iidImageList = new Guid("46EB5926-582E-4017-9FDF-E8998DAA0950");
        int hr = SHGetImageList(SHIL_JUMBO, ref iidImageList, out IImageList list);
        if (hr != 0 || list is null) {
            return null;
        }

        try {
            uint flags = SHGFI_SYSICONINDEX;
            bool exists = File.Exists(path) || Directory.Exists(path);
            if (!exists) {
                flags |= SHGFI_USEFILEATTRIBUTES;
            }

            var info = new SHFILEINFO();
            IntPtr result = SHGetFileInfo(
                path,
                FILE_ATTRIBUTE_NORMAL,
                ref info,
                (uint)Marshal.SizeOf<SHFILEINFO>(),
                flags);
            if (result == IntPtr.Zero) {
                return null;
            }

            IntPtr hIcon = IntPtr.Zero;
            try {
                if (list.GetIcon(info.iIcon, ILD_TRANSPARENT, ref hIcon) != 0 || hIcon == IntPtr.Zero) {
                    return null;
                }
                return HIconToBitmap(hIcon);
            } finally {
                if (hIcon != IntPtr.Zero) {
                    DestroyIcon(hIcon);
                }
            }
        } finally {
            Marshal.ReleaseComObject(list);
        }
    }


    /// <summary>
    /// Gets a base image of the requested side (thumbnail-if-available,
    /// otherwise icon), without overlays. Returns a managed
    /// <see cref="Bitmap"/> in top-down 32-bpp ARGB so the caller can paint
    /// over it safely.
    /// </summary>
    private static Bitmap? LoadShellBitmap(string path, int side) {
        IShellItem? item = null;
        try {
            int hr;
            try {
                hr = SHCreateItemFromParsingName(path, IntPtr.Zero, _iidShellItem, out item);
            } catch {
                return null;
            }
            if (hr != 0 || item is null) {
                return null;
            }

            if (item is not IShellItemImageFactory factory) {
                return null;
            }

            var size = new SIZE { cx = side, cy = side };
            int hrImg = factory.GetImage(size, SIIGBF_RESIZETOFIT, out IntPtr hBitmap);
            if (hrImg != 0 || hBitmap == IntPtr.Zero) {
                return null;
            }

            try {
                return HBitmapToBitmap(hBitmap);
            } finally {
                DeleteObject(hBitmap);
            }
        } finally {
            if (item is not null) {
                Marshal.ReleaseComObject(item);
            }
        }
    }


    /// <summary>
    /// Asks the shell which overlay (link arrow, shared hand, …) applies
    /// to <paramref name="path"/>. Returns 0 when none, 1..15 otherwise.
    ///
    /// Includes <c>SHGFI_ICON</c> in the call even though we don't use
    /// the returned icon — empirically, on Windows 11 some builds skip
    /// overlay-index computation when only <c>SHGFI_SYSICONINDEX</c>
    /// is set. The MSDN wording ("Modifies SHGFI_ICON. Modifies
    /// SHGFI_SYSICONINDEX…") is ambiguous about whether both forms are
    /// supported; the doc-blessed sample uses SHGFI_ICON, so we do too
    /// and destroy the throwaway HICON.
    /// </summary>
    private static int GetOverlayIndex(string path) {
        uint flags = SHGFI_OVERLAYINDEX | SHGFI_ICON;
        bool exists = File.Exists(path) || System.IO.Directory.Exists(path);
        if (!exists) {
            flags |= SHGFI_USEFILEATTRIBUTES;
        }

        var info = new SHFILEINFO();
        IntPtr res = SHGetFileInfo(
            path,
            FILE_ATTRIBUTE_NORMAL,
            ref info,
            (uint)Marshal.SizeOf<SHFILEINFO>(),
            flags);
        if (info.hIcon != IntPtr.Zero) {
            DestroyIcon(info.hIcon);
        }
        if (res == IntPtr.Zero) {
            return 0;
        }

        // Overlay index lives in the upper byte of iIcon. Use UNSIGNED
        // right-shift so we don't sign-extend a negative iIcon into a
        // bogus 0xFF overlay value.
        return (int)(((uint)info.iIcon) >> 24) & 0xFF;
    }


    /// <summary>
    /// Fetches the overlay-only icon (e.g. just the link arrow on a
    /// transparent canvas) from a shell system image list.
    ///
    /// Tries 48 → 32 → 16 px image lists in turn. The jumbo (256 px)
    /// list doesn't reliably contain overlay slots, and on some builds
    /// even the 48-px list comes back empty for certain overlays, so
    /// we descend to whichever list actually answers.
    ///
    /// Pixel extraction goes through <c>GetIconInfo</c> → color HBITMAP
    /// → <c>GetDIBits</c> rather than <c>Icon.FromHandle().ToBitmap()</c>.
    /// ToBitmap has a long-standing alpha-handling problem with HICONs
    /// returned by <c>ImageList_GetIcon</c>: the result often comes back
    /// either fully transparent (badge invisible) or with the
    /// transparency-mask collapsed to opaque black (badge surrounded
    /// by a square block). GetIconInfo+GetDIBits gives us the raw
    /// 32-bpp ARGB pixels directly, which paint correctly.
    /// </summary>
    private static Bitmap? LoadOverlayBitmap(int overlayIndex) {
        // Order matters: prefer larger lists for sharper badges, but
        // accept whatever's actually populated.
        int[] sizes = { SHIL_EXTRALARGE, SHIL_LARGE, SHIL_SMALL };
        foreach (int shilSize in sizes) {
            var bmp = TryLoadOverlayFromList(overlayIndex, shilSize);
            if (bmp is not null) {
                return bmp;
            }
        }
        return null;
    }

    private static Bitmap? TryLoadOverlayFromList(int overlayIndex, int shilSize) {
        var iidImageList = new Guid("46EB5926-582E-4017-9FDF-E8998DAA0950");
        int hr = SHGetImageList(shilSize, ref iidImageList, out IImageList list);
        if (hr != 0 || list is null) {
            IconLog($"SHGetImageList(SHIL={shilSize}) failed hr=0x{hr:X}");
            return null;
        }

        IntPtr hIcon = IntPtr.Zero;
        try {
            int rcMap = list.GetOverlayImage(overlayIndex, out int overlayImg);
            if (rcMap != 0 || overlayImg <= 0) {
                IconLog($"GetOverlayImage(SHIL={shilSize}, ov={overlayIndex}) -> rc=0x{rcMap:X} idx={overlayImg}");
                return null;
            }

            int rcIcon = list.GetIcon(overlayImg, ILD_TRANSPARENT, ref hIcon);
            if (rcIcon != 0 || hIcon == IntPtr.Zero) {
                IconLog($"GetIcon(SHIL={shilSize}, slot={overlayImg}) -> rc=0x{rcIcon:X}");
                return null;
            }

            var bmp = HIconToBitmap(hIcon);
            if (bmp is null) {
                IconLog($"HIconToBitmap failed for overlay (SHIL={shilSize})");
            }
            return bmp;
        } finally {
            if (hIcon != IntPtr.Zero) {
                DestroyIcon(hIcon);
            }
            Marshal.ReleaseComObject(list);
        }
    }

    /// <summary>
    /// Extracts the colour DIB out of an <c>HICON</c> and returns it as
    /// a managed 32-bpp ARGB top-down <see cref="Bitmap"/>. Generic helper
    /// used for both base icons (icon-only file path) and overlay icons.
    /// We don't use <c>Icon.ToBitmap()</c> because it strips the alpha
    /// channel for HICONs returned by <c>ImageList_GetIcon</c>.
    /// </summary>
    private static Bitmap? HIconToBitmap(IntPtr hIcon) {
        var iconInfo = new ICONINFO();
        if (!GetIconInfo(hIcon, ref iconInfo)) {
            return null;
        }

        try {
            if (iconInfo.hbmColor == IntPtr.Zero) {
                // 1-bit-per-pixel monochrome icon — modern shell icons are
                // always 32-bpp ARGB, so this shouldn't fire in practice.
                // Bail out rather than synthesising colours from the mask.
                return null;
            }
            return HBitmapToBitmap(iconInfo.hbmColor);
        } finally {
            if (iconInfo.hbmColor != IntPtr.Zero) {
                DeleteObject(iconInfo.hbmColor);
            }
            if (iconInfo.hbmMask != IntPtr.Zero) {
                DeleteObject(iconInfo.hbmMask);
            }
        }
    }


    /// <summary>
    /// Draws <paramref name="overlay"/> in the bottom-left of <paramref name="canvas"/>,
    /// scaled up to roughly match Explorer's badge size on jumbo icons.
    /// </summary>
    private static void CompositeOverlay(Bitmap canvas, Bitmap overlay) {
        // ~62% of the canvas — larger than Explorer's default to make
        // the badge unmissable on tiles that scale down to ~72 px in
        // the LargeIcons view. At those sizes a 31 % badge becomes ~22 px,
        // which is easy to overlook. 62 % gives a clearly visible arrow.
        int overlayPx = canvas.Width * 5 / 8; // 160 for 256

        using var g = Graphics.FromImage(canvas);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.SmoothingMode = SmoothingMode.HighQuality;
        g.CompositingMode = CompositingMode.SourceOver;
        g.CompositingQuality = CompositingQuality.HighQuality;
        var dest = new Rectangle(
            0,
            canvas.Height - overlayPx,
            overlayPx,
            overlayPx);
        g.DrawImage(overlay, dest);
    }


    // ------------------------------------------------------------------
    // HBITMAP / HICON → managed Bitmap conversion.
    // ------------------------------------------------------------------

    /// <summary>
    /// Converts an arbitrary <c>HBITMAP</c> from the shell into a managed
    /// 32-bpp ARGB <see cref="Bitmap"/>, normalised to top-down.
    ///
    /// We can't use the simpler <c>new Bitmap(w, h, stride, fmt, scan0)</c>
    /// wrap because that interpretation depends on the source DIB's
    /// orientation, which the <c>BITMAP</c> struct doesn't expose: shell
    /// thumbnail providers usually return top-down 32-bpp PARGB, but the
    /// icon-fallback path (for files without a thumbnail) often returns
    /// bottom-up. The wrap rendered the bottom-up case upside-down.
    ///
    /// <c>GetDIBits</c> with <c>biHeight = -height</c> tells GDI to give
    /// us pixels in top-down order regardless of how they're stored in
    /// the source, which fixes both shapes with one path.
    /// </summary>
    private static Bitmap? HBitmapToBitmap(IntPtr hBitmap) {
        var info = new BITMAP();
        if (GetObject(hBitmap, Marshal.SizeOf<BITMAP>(), ref info) == 0) {
            return null;
        }

        int width = info.bmWidth;
        int height = info.bmHeight;
        if (width <= 0 || height <= 0) {
            return null;
        }

        var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        bool ok = false;
        var rect = new Rectangle(0, 0, width, height);
        var data = bmp.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try {
            var bi = new BITMAPINFOHEADER {
                biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
                biWidth = width,
                biHeight = -height,        // negative → top-down output
                biPlanes = 1,
                biBitCount = 32,
                biCompression = BI_RGB,
            };

            IntPtr hdc = GetDC(IntPtr.Zero);
            try {
                int rc = GetDIBits(
                    hdc, hBitmap,
                    0, (uint)height,
                    data.Scan0,
                    ref bi,
                    DIB_RGB_COLORS);
                ok = rc != 0;
            } finally {
                ReleaseDC(IntPtr.Zero, hdc);
            }
        } finally {
            bmp.UnlockBits(data);
        }

        if (!ok) {
            bmp.Dispose();
            return null;
        }
        return bmp;
    }


    private static byte[] HIconToPng(IntPtr hIcon) {
        using var icon = Icon.FromHandle(hIcon);
        using var bmp = icon.ToBitmap();
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }


    // ------------------------------------------------------------------
    // P/Invoke — shell + GDI.
    // ------------------------------------------------------------------

    private const uint SHGFI_ICON = 0x000000100;
    private const uint SHGFI_LARGEICON = 0x000000000;
    private const uint SHGFI_SMALLICON = 0x000000001;
    private const uint SHGFI_OVERLAYINDEX = 0x000000040;
    private const uint SHGFI_SYSICONINDEX = 0x000004000;
    private const uint SHGFI_LINKOVERLAY = 0x000008000;
    private const uint SHGFI_USEFILEATTRIBUTES = 0x000000010;
    private const uint FILE_ATTRIBUTE_NORMAL = 0x00000080;

    private const int JumboSize = 256;

    /// <summary>
    /// Side of a <see cref="IconSize.Medium"/> thumbnail. Big enough that a
    /// tile drawn at 32–64 px stays crisp (and survives a display scaled to
    /// 150 %), small enough that a folder of photographs costs a quarter of
    /// what the jumbo tier costs to decode and hold.
    /// </summary>
    private const int MediumSize = 96;
    private const int SIIGBF_RESIZETOFIT = 0x00000000;

    private const int SHIL_SMALL = 0x1;        // 16 × 16
    private const int SHIL_LARGE = 0x0;        // 32 × 32
    private const int SHIL_EXTRALARGE = 0x2;   // 48 × 48
    private const int SHIL_JUMBO = 0x4;        // 256 × 256
    private const int ILD_TRANSPARENT = 0x1;

    private const int BI_RGB = 0;
    private const uint DIB_RGB_COLORS = 0;

    private static readonly Guid _iidShellItem = new("43826D1E-E718-42EE-BC55-A1E261C37BFE");


    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct SHFILEINFO {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE {
        public int cx;
        public int cy;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAP {
        public int bmType;
        public int bmWidth;
        public int bmHeight;
        public int bmWidthBytes;
        public ushort bmPlanes;
        public ushort bmBitsPixel;
        public IntPtr bmBits;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ICONINFO {
        [MarshalAs(UnmanagedType.Bool)] public bool fIcon;
        public int xHotspot;
        public int yHotspot;
        public IntPtr hbmMask;
        public IntPtr hbmColor;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
        // followed by bmiColors[] when biBitCount <= 8 — not needed for 32-bpp,
        // but we still pass-by-ref so a few bytes of "color table" after the
        // header are written into nothingness; with biClrUsed = 0 GDI doesn't
        // read past biClrImportant for 32-bpp.
    }


    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SHGetFileInfo(
        string pszPath,
        uint dwFileAttributes,
        ref SHFILEINFO psfi,
        uint cbSizeFileInfo,
        uint uFlags);

    // Same SHGetFileInfo but the first arg is a PIDL (ITEMIDLIST*) instead
    // of a string. Required for shell-namespace items that have no path —
    // see LoadShellNamespaceIcon.
    [DllImport("shell32.dll", EntryPoint = "SHGetFileInfoW", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfoPidl(
        IntPtr pidl,
        uint dwFileAttributes,
        ref SHFILEINFO psfi,
        uint cbSizeFileInfo,
        uint uFlags);

    // REFKNOWNFOLDERID in COM is a const-GUID pointer; the standard P/Invoke
    // is `[MarshalAs(UnmanagedType.LPStruct)] Guid`, matching the pattern
    // used by SHCreateItemFromParsingName above.
    [DllImport("shell32.dll")]
    private static extern int SHGetKnownFolderIDList(
        [MarshalAs(UnmanagedType.LPStruct)] Guid rfid,
        uint dwFlags,
        IntPtr hToken,
        out IntPtr ppidl);

    // Parses a display name (including shell URIs like "shell:RecycleBinFolder",
    // CLSID parse names like "::{645FF040-…}", and regular paths) into a PIDL.
    // The Unicode-only entry point is the one used by Explorer; the ANSI
    // variant exists for back-compat with very old code.
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHParseDisplayName(
        [MarshalAs(UnmanagedType.LPWStr)] string pszName,
        IntPtr pbc,
        out IntPtr ppidl,
        uint sfgaoIn,
        out uint psfgaoOut);

    [DllImport("shell32.dll")]
    private static extern void ILFree(IntPtr pidl);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHCreateItemFromParsingName(
        [MarshalAs(UnmanagedType.LPWStr)] string pszPath,
        IntPtr pbc,
        [MarshalAs(UnmanagedType.LPStruct)] Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItem ppv);

    [DllImport("shell32.dll", EntryPoint = "#727")]
    private static extern int SHGetImageList(int iImageList, ref Guid riid, out IImageList ppv);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetIconInfo(IntPtr hIcon, ref ICONINFO piconinfo);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr ho);

    [DllImport("gdi32.dll", EntryPoint = "GetObject")]
    private static extern int GetObject(IntPtr hgdiobj, int cb, ref BITMAP lpvObject);

    [DllImport("gdi32.dll")]
    private static extern int GetDIBits(
        IntPtr hdc,
        IntPtr hbm,
        uint start,
        uint cLines,
        IntPtr lpvBits,
        ref BITMAPINFOHEADER lpbmi,
        uint usage);


    [ComImport]
    [Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem {
        // Empty: we only need the RCW so we can QI to IShellItemImageFactory.
    }

    [ComImport]
    [Guid("BCC18B79-BA16-442F-80C4-8A59C30C463B")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory {
        [PreserveSig] int GetImage(SIZE size, int flags, out IntPtr phbm);
    }

    // IImageList — we only invoke GetIcon (slot 8) and GetOverlayImage
    // (slot 29). Methods 1..7 and 9..28 are declared as IntPtr stubs to
    // preserve vtable ordering; the runtime fills the implicit IUnknown
    // slots (1..3) ahead of these via [InterfaceType].
    [ComImport]
    [Guid("46EB5926-582E-4017-9FDF-E8998DAA0950")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IImageList {
        [PreserveSig] int Add(IntPtr hbmImage, IntPtr hbmMask, ref int pi);
        [PreserveSig] int ReplaceIcon(int i, IntPtr hicon, ref int pi);
        [PreserveSig] int SetOverlayImage(int iImage, int iOverlay);
        [PreserveSig] int Replace(int i, IntPtr hbmImage, IntPtr hbmMask);
        [PreserveSig] int AddMasked(IntPtr hbmImage, int crMask, ref int pi);
        [PreserveSig] int Draw(IntPtr pimldp);
        [PreserveSig] int Remove(int i);
        [PreserveSig] int GetIcon(int i, int flags, ref IntPtr picon);
        [PreserveSig] int GetImageInfo(int i, IntPtr pImageInfo);
        [PreserveSig] int Copy(int iDst, IntPtr punkSrc, int iSrc, int uFlags);
        [PreserveSig] int Merge(int i1, IntPtr punk2, int i2, int dx, int dy, ref Guid riid, out IntPtr ppv);
        [PreserveSig] int Clone(ref Guid riid, out IntPtr ppv);
        [PreserveSig] int GetImageRect(int i, IntPtr prc);
        [PreserveSig] int GetIconSize(out int cx, out int cy);
        [PreserveSig] int SetIconSize(int cx, int cy);
        [PreserveSig] int GetImageCount(out int pi);
        [PreserveSig] int SetImageCount(int uNewCount);
        [PreserveSig] int SetBkColor(int clrBk, out int pclr);
        [PreserveSig] int GetBkColor(out int pclr);
        [PreserveSig] int BeginDrag(int iTrack, int dxHotspot, int dyHotspot);
        [PreserveSig] int EndDrag();
        [PreserveSig] int DragEnter(IntPtr hwndLock, int x, int y);
        [PreserveSig] int DragLeave(IntPtr hwndLock);
        [PreserveSig] int DragMove(int x, int y);
        [PreserveSig] int SetDragCursorImage(IntPtr punk, int iDrag, int dxHotspot, int dyHotspot);
        [PreserveSig] int DragShowNolock(int fShow);
        [PreserveSig] int GetDragImage(IntPtr ppt, IntPtr pptHotspot, ref Guid riid, out IntPtr ppv);
        [PreserveSig] int GetItemFlags(int i, out int dwFlags);
        [PreserveSig] int GetOverlayImage(int iOverlay, out int piIndex);
    }
}
