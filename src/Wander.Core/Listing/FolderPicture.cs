using System.Text;
using Wander.Core.Companions;
using Wander.Core.FileSystem;
using Wander.Core.Icons;
using Wander.Core.Preview;

namespace Wander.Core.Listing;

/// <summary>The kind of file a folder's own picture shows one of; <see cref="None"/> for what it never shows.</summary>
public enum FolderContent {
    None,
    Program,
    Picture,
    Video,
    Sound,
    Document,
    Model,
    Font,
}


/// <summary>One entry of a folder as its walk read it - a fact, in the order the disk handed it over.</summary>
public readonly record struct FolderItem(string Name, long Size, DateTime ModifiedUtc, bool Hidden = false);


/// <summary>
/// One walk of a folder: its files and its subfolders in the order the disk
/// gave them, and whether the walk got to the end (<see cref="Complete"/>)
/// or stopped on its budget.
/// </summary>
public sealed record FolderRead(IReadOnlyList<FolderItem> Files, IReadOnlyList<FolderItem> Folders, bool Complete);


/// <summary>
/// A file chosen for a folder's picture: where it is, what it is, the
/// reading that keys the picture, and whether it was found one level down
/// (<see cref="Inside"/>) for want of anything in the folder itself.
/// </summary>
public sealed record FolderPick(string Path, FolderItem Item, FolderContent Content, bool Inside = false);


/// <summary>
/// Which files a folder's own picture shows (PLAN F1, decisions of
/// 2026-11-06): one of each kind the user works with, then more of the
/// same until the places are filled. The program a folder installs goes
/// first; settings, logs, libraries, archives, sidecars, copies and the
/// other programs never go at all. Facts in, choice out: the walk and the
/// drawing are the platform's.
///
/// <para>
/// A folder read to its end is ranked by freshness - what the user worked
/// on last; one the walk gave up on is ranked by what it did read, in the
/// order read: on NTFS the first by name, on a FAT card the first written.
/// A folder with nothing of its own is shown by its subfolders, one file
/// from each, a level down and no deeper.
/// </para>
/// </summary>
public static class FolderPicture {
    /// <summary>Places on the gallery's face of a folder.</summary>
    public const int GridCards = 4;

    /// <summary>Subfolders looked into for a folder with nothing of its own: past these, the walks cost more than the picture says.</summary>
    public const int MaxSubfolders = 8;

    /// <summary>
    /// Names of the programs that come with a program rather than being it
    /// (decision 2026-11-06), matched on the name without its extension.
    /// </summary>
    private static readonly string[] _helperPrefixes = { "unins", "uninstall", "setup" };

    private static readonly string[] _helperParts = { "update", "crash", "report", "helper", "service", "redist" };

