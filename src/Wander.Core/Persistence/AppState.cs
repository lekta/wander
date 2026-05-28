namespace Wander.Core.Persistence;

public sealed record AppState {
    public string? LastPath { get; init; }
    public string? ViewMode { get; init; }
    public IReadOnlyList<string> ExpandedPaths { get; init; } = Array.Empty<string>();
    public bool IsPreviewVisible { get; init; }
    public double PreviewWidth { get; init; } = 280;

    /// <summary>
    /// User-defined bookmark folders (full paths). Order is preserved as
    /// the user reorders them; special folders (This PC, Downloads) live
    /// separately and are toggled via <see cref="AppSettings"/>.
    /// </summary>
    public IReadOnlyList<string> Favorites { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Collapsed state of the bookmarks panel itself (the section above
    /// the drives tree). Defaults to expanded for new users — discovery
    /// matters more than chrome conservation on first run.
    /// </summary>
    public bool IsBookmarksExpanded { get; init; } = true;

    /// <summary>
    /// Window position / size at close. Null on a fresh install (or after a
    /// rolled-back schema) so consumers fall back to the XAML defaults
    /// rather than to (0, 0, 0, 0). Width/Height are remembered even when
    /// the window was Maximized at close — that way restoring it to Normal
    /// lands at the same size it had before being maximized.
    /// </summary>
    public WindowGeometry? Window { get; init; }

    /// <summary>
    /// User preferences (separate from session-state above). Always
    /// non-null so consumers don't have to null-check; the default
    /// record represents the out-of-the-box settings.
    /// </summary>
    public AppSettings Settings { get; init; } = new();
}


/// <summary>
/// Window placement remembered between sessions. Separate from
/// <see cref="AppState"/> so the geometry block can grow (multi-monitor
/// info, splitter widths, …) without thrashing the top-level shape.
/// </summary>
public sealed record WindowGeometry {
    public double Left { get; init; }
    public double Top { get; init; }
    public double Width { get; init; }
    public double Height { get; init; }
    public bool Maximized { get; init; }
}
