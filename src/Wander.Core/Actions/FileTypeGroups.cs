using Wander.Core.FileSystem;
using Wander.Core.Icons;
using Wander.Core.Preview;

namespace Wander.Core.Actions;

/// <summary>The kinds of file an action can be offered for.</summary>
public enum FileTypeGroup {
    All,
    Images,
    Video,
    Audio,
    TextAndCode,
    Documents,
    Archives,
    Folders,
}


/// <summary>
/// One table of what counts as a picture, a video, a document, so an action
/// "for video" and the preview pane agree about a <c>.mkv</c>. Nothing here
/// is a new list: pictures are <see cref="ImageFormats"/>, video and text
/// are the preview router's, audio is what the pane plays. The two lists
/// that are new - documents and archives - exist because nothing in Core
/// had needed them before.
/// </summary>
public static class FileTypeGroups {
    private static readonly IReadOnlySet<string> _none = new HashSet<string>();

    /// <summary>The document formats an office converter takes.</summary>
    public static readonly IReadOnlySet<string> Documents = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
        ".pdf", ".doc", ".docx", ".odt", ".rtf",
        ".xls", ".xlsx", ".ods",
        ".ppt", ".pptx", ".odp",
        ".epub", ".fb2",
    };

    /// <summary>
    /// Archives by extension. The shell's own answer to "is this an archive"
    /// is per machine (<c>IShellNamespace</c>); a menu built for every
    /// right-click cannot afford to ask, and the common formats are known.
    /// </summary>
    public static readonly IReadOnlySet<string> Archives = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
        ".zip", ".7z", ".rar", ".tar", ".gz", ".tgz", ".bz2", ".xz",
    };

    /// <summary>
    /// The groups a mixed selection is tested against, most specific first.
    /// Order matters where lists overlap: a <c>.svg</c> is code before it
    /// is anything else here.
    /// </summary>
    private static readonly FileTypeGroup[] _fileGroups = {
        FileTypeGroup.Images, FileTypeGroup.Video, FileTypeGroup.Audio,
        FileTypeGroup.TextAndCode, FileTypeGroup.Documents, FileTypeGroup.Archives,
    };


    public static bool Matches(FileTypeGroup group, FileSystemEntry entry) {
        return group switch {
            FileTypeGroup.All => true,
            FileTypeGroup.Folders => entry.Kind == EntryKind.Directory,
            _ => entry.Kind == EntryKind.File && Extensions(group).Contains(Path.GetExtension(entry.Name)),
        };
    }


    /// <summary>The extensions of a group; empty for the two that are not about extensions.</summary>
    public static IReadOnlySet<string> Extensions(FileTypeGroup group) {
        return group switch {
            FileTypeGroup.Images => ImageFormats.All,
            FileTypeGroup.Video => PreviewRouter.Video,
            FileTypeGroup.Audio => AudioTags.Extensions,
            FileTypeGroup.TextAndCode => PreviewRouter.TextLike,
            FileTypeGroup.Documents => Documents,
            FileTypeGroup.Archives => Archives,
            _ => _none,
        };
    }


    /// <summary>
    /// The one group every item of a selection belongs to, or null for an
    /// empty or mixed selection. What the header menu's caption counts:
    /// "3 videos" rather than "3 items".
    /// </summary>
    public static FileTypeGroup? Classify(IReadOnlyList<FileSystemEntry> selection) {
        if (selection.Count == 0) {
            return null;
        }
        if (selection.All(e => e.Kind == EntryKind.Directory)) {
            return FileTypeGroup.Folders;
        }

        foreach (var group in _fileGroups) {
            if (selection.All(e => Matches(group, e))) {
                return group;
            }
        }

        return null;
    }


    /// <summary>
    /// Resource key of the group's name, lower case - what a type picker
    /// lists. The menu caption has its own, counted, forms.
    /// </summary>
    public static string NameKey(FileTypeGroup group) {
        return group switch {
            FileTypeGroup.Images => "FileTypeImages",
            FileTypeGroup.Video => "FileTypeVideo",
            FileTypeGroup.Audio => "FileTypeAudio",
            FileTypeGroup.TextAndCode => "FileTypeTextAndCode",
            FileTypeGroup.Documents => "FileTypeDocuments",
            FileTypeGroup.Archives => "FileTypeArchives",
            FileTypeGroup.Folders => "FileTypeFolders",
            _ => "FileTypeAll",
        };
    }
}


/// <summary>
/// Which files an action is for: a group, or a mask such as
/// <c>*.psd;*.ai</c> for a type no group covers. A non-empty mask wins over
/// the group, and a mask never matches a folder.
/// </summary>
public sealed record FileTypeSelector(FileTypeGroup Group = FileTypeGroup.All, string Mask = "") {
    public bool HasMask => Mask.Trim().Length > 0;


    public bool Matches(FileSystemEntry entry) {
        if (!HasMask) {
            return FileTypeGroups.Matches(Group, entry);
        }
        if (entry.Kind != EntryKind.File) {
            return false;
        }

        string extension = Path.GetExtension(entry.Name);
        foreach (string part in Mask.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) {
            if (part == "*" || part == "*.*") {
                return true;
            }
            // "*.psd", ".psd" and "psd" all mean the same thing.
            string wanted = part.TrimStart('*');
            if (!wanted.StartsWith('.')) {
                wanted = "." + wanted;
            }
            if (string.Equals(wanted, extension, StringComparison.OrdinalIgnoreCase)) {
                return true;
            }
        }

        return false;
    }
}
