namespace Wander.Core.FileSystem;

/// <summary>
/// Comparer factory for <see cref="FileSystemEntry"/>. Pure logic — given
/// a <see cref="SortOptions"/> and an <see cref="IComparer{String}"/> for
/// names (Windows passes a <c>StrCmpLogicalW</c>-based one to get Explorer's
/// natural ordering; tests fall back to ordinal), it produces a comparer
/// applied over the folderLikes / files buckets in
/// <see cref="IFileSystem.Enumerate"/>.
///
/// <para>
/// Tie-breaker is always the name comparer — equal sizes / dates / types
/// would otherwise reorder unpredictably between calls, which is jarring
/// when refreshing the same folder. Reversed (Descending) ordering also
/// reverses the tiebreak, so a Z→A name pass within an otherwise-equal
/// group reads naturally.
/// </para>
/// </summary>
public static class EntryComparers {

    /// <summary>
    /// Build a comparer for the primary key inside a single bucket
    /// (folderLikes or files). Callers handle the GroupFoldersFirst split
    /// themselves — see <see cref="SortOptions.GroupFoldersFirst"/>.
    /// </summary>
    public static IComparer<FileSystemEntry> Build(SortOptions options, IComparer<string>? nameComparer = null) {
        var nc = nameComparer ?? StringComparer.OrdinalIgnoreCase;

        IComparer<FileSystemEntry> tiebreaker = Comparer<FileSystemEntry>.Create((a, b) =>
            nc.Compare(a.Name, b.Name));

        IComparer<FileSystemEntry> primary = options.Key switch {
            SortKey.Name => tiebreaker,
            SortKey.ModifiedDate => Comparer<FileSystemEntry>.Create((a, b) => a.ModifiedUtc.CompareTo(b.ModifiedUtc)),
            SortKey.Size => Comparer<FileSystemEntry>.Create((a, b) => (a.Size ?? 0L).CompareTo(b.Size ?? 0L)),
            SortKey.Type => Comparer<FileSystemEntry>.Create((a, b) => string.Compare(
                                        System.IO.Path.GetExtension(a.Name),
                                        System.IO.Path.GetExtension(b.Name),
                                        StringComparison.OrdinalIgnoreCase)),
            // Unrated sorts as zero rather than below it: "no stars" and
            // "explicitly zero stars" are the same statement about a photo,
            // and a folder half-way through its rating pass must not jump
            // its rows around as nulls turn into zeroes.
            SortKey.Rating => Comparer<FileSystemEntry>.Create((a, b) =>
                                        (a.Rating?.Rank ?? 0).CompareTo(b.Rating?.Rank ?? 0)),
            _ => tiebreaker,
        };

        // Compose primary + name tiebreaker so equal keys order by name.
        IComparer<FileSystemEntry> composed = options.Key == SortKey.Name
            ? primary
            : Comparer<FileSystemEntry>.Create((a, b) => {
                int c = primary.Compare(a, b);
                return c != 0 ? c : tiebreaker.Compare(a, b);
            });

        return options.Ascending ? composed : Reverse(composed);
    }

    /// <summary>
    /// Sorts a whole listing: the folders-first split plus
    /// <see cref="Build"/> inside each bucket. Both the enumerating
    /// filesystem and the pass that re-sorts after ratings arrive go
    /// through here, so a listing ordered once and re-ordered later cannot
    /// end up in two different orders.
    /// </summary>
    public static IReadOnlyList<FileSystemEntry> Sort(
        IReadOnlyList<FileSystemEntry> entries, SortOptions options, IComparer<string>? nameComparer = null) {
        var comparer = Build(options, nameComparer);

        if (!options.GroupFoldersFirst) {
            var merged = new List<FileSystemEntry>(entries);
            merged.Sort(comparer);

            return merged;
        }

        var folderLikes = new List<FileSystemEntry>();
        var files = new List<FileSystemEntry>();
        foreach (var entry in entries) {
            (entry.IsFolderLike ? folderLikes : files).Add(entry);
        }
        folderLikes.Sort(comparer);
        files.Sort(comparer);

        var result = new List<FileSystemEntry>(folderLikes.Count + files.Count);
        result.AddRange(folderLikes);
        result.AddRange(files);

        return result;
    }


    private static IComparer<T> Reverse<T>(IComparer<T> inner) {
        return Comparer<T>.Create((a, b) => inner.Compare(b, a));
    }
}
