using Wander.Core.FileSystem;

namespace Wander.Core.Listing;

/// <summary>One step of reconciling the rows on screen with a fresh listing.</summary>
public enum ListingEditKind {
    /// <summary>Remove the row at <see cref="ListingEdit.Index"/>.</summary>
    RemoveAt,

    /// <summary>Insert <see cref="ListingEdit.Entry"/> at <see cref="ListingEdit.Index"/>.</summary>
    Insert,

    /// <summary>Move the row at <see cref="ListingEdit.Index"/> to <see cref="ListingEdit.ToIndex"/>.</summary>
    Move,

    /// <summary>
    /// Replace the row at <see cref="ListingEdit.Index"/> with
    /// <see cref="ListingEdit.Entry"/> — same file, different facts. The one
    /// case where a surviving row loses its container.
    /// </summary>
    Replace,
}


/// <summary>
/// A single edit against the live list. Indices refer to the list as it
/// stands <em>when this edit is applied</em>, with every earlier edit of the
/// plan already in — the plan replays in order or not at all.
/// </summary>
public readonly record struct ListingEdit(
    ListingEditKind Kind, int Index, int ToIndex = -1, FileSystemEntry? Entry = null);


/// <summary>
/// What reconciling the rows with a fresh listing amounts to: either a
/// wholesale rebuild, or an ordered list of edits.
/// </summary>
public sealed class ListingDiffPlan {
    internal static readonly ListingDiffPlan Rebuild = new(true, Array.Empty<ListingEdit>());


    private ListingDiffPlan(bool wholesale, IReadOnlyList<ListingEdit> edits) {
        Wholesale = wholesale;
        Edits = edits;
    }


    /// <summary>
    /// True when so little of the incoming listing lines up with what is on
    /// screen that reconciling it row by row is pointless — flipping the
    /// sort moves every row anyway, and the straight rebuild is both cheaper
    /// and no more disruptive than shuffling the collection item by item.
    /// </summary>
    public bool Wholesale { get; }

    /// <summary>The edits, in application order. Empty for a wholesale plan.</summary>
    public IReadOnlyList<ListingEdit> Edits { get; }


    internal static ListingDiffPlan Of(IReadOnlyList<ListingEdit> edits) {
        return new ListingDiffPlan(false, edits);
    }
}


/// <summary>
/// Reconciles a listing with a fresh one instead of clearing and refilling
/// it. Rows that did not change produce no edit and keep their containers —
/// that is what stops the list blinking on every refresh and what lets the
/// selection survive a rename or a delete.
///
/// <para>
/// Pure: reads two lists, returns a plan, touches nothing. The view model
/// replays the plan against the bound collection; this is the half worth
/// testing, and it is the half that used to live inline in the view model
/// where no test could reach it.
/// </para>
/// </summary>
public static class ListingDiff {
    public static ListingDiffPlan Compute(
        IReadOnlyList<FileSystemEntry> current, IReadOnlyList<FileSystemEntry> incoming) {
        if (IsWholesaleChange(current, incoming)) {
            return ListingDiffPlan.Rebuild;
        }

        // The plan's indices must mean "the list as it stands mid-replay",
        // so the algorithm runs against a working copy and records what it
        // does to it.
        var work = new List<FileSystemEntry>(current);
        var edits = new List<ListingEdit>();

        var wanted = new HashSet<string>(incoming.Select(e => e.FullPath), StringComparer.OrdinalIgnoreCase);
        for (int i = work.Count - 1; i >= 0; i--) {
            if (!wanted.Contains(work[i].FullPath)) {
                edits.Add(new ListingEdit(ListingEditKind.RemoveAt, i));
                work.RemoveAt(i);
            }
        }

        for (int i = 0; i < incoming.Count; i++) {
            var want = incoming[i];
            int at = IndexOfPath(work, want.FullPath, i);
            if (at < 0) {
                edits.Add(new ListingEdit(ListingEditKind.Insert, i, Entry: want));
                work.Insert(i, want);

                continue;
            }
            if (at != i) {
                edits.Add(new ListingEdit(ListingEditKind.Move, at, i));
                var moved = work[at];
                work.RemoveAt(at);
                work.Insert(i, moved);
            }
            // Same file, different facts (size, timestamp, sidecars): the row
            // has to show the new ones.
            if (!work[i].SaysTheSameAs(want)) {
                edits.Add(new ListingEdit(ListingEditKind.Replace, i, Entry: want));
                work[i] = want;
            }
        }

        return ListingDiffPlan.Of(edits);
    }


    private static bool IsWholesaleChange(
        IReadOnlyList<FileSystemEntry> current, IReadOnlyList<FileSystemEntry> incoming) {
        if (current.Count == 0 || incoming.Count == 0) {
            return true;
        }

        int aligned = 0;
        int common = Math.Min(current.Count, incoming.Count);
        for (int i = 0; i < common; i++) {
            if (string.Equals(current[i].FullPath, incoming[i].FullPath, StringComparison.OrdinalIgnoreCase)) {
                aligned++;
            }
        }

        return aligned * 2 < Math.Max(current.Count, incoming.Count);
    }


    private static int IndexOfPath(List<FileSystemEntry> rows, string path, int from) {
        for (int i = from; i < rows.Count; i++) {
            if (string.Equals(rows[i].FullPath, path, StringComparison.OrdinalIgnoreCase)) {
                return i;
            }
        }

        return -1;
    }
}
