using Wander.Core.FileSystem;

namespace Wander.Core.Preview;

/// <summary>How the pictures open full screen - see <see cref="FullscreenPlan"/>.</summary>
public enum FullscreenMode {
    /// <summary>One picture; the arrow keys walk the whole list.</summary>
    Single,

    /// <summary>Two pictures together, as the split of the preview pane shows them.</summary>
    Pair,

    /// <summary>One picture at a time; the arrow keys walk only the selected ones.</summary>
    Selection,
}


/// <summary>
/// What Enter or Space shows full screen from the gallery (PLAN Q5,
/// 2026-09-24). Pictures only: a selection with anything else in it opens
/// in its programs, as Enter always did. One picture is shown and the arrow
/// keys walk the list; two are shown together; more are walked one by one,
/// from the one the keyboard is on - the selection is the set to look
/// through, and opening them all in another program was never the wish.
/// </summary>
/// <param name="Mode">How they are shown.</param>
/// <param name="Pictures">The pictures, in the list's order.</param>
/// <param name="Start">The one shown first; for a pair, the first of the two.</param>
public sealed record FullscreenPlan(FullscreenMode Mode, IReadOnlyList<FileSystemEntry> Pictures, FileSystemEntry Start) {
    /// <summary>The plan for a selection, or null when it is empty or not all pictures.</summary>
    /// <param name="selection">What is selected, in whatever order the list reports it.</param>
    /// <param name="listing">The rows on screen: the order the pictures go in.</param>
    /// <param name="caret">The row the keyboard is on - where a walk of the selection starts.</param>
    public static FullscreenPlan? Of(
        IReadOnlyList<FileSystemEntry> selection, IReadOnlyList<FileSystemEntry> listing, string? caret) {
        if (selection.Count == 0 || !selection.All(PictureWalk.IsPicture)) {
            return null;
        }

        // A row not in the listing goes last; the sort keeps ties as they came.
        var order = new Dictionary<string, int>(listing.Count, StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < listing.Count; i++) {
            order.TryAdd(listing[i].FullPath, i);
        }
        var pictures = selection.OrderBy(e => order.TryGetValue(e.FullPath, out int at) ? at : int.MaxValue).ToArray();

        if (pictures.Length == 1) {
            return new FullscreenPlan(FullscreenMode.Single, pictures, pictures[0]);
        }
        if (pictures.Length == 2) {
            return new FullscreenPlan(FullscreenMode.Pair, pictures, pictures[0]);
        }

        var start = caret is null
            ? null
            : Array.Find(pictures, e => string.Equals(e.FullPath, caret, StringComparison.OrdinalIgnoreCase));

        return new FullscreenPlan(FullscreenMode.Selection, pictures, start ?? pictures[0]);
    }
}
