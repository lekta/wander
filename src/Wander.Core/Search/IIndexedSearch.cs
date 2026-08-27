namespace Wander.Core.Search;

/// <summary>
/// A search answered by an index somebody else already maintains — on
/// Windows, the catalogue behind Explorer's own search box.
///
/// <para>
/// Wander keeps no index of its own, and the measurements say it should
/// not: a folder tree the size of a source repository is scanned in a
/// couple of hundred milliseconds, which no index can meaningfully beat
/// once you count what it costs to build and keep current. The one case
/// scanning cannot serve is "somewhere on this machine", and there the
/// system index answers in tens of milliseconds for free. Reaching for it
/// rather than growing our own is the "integrate with the system" pillar
/// applied to search.
/// </para>
///
/// <para>
/// The price is honesty about coverage: the index holds what Windows was
/// told to index, in the formats it has filters for. A result set from
/// here is therefore reported as coming from the index, not as the truth
/// about the disk.
/// </para>
/// </summary>
public interface IIndexedSearch {
    /// <summary>
    /// False when there is no usable index — the service is off, or this
    /// build is not running on Windows. Callers offer the scope only when
    /// this is true, rather than offering it and failing.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Full paths the index believes match. Ordered by whatever the
    /// provider considers relevant; the caller re-sorts.
    /// </summary>
    /// <param name="query">What the user typed.</param>
    /// <param name="scopePath">Folder to confine the search to, or null for everything indexed.</param>
    /// <param name="searchContents">Match file contents as well as names.</param>
    /// <param name="limit">Most paths to return.</param>
    IReadOnlyList<string> Search(string query, string? scopePath, bool searchContents, int limit, CancellationToken token);
}
