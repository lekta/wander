using Wander.Core.Layout;
using Wander.Core.Listing;
using Wander.Core.Navigation;
using Wander.Core.Panels;

namespace Wander.Core.Workspace;

/// <summary>
/// Something that happened, with its reason (REDESIGN 4.4) - the only way
/// into the model. The views turn clicks and keys into these; the
/// application turns what its services did (a navigation, a level read, a
/// folder moved) into these. An event whose reason is not known is
/// answered conservatively: it selects nothing and opens nothing.
/// </summary>
public abstract record WorkspaceEvent;


// --- The window and the keyboard -------------------------------------------

/// <summary>Why the keyboard came into a zone.</summary>
public enum ZoneReason {
    /// <summary>Nobody said.</summary>
    Unknown,

    /// <summary>Tab or Shift+Tab.</summary>
    Tab,

    /// <summary>Ctrl+1 into a panel - the panel the open folder came from, or the other one.</summary>
    PanelKey,

    /// <summary>Ctrl+Shift+E - the open folder's own panel, opened down to it.</summary>
    RevealKey,

    /// <summary>A click in the zone.</summary>
    Click,

    /// <summary>Back from a context menu.</summary>
    MenuReturn,

    /// <summary>Back from a modal dialog.</summary>
    DialogReturn,

    /// <summary>The window was activated.</summary>
    Activation,

    /// <summary>The element that had it was taken away, and WPF put the keyboard on the window.</summary>
    FocusFell,

    /// <summary>The application put it there.</summary>
    Programmatic,
}


/// <summary>The keyboard is in <paramref name="Zone"/> now (null for none), for <paramref name="Reason"/>.</summary>
public sealed record ZoneEntered(WindowZone? Zone, ZoneReason Reason) : WorkspaceEvent;

/// <summary>The window became the active one.</summary>
public sealed record WindowActivated(long NowMs) : WorkspaceEvent;

/// <summary>Another window became the active one.</summary>
public sealed record WindowDeactivated : WorkspaceEvent;

/// <summary>A context menu opened on <paramref name="Context"/>.</summary>
public sealed record MenuOpened(MenuContext Context) : WorkspaceEvent;

/// <summary>The menu opened on <paramref name="Context"/> closed.</summary>
public sealed record MenuClosed(MenuContext Context) : WorkspaceEvent;

/// <summary>The settings the rules read changed.</summary>
public sealed record OptionsChanged(WorkspaceOptions Options) : WorkspaceEvent;

/// <summary>
/// These zones are going off screen - the folders pane put away, the
/// bookmarks folded. Told before they go: a zone off screen cannot hold the
/// keyboard, and WPF would drop it on the window.
/// </summary>
public sealed record PaneHidden(IReadOnlyList<WindowZone> Zones) : WorkspaceEvent;

/// <summary>A modal dialog of the window's is about to open.</summary>
public sealed record DialogOpened : WorkspaceEvent;

/// <summary>
/// That dialog closed. WPF gives the keyboard back to whatever it finds
/// first in the window, which is nowhere the user was.
/// </summary>
public sealed record DialogClosed : WorkspaceEvent;


// --- Facts from the application ----------------------------------------------

/// <summary>The window comes up: the branches saved open (<paramref name="Expanded"/>, already checked for being there) open again, and the drives are read.</summary>
public sealed record WorkspaceStarted(IReadOnlyList<NavigationStop> Expanded) : WorkspaceEvent;

/// <summary>How the open folder changed.</summary>
public enum NavigationKind {
    /// <summary>Somewhere new.</summary>
    Go,

    /// <summary>Back in the history.</summary>
    Back,

    /// <summary>Forward in the history.</summary>
    Forward,

    /// <summary>One level up.</summary>
    Up,

    /// <summary>The open folder moved or was renamed, and the history was rewritten after it.</summary>
    Rewrite,
}


/// <summary>The folder open in the list is <paramref name="Path"/> now, opened from <paramref name="Source"/>.</summary>
public sealed record Navigated(string Path, NavigationSource Source, NavigationKind How) : WorkspaceEvent;

/// <summary>
/// The rows under <paramref name="Path"/> in <paramref name="Pane"/>, as the
/// disk answered a read asked under <paramref name="Epoch"/>;
/// <see cref="PanelState.TopKey"/> for the drives.
/// </summary>
public sealed record BranchRead(Pane Pane, string Path, IReadOnlyList<PanelRow> Rows, int Epoch) : WorkspaceEvent;

