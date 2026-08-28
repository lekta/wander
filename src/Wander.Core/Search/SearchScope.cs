namespace Wander.Core.Search;

/// <summary>
/// How far a search reaches. The two values are two different costs, which
/// is why this is a user-facing choice rather than a heuristic: one folder
/// is bounded by a single listing, a subtree is not bounded by anything.
/// That difference is also what decides whether a search may start on its
/// own from a two-character query — see
/// <c>ContentSearchController.MinAutoRunLength</c>.
/// </summary>
public enum SearchScope {
    /// <summary>
    /// The folder on screen and nothing else. Cheap enough that a content
    /// search here runs as freely as the name filter does.
    /// </summary>
    CurrentFolder,

    /// <summary>The current folder and everything under it.</summary>
    Subfolders,
}
