using Wander.Core.FileSystem;

namespace Wander.Core.Listing;

/// <summary>
/// Putting what sidecars say into the rows of a listing.
///
/// <para>
/// Here rather than beside the sidecar readers: reading one file's rating
/// is a question about that file, but walking a folder's rows and deciding
/// which of them to replace is a question about the listing — the same
/// question <see cref="ListingDiff"/> answers for a different reason. How
/// a rating is read stays the caller's business and arrives as a
/// delegate.
/// </para>
/// </summary>
public static class RatedListing {
    /// <summary>
    /// The same listing with <see cref="FileSystemEntry.Rating"/> filled in
    /// from each row's sidecar. Returns the list it was given, unchanged,
    /// when nothing in the folder has a rating — that is the common case,
    /// and it lets the caller skip the whole UI pass rather than reconcile
    /// a list against an identical copy of itself.
    ///
    /// <para>
    /// Cheap by construction: <paramref name="readRating"/> only touches
    /// rows that already carry a companion, so a folder with no sidecars
    /// costs no I/O at all, and a folder of RAW files costs one small text
    /// read per photo. This is meant to run on a worker thread after the
    /// listing has landed, not as part of it — the listing must not wait
    /// on it.
    /// </para>
    ///
    /// <para>
    /// A row that came in with a rating carried over from the screen
    /// (<see cref="CarryRatings"/>) gets what its sidecar says now, which
    /// may be nothing.
    /// </para>
    /// </summary>
    public static IReadOnlyList<FileSystemEntry> WithRatings(
        IReadOnlyList<FileSystemEntry> entries,
        Func<FileSystemEntry, SidecarRating?> readRating,
        CancellationToken ct = default) {
        List<FileSystemEntry>? rated = null;

        for (int i = 0; i < entries.Count; i++) {
            ct.ThrowIfCancellationRequested();

            var rating = readRating(entries[i]);
            if (rating is null && entries[i].Rating is null) {
                rated?.Add(entries[i]);
                continue;
            }

            rated ??= new List<FileSystemEntry>(entries.Take(i));
            rated.Add(entries[i] with { Rating = rating });
        }

        return rated ?? entries;
    }


    /// <summary>
    /// The folder's rows listed again, with the ratings the same rows have
    /// on screen (<paramref name="shown"/>, by path) until the pass above
    /// reads the sidecars again (2026-09-28). A listing knows no ratings of
    /// its own: without this every re-listing - F5, the watcher, another
    /// order - dropped the stars for a moment, a filter by stars hid every
    /// photograph and the selection with them, and an order by rating came
    /// by name first and jumped once the pass landed. Only rows that still
    /// have a companion take one - a rating lives nowhere else. Returns the
    /// list it was given when nothing on screen is rated.
    /// </summary>
    /// <param name="entries">The new listing, in the order <paramref name="sort"/> made without ratings.</param>
    /// <param name="shown">The rows on screen before it, ratings and all.</param>
    /// <param name="sort">The listing's order: by rating, it is made again with the carried ratings.</param>
    public static IReadOnlyList<FileSystemEntry> CarryRatings(
        IReadOnlyList<FileSystemEntry> entries, IReadOnlyList<FileSystemEntry> shown, SortOptions sort) {
        Dictionary<string, SidecarRating>? known = null;
        foreach (var row in shown) {
            if (row.Rating is { } rating) {
                known ??= new Dictionary<string, SidecarRating>(StringComparer.OrdinalIgnoreCase);
                known[row.FullPath] = rating;
            }
        }
        if (known is null) {
            return entries;
        }

        var carried = new List<FileSystemEntry>(entries.Count);
        foreach (var entry in entries) {
            carried.Add(entry.Rating is null && entry.HasCompanions && known.TryGetValue(entry.FullPath, out var rating)
                ? entry with { Rating = rating }
                : entry);
        }

        return sort.Key == SortKey.Rating ? EntryComparers.Sort(carried, sort) : carried;
    }
}
