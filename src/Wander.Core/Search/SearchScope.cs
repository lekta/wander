namespace Wander.Core.Search;

/// <summary>
/// How far a search reaches. The three values are three different costs,
/// which is why they are a user-facing choice rather than a heuristic:
/// filtering the folder on screen is free, walking its subtree is seconds,
/// and the whole machine is only affordable through somebody else's index.
/// </summary>
public enum SearchScope {
    /// <summary>
    /// The listing already on screen. No disk access at all — this is the
    /// live name filter that <see cref="FileSystem.SearchController"/> has
    /// always done, and it stays the default.
    /// </summary>
    CurrentFolder,

    /// <summary>The current folder and everything under it, walked by us.</summary>
    Subfolders,

    /// <summary>
    /// Everything the Windows Search index knows about. Answered by
    /// <see cref="IIndexedSearch"/> rather than by walking: an index that
    /// the operating system already maintains costs us nothing to read and
    /// nothing to keep up to date.
    /// </summary>
    Computer,
}