    private static readonly HashSet<string> _videos = new(PreviewRouter.Video.Concat(new[] { ".mts", ".m2ts" }), StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> _fonts = new(StringComparer.OrdinalIgnoreCase) {
        ".ttf", ".otf", ".ttc",
    };

    /// <summary>
    /// What a user reads: PDF, office, books, prose. Code, configs, logs and
    /// tables of data are not here - they are what a project or a program
    /// keeps beside its work, and every one of them looks the same.
    /// </summary>
    private static readonly HashSet<string> _documents = new(StringComparer.OrdinalIgnoreCase) {
        ".pdf", ".doc", ".docx", ".rtf", ".odt",
        ".xls", ".xlsx", ".ods",
        ".ppt", ".pptx", ".odp",
        ".fb2", ".epub", ".cbz", ".djvu",
        ".txt", ".md", ".markdown",
    };


    /// <summary>How many files a picture of this size shows: four on the gallery's face, two cards in a tile, three in an icon.</summary>
    public static int Cards(IconSize size, bool grid) {
        return grid ? GridCards : size == IconSize.Medium ? 2 : 3;
    }


    /// <summary>The kind a file is by its extension alone; every <c>.exe</c> is a <see cref="FolderContent.Program"/> here, the main one or not.</summary>
    public static FolderContent ContentOf(string name) {
        string ext = Path.GetExtension(name);
        if (ext.Equals(".exe", StringComparison.OrdinalIgnoreCase)) {
            return FolderContent.Program;
        }
        if (ImageFormats.All.Contains(ext) || ext.Equals(".svg", StringComparison.OrdinalIgnoreCase)) {
            return FolderContent.Picture;
        }
        if (_videos.Contains(ext)) {
            return FolderContent.Video;
        }
        if (AudioTags.Extensions.Contains(ext)) {
            return FolderContent.Sound;
        }
        if (MeshFile.Extensions.Contains(ext)) {
            return FolderContent.Model;
        }
        if (_fonts.Contains(ext)) {
            return FolderContent.Font;
        }

        return _documents.Contains(ext) ? FolderContent.Document : FolderContent.None;
    }


    /// <summary>
    /// The program the folder is for, among its files: not an uninstaller,
    /// an updater, a crash reporter, a helper, a service, a setup or a
    /// redistributable; of the rest the one named as the folder, else the
    /// biggest, else the first. Null when there is none.
    /// </summary>
    public static FolderItem? MainProgram(string folderName, IEnumerable<FolderItem> files) {
        FolderItem? biggest = null;
        foreach (var file in files) {
            if (ContentOf(file.Name) != FolderContent.Program || IsHelper(file.Name)) {
                continue;
            }

            if (Path.GetFileNameWithoutExtension(file.Name).Equals(folderName, StringComparison.OrdinalIgnoreCase)) {
                return file;
            }
            if (biggest is null || file.Size > biggest.Value.Size) {
                biggest = file;
            }
        }

        return biggest;
    }


    /// <summary>
    /// The files to show, front card first: the main program, then the best
    /// file of each kind in the order those best files rank, then the next
    /// best of any kind, up to <paramref name="cards"/>.
    /// </summary>
    public static IReadOnlyList<FolderPick> Pick(
        string folder, IReadOnlyList<FolderItem> files, bool complete, int cards, CompanionResolver companions) {
        var picks = new List<FolderPick>(Math.Max(0, cards));
        if (cards <= 0) {
            return picks;
        }

        var programs = new List<FolderItem>();
        var weighty = new List<(FolderItem Item, FolderContent Content)>();
        foreach (var file in Rank(files, complete)) {
            var content = ContentOf(file.Name);
            if (content == FolderContent.None || !IsOwn(file, companions)) {
                continue;
            }

            if (content == FolderContent.Program) {
                programs.Add(file);
            } else {
                weighty.Add((file, content));
            }
        }

        if (MainProgram(Path.GetFileName(folder.TrimEnd('\\', '/')), programs) is { } main) {
            picks.Add(new FolderPick(Path.Combine(folder, main.Name), main, FolderContent.Program));
        }
        foreach (var (file, content) in weighty) {
            if (picks.Count < cards && !picks.Exists(p => p.Content == content)) {
                picks.Add(new FolderPick(Path.Combine(folder, file.Name), file, content));
            }
        }
        foreach (var (file, content) in weighty) {
            if (picks.Count < cards && !picks.Exists(p => p.Item.Name == file.Name)) {
                picks.Add(new FolderPick(Path.Combine(folder, file.Name), file, content));
            }
        }

        return picks;
    }


    /// <summary>
    /// The files to show for a folder walked as <paramref name="read"/>:
    /// its own, or - with nothing of its own - the best file of each of its
    /// subfolders in their rank, read through <paramref name="readSubfolder"/>.
    /// That returns null once the walks are out of time; the subfolders read
    /// by then are what is shown.
    /// </summary>
    public static IReadOnlyList<FolderPick> Choose(
        string folder, FolderRead read, int cards, CompanionResolver companions, Func<string, FolderRead?> readSubfolder) {
        var own = Pick(folder, read.Files, read.Complete, cards, companions);
        if (own.Count > 0) {
            return own;
        }

        var picks = new List<FolderPick>(cards);
        int tried = 0;
        foreach (var sub in Rank(read.Folders, read.Complete)) {
            if (picks.Count >= cards || tried >= MaxSubfolders) {
                break;
            }
            if (sub.Hidden || ServiceFiles.IsFolderName(sub.Name)) {
                continue;
            }

            tried++;
            string path = Path.Combine(folder, sub.Name);
            if (readSubfolder(path) is not { } inside) {
                break;
            }
            if (Pick(path, inside.Files, inside.Complete, 1, companions) is [var best]) {
                picks.Add(best with { Inside = true });
            }
        }

        return picks;
    }


    /// <summary>
    /// What the picture is made of, as a cache key: the folder's own
    /// reading, the look, and every file on it with its reading - a file
    /// edited in place is a new key, though the folder's time stays.
    /// </summary>
    public static string Signature(string folder, DateTime folderModifiedUtc, FolderLook look, int cards, IReadOnlyList<FolderPick> picks) {
        var key = new StringBuilder("folder|")
            .Append(folder.ToLowerInvariant()).Append('|')
            .Append(folderModifiedUtc.Ticks).Append('|')
            .Append(cards).Append(look.KeySuffix);
        foreach (var pick in picks) {
            key.Append('|').Append(pick.Path.ToLowerInvariant())
                .Append('|').Append(pick.Item.ModifiedUtc.Ticks)
                .Append('|').Append(pick.Item.Size);
        }

        return key.ToString();
    }


    /// <summary>Freshest first when the walk saw everything; otherwise as read. Stable either way: ties keep the disk's order.</summary>
    private static IEnumerable<FolderItem> Rank(IReadOnlyList<FolderItem> items, bool complete) {
        return complete ? items.OrderByDescending(i => i.ModifiedUtc) : items;
    }

    /// <summary>
    /// The user's own file rather than the system's, a sidecar, a copy or a
    /// backup - the same test the gallery counts pictures by
    /// (<see cref="ImageFolderProbe"/>), and the Blender backups past it.
    /// </summary>
    private static bool IsOwn(FolderItem file, CompanionResolver companions) {
        return !file.Hidden
            && !ServiceFiles.IsFileName(file.Name)
            && ImageFolderProbe.IsContent(file.Name, companions)
            && !MeshFile.IsBackup(file.Name);
    }

    private static bool IsHelper(string name) {
        string stem = Path.GetFileNameWithoutExtension(name);
        foreach (string prefix in _helperPrefixes) {
            if (stem.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) {
                return true;
            }
        }
        foreach (string part in _helperParts) {
            if (stem.Contains(part, StringComparison.OrdinalIgnoreCase)) {
                return true;
            }
        }

        return false;
    }
}
