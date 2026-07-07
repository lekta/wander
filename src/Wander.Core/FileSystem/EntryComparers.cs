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

    private static IComparer<T> Reverse<T>(IComparer<T> inner) {
        return Comparer<T>.Create((a, b) => inner.Compare(b, a));
    }
}
