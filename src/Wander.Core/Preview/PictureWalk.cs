using Wander.Core.FileSystem;

namespace Wander.Core.Preview;

/// <summary>
/// Walking the pictures of a list full screen (PLAN Q5): an arrow key goes
/// to the next picture the given way, past whatever is not one.
/// </summary>
public static class PictureWalk {
    /// <summary>A step to the first picture of the list - Home.</summary>
    public const int First = int.MinValue;

    /// <summary>A step to the last one - End.</summary>
    public const int Last = int.MaxValue;


    /// <summary>What full screen shows and walks: a file the pane draws as a picture, still or moving.</summary>
    public static bool IsPicture(FileSystemEntry entry) {
        return entry.Kind == EntryKind.File
            && PreviewRouter.Route(entry.FullPath) is PreviewRoute.Image or PreviewRoute.Animation;
    }

    /// <summary>Where <paramref name="path"/> stands in <paramref name="rows"/>, or -1.</summary>
    public static int IndexOf(IReadOnlyList<FileSystemEntry> rows, string path) {
        for (int i = 0; i < rows.Count; i++) {
            if (string.Equals(rows[i].FullPath, path, StringComparison.OrdinalIgnoreCase)) {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// The row to go to from the picture on show, or -1 when there is no
    /// picture that way: at an end the walk stays. <see cref="First"/> and
    /// <see cref="Last"/> go to the first and the last picture of the list.
    /// </summary>
    /// <param name="rows">The list, in its order.</param>
    /// <param name="current">The picture on show.</param>
    /// <param name="stood">
    /// Where it stood when it was last seen in <paramref name="rows"/>. A
    /// picture can leave the list while it is on show - a star set under
    /// the rating filter hides it - and the walk then goes on from its
    /// place: forward to the row that took it, back to the one before.
    /// </param>
    /// <param name="by">+1 or -1; <see cref="First"/> or <see cref="Last"/>.</param>
    /// <param name="skip">
    /// A picture the walk passes over: the left one of a pair, for the right
    /// one walked beside it (2026-09-24) - a picture next to itself compares
    /// nothing.
    /// </param>
    public static int Step(IReadOnlyList<FileSystemEntry> rows, string current, int stood, int by, string? skip = null) {
        int direction;
        int from;
        if (by is First or Last) {
            // Looked for inwards from its end.
            direction = by == First ? 1 : -1;
            from = by == First ? 0 : rows.Count - 1;
        } else {
            direction = by > 0 ? 1 : -1;
            int at = IndexOf(rows, current);
            int place = Math.Clamp(stood, 0, rows.Count);
            from = at >= 0 ? at + direction
                : direction > 0 ? place
                : place - 1;
        }

        for (int i = from; i >= 0 && i < rows.Count; i += direction) {
            if (IsPicture(rows[i]) && !string.Equals(rows[i].FullPath, skip, StringComparison.OrdinalIgnoreCase)) {
                return i;
            }
        }

        return -1;
    }
}
