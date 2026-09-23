namespace Wander.Core.Panels;

/// <summary>
/// One of the two folder panels on the left of the window. Its own type
/// rather than <c>NavigationSource</c>: that one says where a navigation
/// came from and has six answers, two of which are panels.
/// </summary>
public enum Pane {
    /// <summary>The bookmarks, above.</summary>
    Bookmarks,

    /// <summary>The drives tree, below.</summary>
    Drives,
}
