using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using Wander.Core;
using Wander.Core.Icons;
using Wander.Core.Logging;

namespace Wander.Platform.Windows.Icons;

/// <summary>
/// Provides system icons / thumbnails via the Win32 shell.
///
/// Strategy by size:
///  - <b>Small / Normal</b>: <c>SHGetFileInfo</c>. The shell bakes the
///    link-overlay arrow in at these sizes for free
///    (<c>SHGFI_LINKOVERLAY</c>).
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
/// Caching is in-memory only and keyed by extension for the small / normal
/// sizes (shared icons) and per-path for Large (each thumbnail is unique).
/// </summary>
public sealed class SystemIconProvider : IIconProvider {
    private readonly Dictionary<string, byte[]> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();


    public byte[]? GetIcon(string path, IconSize size) {
        if (string.IsNullOrEmpty(path)) {
            return null;
        }

        string key = BuildCacheKey(path, size);

        lock (_lock) {
            if (_cache.TryGetValue(key, out byte[]? cached)) {
                return cached;
            }
        }

        try {
            byte[]? bytes = LoadIcon(path, size);
            if (bytes is not null) {
                lock (_lock) {
                    _cache[key] = bytes;
                }
            }
            return bytes;
        } catch {
            return null;
        }
    }


    private static string BuildCacheKey(string path, IconSize size) {
        if (System.IO.Directory.Exists(path)) {
            return $"dir|{path}|{size}";
        }

        string ext = Path.GetExtension(path);

        // Shortcuts (.lnk) get a unique composite per file → cache per path.
        if (ext.Equals(".lnk", StringComparison.OrdinalIgnoreCase)) {
            return $"lnk|{path}|{size}";
        }

        // Large = jumbo path with thumbnails → unique per file.
        if (size == IconSize.Large) {
            return $"thumb|{path}";
        }

        if (string.IsNullOrEmpty(ext)) {
            return $"file|noext|{size}";
        }
        return $"ext|{ext.ToLowerInvariant()}|{size}";
    }

    private static byte[]? LoadIcon(string path, IconSize size) {
        return size switch {
            IconSize.Large => LoadJumboImage(path),
            _ => LoadShellIcon(path, size),
        };
    }


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
        Bitmap? baseBmp = LoadShellBitmapJumbo(path);
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
    /// Gets a 256-px base image (thumbnail-if-available, otherwise icon),
    /// without overlays. Returns a managed <see cref="Bitmap"/> in
    /// top-down 32-bpp ARGB so the caller can paint over it safely.
    /// </summary>
    private static Bitmap? LoadShellBitmapJumbo(string path) {
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

            var size = new SIZE { cx = JumboSize, cy = JumboSize };
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

            return HIconToBitmap(hIcon, shilSize);
        } finally {
            if (hIcon != IntPtr.Zero) {
                DestroyIcon(hIcon);
            }
            Marshal.ReleaseComObject(list);
        }
    }

    /// <summary>
    /// Extracts the colour DIB out of an <c>HICON</c> and returns it as
    /// a managed 32-bpp ARGB top-down <see cref="Bitmap"/>. See the
    /// note above for why we don't use <c>Icon.ToBitmap()</c>.
    /// </summary>
    private static Bitmap? HIconToBitmap(IntPtr hIcon, int shilSize) {
        var iconInfo = new ICONINFO();
        if (!GetIconInfo(hIcon, ref iconInfo)) {
            IconLog($"GetIconInfo failed for overlay (SHIL={shilSize})");
            return null;
        }

        try {
            if (iconInfo.hbmColor == IntPtr.Zero) {
                // 1-bit-per-pixel monochrome icon — overlays from
                // modern shell32 are always 32-bpp ARGB, so this
                // shouldn't fire in practice. Bail out rather than
                // synthesising colours from the mask.
                IconLog($"overlay icon has no colour bitmap (SHIL={shilSize})");
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
    private const int SIIGBF_RESIZETOFIT = 0x00000000;

    private const int SHIL_SMALL = 0x1;        // 16 × 16
    private const int SHIL_LARGE = 0x0;        // 32 × 32
    private const int SHIL_EXTRALARGE = 0x2;   // 48 × 48
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
