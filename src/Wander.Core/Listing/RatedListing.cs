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
    /// </summary>
    public static IReadOnlyList<FileSystemEntry> WithRatings(
        IReadOnlyList<FileSystemEntry> entries,
        Func<FileSystemEntry, SidecarRating?> readRating,
        CancellationToken ct = default) {
        List<FileSystemEntry>? rated = null;

        for (int i = 0; i < entries.Count; i++) {
            ct.ThrowIfCancellationRequested();

            var rating = readRating(entries[i]);
            if (rating is null) {
                rated?.Add(entries[i]);
                continue;
            }

            rated ??= new List<FileSystemEntry>(entries.Take(i));
            rated.Add(entries[i] with { Rating = rating });
        }

        return rated ?? entries;
    }
}
