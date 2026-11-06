using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using System.IO.Enumeration;
using System.Runtime.InteropServices;
using Wander.Core;
using Wander.Core.Companions;
using Wander.Core.Diagnostics;
using Wander.Core.FileSystem;
using Wander.Core.Folders;
using Wander.Core.Icons;
using Wander.Core.Listing;
using Wander.Core.Logging;
using Wander.Core.Shell;
using DiskEntry = System.IO.Enumeration.FileSystemEntry;

namespace Wander.Platform.Windows.Icons;

/// <summary>
/// A folder's own picture, made of its files' pictures (PLAN F1, decisions
/// 2026-11-06) - for the two thumbnail steps; the small icons stay the
/// shell's. The shell's peek at a folder shows one file, the newest of any
/// type, so a folder of RAW files with fresher sidecars came up as a blank
/// sheet, and none of Wander's own pictures - RAW, TGA, models, a track's
/// shape - ever reached a folder.
///
/// <para>
/// One walk of the folder on a time budget picks the files
/// (<see cref="FolderPicture"/>); their pictures are the ones their own
/// tiles use, at the same step - memory, disk, Wander's renderers - and
/// <see cref="FolderThumbnail"/> lays them out. Null means "the shell's
/// turn": a folder inside an archive or on a device, one somebody gave an
/// icon (<c>desktop.ini</c>), one that cannot be read, and one on a network
/// or a removable drive that has not been opened this session - there a
/// walk is round trips, and the pictures of its files are not to hand.
/// </para>
///
/// <para>
/// The large step goes to disk under a key of what the picture shows
/// (<see cref="FolderPicture.Signature"/>): the folder's time and every
/// file on it. The folder's time alone would miss a picked file edited in
/// place.
/// </para>
/// </summary>
internal sealed class FolderPictures {
    /// <summary>
    /// The walks of one picture - the folder's, and its subfolders' when it
    /// has nothing of its own - stop here. A local folder of 10 000 files is
    /// read whole in about 5 ms, the same over SMB in about 25 (stand
    /// 2026-11-05); a folder past this is shown by the start it read.
    /// </summary>
    private const int WalkBudgetMs = 25;

    /// <summary>Entries one walk reads at most, whatever the clock says - the start of a folder of a million is as good as all of it.</summary>
    private const int WalkCap = 10_000;

    private readonly Func<string, IconSize, byte[]?> _fileIcon;
    private readonly Func<string, bool> _lasting;
    private readonly ThumbnailDiskCache? _disk;

    /// <summary>
    /// Whether a folder was read to its end the first time this session -
    /// kept, so a folder on the edge of the budget does not flip between
    /// "freshest" and "first read" from one draw to the next.
    /// </summary>
    private readonly ConcurrentDictionary<string, bool> _complete = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Folders on network and removable drives whose files' pictures were asked for this session - the ones opened.</summary>
    private readonly ConcurrentDictionary<string, byte> _visited = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Whether a drive is a network, removable or optical one, by its root; asked once a session.</summary>
    private readonly ConcurrentDictionary<string, bool> _farRoots = new(StringComparer.OrdinalIgnoreCase);


    /// <param name="fileIcon">A file's own picture at a step, as its tile has it.</param>
    /// <param name="lasting">Whether a file's picture is the one it will keep - not its type's icon for want of a codec: a folder showing one stays off the disk.</param>
    /// <param name="disk">Where the large step is kept between sessions; none, and every draw is fresh.</param>
    public FolderPictures(Func<string, IconSize, byte[]?> fileIcon, Func<string, bool> lasting, ThumbnailDiskCache? disk) {
        _fileIcon = fileIcon;
        _lasting = lasting;
        _disk = disk;
    }


    /// <summary>The folder's own picture as PNG, <paramref name="side"/> pixels square, or null for the shell's.</summary>
    public byte[]? Render(string folder, IconSize size, FolderLook look, int side) {
        if (size is not (IconSize.Medium or IconSize.Large) || !OnDisk(folder) || IsVolumeRoot(folder)) {
            return null;
        }

        bool far = IsFar(folder);
        if (far && !_visited.ContainsKey(folder)) {
            return null;
        }

        try {
            long deadline = Stopwatch.GetTimestamp() + (Stopwatch.Frequency * WalkBudgetMs / 1000);
            if (Walk(folder, deadline, out bool hasIni) is not { } read || (hasIni && SetsIcon(folder))) {
                return null;
            }

            int cards = FolderPicture.Cards(size, look.Grid);
            var companions = ServiceLocator.TryGet<CompanionResolver>() ?? CompanionResolver.Default;
            // Not on a far drive: every subfolder is a walk over the wire,
            // and none of them was opened.
            var picks = FolderPicture.Choose(folder, read, cards, companions,
                sub => far || Stopwatch.GetTimestamp() > deadline ? null : Walk(sub, deadline, out _));

            string? key = size == IconSize.Large && _disk is not null
                ? FolderPicture.Signature(folder, Directory.GetLastWriteTimeUtc(folder), look, cards, picks)
                : null;
            if (key is not null && _disk!.TryReadKeyed(key) is { } stored) {
                return stored;
            }

            return Compose(picks, size, look, side, key);
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or ExternalException) {
            Log.Info($"[icon] folder picture failed: {ex.GetType().Name} - {folder}");

            return null;
        }
    }