/// <summary>Whether each of these rows has subfolders, as the disk answered.</summary>
public sealed record ChevronsProbed(Pane Pane, IReadOnlyDictionary<string, bool> HasChildren) : WorkspaceEvent;

/// <summary>The bookmarks panel's top rows are these now: a bookmark added, removed, moved, relocated, switched on or off.</summary>
public sealed record BookmarksChanged(IReadOnlyList<PanelRow> Rows) : WorkspaceEvent;

/// <summary>Wander moved or renamed the folder <paramref name="From"/>; it is <paramref name="To"/> now.</summary>
public sealed record Relocated(string From, string To) : WorkspaceEvent;

/// <summary>These folders are gone - deleted by Wander, or found gone.</summary>
public sealed record Removed(IReadOnlyList<string> Paths) : WorkspaceEvent;

/// <summary>The folder gained or lost subfolders: the rows standing on it are read again.</summary>
public sealed record FolderChanged(string Path) : WorkspaceEvent;

/// <summary>Everything the panels have read is read again - F5.</summary>
public sealed record PanelsRefreshRequested : WorkspaceEvent;

/// <summary>
/// The list's rows were laid down again: as they stood (<paramref name="Before"/>)
/// and as they are (<paramref name="After"/>), in order; why; what the
/// pending intent came to (<see cref="FolderSession.DecideArrival"/>); and the
/// rows renamed or moved since the last landing, old path to new - Wander's
/// own renames and the ones the watcher saw.
/// </summary>
public sealed record ListingLanded(
    IReadOnlyList<string> Before,
    IReadOnlyList<string> After,
    ListingReason Reason,
    ArrivalDecision Intent,
    IReadOnlyList<(string From, string To)> Renames) : WorkspaceEvent;

/// <summary>The list shows its rows in another view: another control holds them now.</summary>
public sealed record ViewModeChanged : WorkspaceEvent;


// --- Input in the list ------------------------------------------------------------

/// <summary>
/// The user changed the list's selection - a click, a key, a sweep, Ctrl+A.
/// <paramref name="Primary"/> is the list's own main row, <paramref name="Caret"/>
/// the row the keyboard is on.
/// </summary>
public sealed record ListSelectionChanged(IReadOnlyList<string> Selection, string? Primary, string? Caret) : WorkspaceEvent;

/// <summary>The keyboard's row in the list moved with the selection as it was - a press on a row, a row focused.</summary>
public sealed record ListCaretMoved(string? Path) : WorkspaceEvent;


// --- Input in a panel -----------------------------------------------------------

/// <summary>A row was clicked - what opens its folder.</summary>
public sealed record RowClicked(Pane Pane, string Path, long NowMs) : WorkspaceEvent;

/// <summary>Enter in a panel: the row under the cursor opens.</summary>
public sealed record RowActivated(Pane Pane, long NowMs) : WorkspaceEvent;

/// <summary>
/// A row's chevron, or a double click on it: open or close.
/// <paramref name="All"/> is Alt held - open the children as well, or close
/// everything below.
/// </summary>
public sealed record ChevronToggled(Pane Pane, string Path, bool Open, bool All) : WorkspaceEvent;

/// <summary>A key that moves a panel's cursor (decision B26); <paramref name="PageSize"/> is how many lines the panel shows.</summary>
public sealed record CaretMoveRequested(Pane Pane, PanelKey Key, int PageSize, long NowMs) : WorkspaceEvent;

/// <summary>
/// A panel's cursor put on a row by name rather than by a key - the first
/// letters typed into the panel (decision B26). Opens nothing.
/// </summary>
public sealed record CaretMoved(Pane Pane, string Path) : WorkspaceEvent;

/// <summary>The timer a <see cref="ScheduleThrottle"/> asked for has run out.</summary>
public sealed record ThrottleElapsed(long NowMs) : WorkspaceEvent;

/// <summary>F2 or "Rename" on a row of a panel.</summary>
public sealed record EditRequested(Pane Pane, string Path) : WorkspaceEvent;

/// <summary>The editor on a panel row closed - applied or not; the rename itself, if any, comes back as <see cref="Relocated"/>.</summary>
public sealed record EditEnded(Pane Pane) : WorkspaceEvent;
