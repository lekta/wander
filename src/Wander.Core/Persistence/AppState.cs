using Wander.Core.Navigation;

namespace Wander.Core.Persistence;

/// <summary>
/// Top-level snapshot persisted to <c>state.json</c>. Four logical buckets:
/// <see cref="Session"/> (where the user left off), <see cref="Favorites"/>
/// (their user-defined bookmark list), <see cref="Window"/> (window
/// placement), and <see cref="Settings"/> (preference toggles). Each
/// bucket is its own record so it can grow independently without thrashing
/// the top-level shape.
/// </summary>
public sealed record AppState {
    /// <summary>Where the user left off — folder, expansions, panes, view mode.</summary>
    public SessionState Session { get; init; } = new();

    /// <summary>
    /// User-defined bookmark folders (full paths). Order is preserved as
    /// the user reorders them; special folders (This PC, Downloads) live
    /// separately and are toggled via <see cref="AppSettings"/>.
    /// </summary>
    public IReadOnlyList<string> Favorites { get; init; } = Array.Empty<string>();

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
/// "Where the user left off" — the session-resume bucket. Distinct from
/// <see cref="AppSettings"/> (long-term preferences) and
/// <see cref="WindowGeometry"/> (chrome placement): everything in here is
/// resetable without surprising the user, and a fresh install starts with
/// the defaults below.
/// </summary>
public sealed record SessionState {
    /// <summary>
    /// Folder the user was on when the session closed, together with the
    /// panel context (drives / bookmarks / address / …) so the restored
    /// session re-expands the right tree. Null on a fresh install.
    /// </summary>
    public NavigationStop? LastPath { get; init; }

    /// <summary>Last view mode (Details / Tiles / LargeIcons) as a string.</summary>
    public string? ViewMode { get; init; }

    /// <summary>
    /// Tree nodes the user had expanded at close, scoped per panel.
    /// The same path can live in both panels (e.g. user-favourite that
    /// also exists deep in the drives subtree) — each ownership is
    /// recorded separately, so restoring keeps both panels matching
    /// their last visible state independently.
    /// </summary>
    public IReadOnlyList<NavigationStop> ExpandedPaths { get; init; } = Array.Empty<NavigationStop>();

    /// <summary>
    /// Folders visited most recently, newest first — the address-bar
    /// dropdown. Capped by <c>RecentPaths.DefaultCapacity</c> on load, so
    /// a hand-grown list in state.json can't bloat the popup.
    /// </summary>
    public IReadOnlyList<string> RecentPaths { get; init; } = Array.Empty<string>();

    public bool IsPreviewVisible { get; init; }
    public double PreviewWidth { get; init; } = 280;

    /// <summary>
    /// Collapsed state of the bookmarks panel itself (the section above
    /// the drives tree). Defaults to expanded for new users — discovery
    /// matters more than chrome conservation on first run.
    /// </summary>
    public bool IsBookmarksExpanded { get; init; } = true;
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
