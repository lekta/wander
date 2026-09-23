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
    /// <summary>
    /// Edits of any kind - rows gone, arrived, moved, rewritten - past which
    /// the plan is a rebuild. Every edit is a notification the bound list
    /// answers on the UI thread; a screenful or two of them is nothing,
    /// thousands are a stall - and so is the replay itself, each move a
    /// shift of the working copy.
    /// </summary>
    private const int MaxReconciledChanges = 256;


    /// <summary>
    /// Is the incoming listing a different list rather than this one with
    /// some rows gone, arrived, moved or rewritten - and if not, the edits.
    ///
    /// <para>
    /// The rows both listings have stay put when they keep their order: the
    /// longest run of them that does (a longest increasing subsequence of
    /// their new places, n log n) stays, and only the rest move. One photo
    /// whose date put it at the other end of the folder is one move - a walk
    /// that moved every row into line behind it took the whole folder, came
    /// out as a rebuild, and the rebuild's Reset put the scroll back at the
    /// top under the user's hands. So did comparing position by position,
    /// until 2026-09-21: one file deleted near the top pushed every row
    /// below it out of line.
    /// </para>
    ///
    /// <para>
    /// The rebuild stays for what it was meant for: nothing shared (another
    /// folder's rows), fewer than half of the shared rows in their order (the
    /// sort flipped), and a change too big to replay one notification at a
    /// time (a filter typed over five thousand rows, a checkout that rewrote
    /// the dates of thousands of files).
    /// </para>
    /// </summary>
    public static ListingDiffPlan Compute(
        IReadOnlyList<FileSystemEntry> current, IReadOnlyList<FileSystemEntry> incoming) {
        if (current.Count == 0 || incoming.Count == 0) {
            return ListingDiffPlan.Rebuild;
        }

        var placeOf = new Dictionary<string, int>(incoming.Count, StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < incoming.Count; i++) {
            placeOf.TryAdd(incoming[i].FullPath, i);
        }

        // The shared rows in the order they stand, by where they go.
        var order = new List<int>(current.Count);
        var sharedPaths = new List<string>(current.Count);
        int rewritten = 0;
        foreach (var row in current) {
            if (placeOf.TryGetValue(row.FullPath, out int at)) {
                order.Add(at);
                sharedPaths.Add(row.FullPath);
                if (!row.SaysTheSameAs(incoming[at])) {
                    rewritten++;
                }
            }
        }

        int shared = order.Count;
        if (shared == 0) {
            return ListingDiffPlan.Rebuild;
        }

        var stays = LongestRun(order);
        var moving = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int k = 0; k < shared; k++) {
            if (!stays[k]) {
                moving.Add(sharedPaths[k]);
            }
        }

        int changed = (current.Count - shared) + (incoming.Count - shared) + moving.Count + rewritten;
        if ((shared - moving.Count) * 2 < shared || changed > MaxReconciledChanges) {
            return ListingDiffPlan.Rebuild;
        }

        return ListingDiffPlan.Of(Replay(current, incoming, placeOf, moving));
    }


    /// <summary>
    /// The edits, against a working copy so every index means "the list as
    /// it stands mid-replay". A row that is going to move and stands where
    /// another belongs steps out to the end, out of the way, and comes back
    /// to its own place when its turn comes: two moves for it, none for the
    /// rows around it.
    /// </summary>
    private static List<ListingEdit> Replay(
        IReadOnlyList<FileSystemEntry> current, IReadOnlyList<FileSystemEntry> incoming,
        Dictionary<string, int> placeOf, HashSet<string> moving) {
        var work = new List<FileSystemEntry>(current);
        var edits = new List<ListingEdit>();
        var standing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int i = work.Count - 1; i >= 0; i--) {
            if (!placeOf.ContainsKey(work[i].FullPath)) {
                edits.Add(new ListingEdit(ListingEditKind.RemoveAt, i));
                work.RemoveAt(i);
            } else {
                standing.Add(work[i].FullPath);
            }
        }

        // The rows stepped out to the end, waiting there for their turn.
        int parked = 0;
        for (int i = 0; i < incoming.Count; i++) {
            var want = incoming[i];
            while (i < work.Count - parked && moving.Contains(work[i].FullPath)
                && !string.Equals(work[i].FullPath, want.FullPath, StringComparison.OrdinalIgnoreCase)) {
                edits.Add(new ListingEdit(ListingEditKind.Move, i, work.Count - 1));
                var aside = work[i];
                work.RemoveAt(i);
                work.Add(aside);
                parked++;
            }

            int at = standing.Contains(want.FullPath) ? IndexOfPath(work, want.FullPath, i) : -1;
            if (at < 0) {
                edits.Add(new ListingEdit(ListingEditKind.Insert, i, Entry: want));
                work.Insert(i, want);

                continue;
            }
            if (at != i) {
                if (at >= work.Count - parked) {
                    parked--;
                }
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

        return edits;
    }

    /// <summary>
    /// Which of <paramref name="order"/> make up a longest increasing run -
    /// patience sorting, with the chain walked back from its tail.
    /// </summary>
    private static bool[] LongestRun(IReadOnlyList<int> order) {
        var tails = new List<int>();
        var previous = new int[order.Count];
        for (int k = 0; k < order.Count; k++) {
            int lo = 0;
            int hi = tails.Count;
            while (lo < hi) {
                int mid = (lo + hi) / 2;
                if (order[tails[mid]] < order[k]) {
                    lo = mid + 1;
                } else {
                    hi = mid;
                }
            }
            previous[k] = lo > 0 ? tails[lo - 1] : -1;
            if (lo == tails.Count) {
                tails.Add(k);
            } else {
                tails[lo] = k;
            }
        }

        var stays = new bool[order.Count];
        for (int k = tails.Count > 0 ? tails[^1] : -1; k >= 0; k = previous[k]) {
            stays[k] = true;
        }

        return stays;
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