    /// <summary>
    /// A file's picture was asked for: on a network or removable drive its
    /// folder is now one that was opened. Returns that folder when it has
    /// just become one - its picture so far was the shell's, and the caller
    /// drops it.
    /// </summary>
    public string? NoteShown(string file) {
        if (!OnDisk(file) || Path.GetDirectoryName(file) is not { Length: > 0 } folder || !IsFar(folder)) {
            return null;
        }

        return _visited.TryAdd(folder, 0) ? folder : null;
    }


    /// <summary>A path the folder walk can read: not a shell name, not on a device, not inside an archive.</summary>
    private static bool OnDisk(string path) {
        return !path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase) && !DevicePath.Is(path) && !Archives.Inside(path);
    }

    /// <summary>A drive or a share itself - the computer's listing draws its picture, the drive's, not a folder's.</summary>
    private static bool IsVolumeRoot(string folder) {
        string? root = Path.GetPathRoot(folder);

        return root is not null
            && string.Equals(folder.TrimEnd('\\', '/'), root.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// One pass over the folder, as long as the budget and the cap allow;
    /// null when it cannot be read. Hidden entries are read as such - the
    /// choice leaves them out - and <c>desktop.ini</c> is noted on the way.
    /// The first reading of a folder this session decides how it ranks for
    /// the rest of it (<see cref="_complete"/>).
    /// </summary>
    private FolderRead? Walk(string folder, long deadline, out bool hasIni) {
        hasIni = false;
        var files = new List<FolderItem>();
        var folders = new List<FolderItem>();
        bool complete = true;
        try {
            var options = new EnumerationOptions { AttributesToSkip = 0, IgnoreInaccessible = true, ReturnSpecialDirectories = false };
            var entries = new FileSystemEnumerable<(FolderItem Item, bool IsFolder)>(folder, (ref DiskEntry e) => (
                new FolderItem(
                    e.FileName.ToString(), e.Length, e.LastWriteTimeUtc.UtcDateTime,
                    (e.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0),
                e.IsDirectory), options);
            foreach (var (item, isFolder) in entries) {
                if (files.Count + folders.Count >= WalkCap || Stopwatch.GetTimestamp() > deadline) {
                    complete = false;

                    break;
                }

                if (isFolder) {
                    folders.Add(item);
                } else {
                    hasIni |= item.Name.Equals(DesktopIni.FileName, StringComparison.OrdinalIgnoreCase);
                    files.Add(item);
                }
            }
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) {
            return null;
        }

        complete = _complete.GetOrAdd(folder, complete) && complete;

        return new FolderRead(files, folders, complete);
    }

    /// <summary>The folder's <c>desktop.ini</c> gives it an icon of its own.</summary>
    private static bool SetsIcon(string folder) {
        try {
            return DesktopIni.SetsIcon(File.ReadAllBytes(Path.Combine(folder, DesktopIni.FileName)));
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
            return false;
        }
    }

    /// <summary>
    /// Whether the folder is on a network share or a drive that comes and
    /// goes. A UNC path has no letter to ask about and is a share.
    /// </summary>
    private bool IsFar(string folder) {
        if (folder.StartsWith(@"\\", StringComparison.Ordinal)) {
            return true;
        }

        string root = Path.GetPathRoot(folder) ?? "";

        return root.Length > 0 && _farRoots.GetOrAdd(root, r =>
            ServiceLocator.TryGet<IVolumeInfoProvider>()?.KindOf(r) is VolumeKind.Network or VolumeKind.Removable or VolumeKind.Optical);
    }

    /// <summary>
    /// Lays the picked files' pictures out. A file whose picture did not
    /// come is left out of the drawing, and then the drawing is left off the
    /// disk: the next session may get it.
    /// </summary>
    private byte[] Compose(IReadOnlyList<FolderPick> picks, IconSize size, FolderLook look, int side, string? key) {
        var cards = new List<FolderCard>(picks.Count);
        bool whole = true;
        try {
            foreach (var pick in picks) {
                if (_fileIcon(pick.Path, size) is { } bytes && Decode(bytes) is { } picture) {
                    cards.Add(new FolderCard(picture, pick.Content, pick.Inside));
                    whole &= _lasting(pick.Path);
                } else {
                    whole = false;
                }
            }

            byte[] png;
            using (PerfLog.Measure("bg.folder-compose")) {
                png = FolderThumbnail.Paint(cards, look.Grid, side, FolderThumbnail.Nominal(look.Grid, size == IconSize.Medium));
            }
            if (key is not null && whole) {
                _disk!.WriteKeyed(key, png);
            }

            return png;
        } finally {
            foreach (var card in cards) {
                card.Picture.Dispose();
            }
        }
    }

    /// <summary>A picture off its bytes, copied loose of the stream GDI+ would otherwise hold for its lifetime; null for bytes it cannot read.</summary>
    private static Bitmap? Decode(byte[] bytes) {
        try {
            using var stream = new MemoryStream(bytes);
            using var read = new Bitmap(stream);

            return new Bitmap(read);
        } catch (Exception ex) when (ex is ArgumentException or OutOfMemoryException or ExternalException) {
            return null;
        }
    }
}
