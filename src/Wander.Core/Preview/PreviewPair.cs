using Wander.Core.FileSystem;

namespace Wander.Core.Preview;

/// <summary>
/// When the preview pane splits in two. Exactly two files selected and both
/// of them something the pane can draw - then each gets half the pane, in
/// the order the listing shows them rather than the order they were
/// clicked in, so the left one is the upper one in the list whichever way
/// the selection was made.
/// </summary>
public static class PreviewPair {
    /// <summary>The two files to show side by side, or null when the selection is not such a pair.</summary>
    /// <param name="selection">What is selected, in whatever order the list reports it.</param>
    /// <param name="listing">The rows on screen; decides which of the two comes first.</param>
    public static (FileSystemEntry First, FileSystemEntry Second)? Of(
        IReadOnlyList<FileSystemEntry> selection, IReadOnlyList<FileSystemEntry> listing) {
        if (selection.Count != 2 || !CanShow(selection[0]) || !CanShow(selection[1])) {
            return null;
        }

        int a = IndexOf(listing, selection[0]);
        int b = IndexOf(listing, selection[1]);

        return a <= b ? (selection[0], selection[1]) : (selection[1], selection[0]);
    }


    private static bool CanShow(FileSystemEntry entry) {
        return entry.Kind == EntryKind.File
            && PreviewRouter.Route(entry.FullPath) != PreviewRoute.Unsupported;
    }

    /// <summary>Position in the listing; a row that is not there sorts last.</summary>
    private static int IndexOf(IReadOnlyList<FileSystemEntry> listing, FileSystemEntry entry) {
        for (int i = 0; i < listing.Count; i++) {
            if (string.Equals(listing[i].FullPath, entry.FullPath, StringComparison.OrdinalIgnoreCase)) {
                return i;
            }
        }

        return int.MaxValue;
    }
}
