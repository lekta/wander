using Wander.Core.FileSystem;

namespace Wander.Core.Rename;

public enum BatchRenameKind {
    /// <summary>Fewer than two items: F2 renames in place, the window has nothing to do.</summary>
    TooFew,
    Files,
    Folders,

    /// <summary>Files and folders together - refused, see <see cref="BatchRenameGate"/>.</summary>
    Mixed,
}


/// <summary>
/// Whether a selection may go to the batch-rename window at all. Two or
/// more items, and all of one kind: a folder that slipped into a selection
/// of photographs would be renamed with them, and a numbering pass over
/// "IMG_0001.jpg ... IMG_0040.jpg, Backup" is not something anybody asked
/// for. Either files or folders - the menu row says which is missing.
/// </summary>
public static class BatchRenameGate {
    public const string SelectTwoKey = "MenuReasonSelectTwo";
    public const string FilesOrFoldersKey = "MenuReasonFilesOrFolders";


    public static BatchRenameKind Classify(IReadOnlyList<FileSystemEntry> selection) {
        if (selection.Count < 2) {
            return BatchRenameKind.TooFew;
        }

        bool anyFolder = false;
        bool anyFile = false;
        foreach (var entry in selection) {
            if (entry.Kind == EntryKind.Directory) {
                anyFolder = true;
            } else {
                anyFile = true;
            }
        }

        if (anyFolder && anyFile) {
            return BatchRenameKind.Mixed;
        }

        return anyFolder ? BatchRenameKind.Folders : BatchRenameKind.Files;
    }


    /// <summary>Resource key explaining a refusal; null when the window may open.</summary>
    public static string? ReasonKey(BatchRenameKind kind) {
        return kind switch {
            BatchRenameKind.TooFew => SelectTwoKey,
            BatchRenameKind.Mixed => FilesOrFoldersKey,
            _ => null,
        };
    }
}
