using Wander.Core.Layout;
using Wander.Core.Listing;
using Wander.Core.Navigation;
using Wander.Core.Panels;

namespace Wander.Core.Workspace;

/// <summary>The folder open in the list: where it is, and which panel it was opened from.</summary>
/// <param name="Path">Null before the first navigation.</param>
/// <param name="Source">Where the navigation came from - which panel the folder's place is in.</param>
public sealed record FolderFacts(string? Path, NavigationSource? Source) {
    public static readonly FolderFacts None = new(null, null);
}


/// <summary>Where the keyboard is.</summary>
/// <param name="Zone">The zone it is in, or null for none - the window itself, a menu, the preview.</param>
/// <param name="LastZone">The last zone it was in: what a keyboard in no zone still means.</param>
/// <param name="WindowActive">The window is the active one.</param>
public sealed record KeyboardState(WindowZone? Zone, WindowZone LastZone, bool WindowActive) {
    public static readonly KeyboardState Initial = new(null, WindowZone.FileList, WindowActive: true);


    /// <summary>Where the keyboard was when a modal dialog opened, while it is open.</summary>
    public WindowZone? BeforeDialog { get; init; }
}


/// <summary>The settings the rules read - copied in when they change (<see cref="OptionsChanged"/>).</summary>
/// <param name="ArrowsOpenFolders">The arrow keys in a panel open the folder under the cursor (AppSettings.TreeKeyboardNavigates).</param>
/// <param name="ShowHidden">Hidden folders are shown.</param>
public sealed record WorkspaceOptions(bool ArrowsOpenFolders, bool ShowHidden) {
    public static readonly WorkspaceOptions Default = new(ArrowsOpenFolders: false, ShowHidden: false);
}


/// <summary>A cursor move waiting to open its row - see <see cref="TreeNavThrottle"/>.</summary>
public sealed record PendingPanelNavigation(Pane Pane, string Path, long DueMs);


/// <summary>A panel's lit row, and whether it is lit as the active one.</summary>
public sealed record PanelHighlight(string? Row, bool Active);


/// <summary>
/// The window as one record (REDESIGN 4.2): the open folder, the list, both
/// panels, the keyboard, the open menu. Immutable: an event makes a new one
/// (<see cref="WorkspaceReducer"/>), and a test compares before with after.
/// What is shown - the target, each panel's highlight and lines, the
/// preview's subject - is derived from it, never kept in it.
/// </summary>
public sealed record WorkspaceState {
    public static readonly WorkspaceState Initial = new();


    public FolderFacts Folder { get; init; } = FolderFacts.None;

    public ListState List { get; init; } = ListState.Empty;

    public PanelState Bookmarks { get; init; } = PanelState.Empty;

    public PanelState Drives { get; init; } = PanelState.Empty;

    public KeyboardState Keyboard { get; init; } = KeyboardState.Initial;

    /// <summary>The open context menu's snapshot, while one is open.</summary>
    public MenuContext? Menu { get; init; }

    public WorkspaceOptions Options { get; init; } = WorkspaceOptions.Default;

    /// <summary>When a panel last opened a folder (the clock of <see cref="TreeNavThrottle"/>).</summary>
    public long? LastPanelNavigationMs { get; init; }

    /// <summary>A cursor move waiting to open its row, or null.</summary>
    public PendingPanelNavigation? PendingNavigation { get; init; }

    /// <summary>When the panels were last read again for the window's activation (P-24).</summary>
    public long? LastActivationRefreshMs { get; init; }

    /// <summary>The last epoch handed to a read; every read gets the next one.</summary>
    public int Epoch { get; init; }


    public PanelState Panel(Pane pane) {
        return pane == Pane.Bookmarks ? Bookmarks : Drives;
    }

    public WorkspaceState WithPanel(Pane pane, PanelState panel) {
        return pane == Pane.Bookmarks ? this with { Bookmarks = panel } : this with { Drives = panel };
    }

    /// <summary>
    /// A panel's lit row - its cursor - and whether it is lit as active:
    /// only in the panel the keyboard is in. Each panel lights at most one
    /// row, and the window at most one active one (P-21).
    /// </summary>
    public PanelHighlight Highlight(Pane pane) {
        return new PanelHighlight(Panel(pane).Caret, Keyboard.Zone == ZoneOf(pane));
    }


    /// <summary>The zone a panel is.</summary>
    public static WindowZone ZoneOf(Pane pane) {
        return pane == Pane.Bookmarks ? WindowZone.Bookmarks : WindowZone.Drives;
    }

    /// <summary>Where a navigation from a panel says it came from.</summary>
    public static NavigationSource SourceOf(Pane pane) {
        return pane == Pane.Bookmarks ? NavigationSource.Bookmark : NavigationSource.Drives;
    }
}
