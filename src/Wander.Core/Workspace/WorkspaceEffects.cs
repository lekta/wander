using Wander.Core.Layout;
using Wander.Core.Listing;
using Wander.Core.Navigation;
using Wander.Core.Panels;

namespace Wander.Core.Workspace;

/// <summary>
/// Something the application is asked to do (REDESIGN 4.6) - data, not a
/// call. The rules decide; one executor in the application carries these
/// out, and what comes of them comes back as events.
/// </summary>
public abstract record WorkspaceEffect;


/// <summary>Open <paramref name="Path"/> in the list, as opened from <paramref name="Source"/>; the folder itself is what is selected there.</summary>
public sealed record Navigate(string Path, NavigationSource Source) : WorkspaceEffect;

/// <summary>Read the rows under <paramref name="Path"/> off the UI thread and answer with <see cref="BranchRead"/> under <paramref name="Epoch"/>.</summary>
public sealed record ReadBranch(Pane Pane, string Path, int Epoch) : WorkspaceEffect;

/// <summary>Ask the disk whether these rows have subfolders, off the UI thread; answer with <see cref="ChevronsProbed"/>.</summary>
public sealed record ProbeChevrons(Pane Pane, IReadOnlyList<string> Paths) : WorkspaceEffect;

/// <summary>Raise <see cref="ThrottleElapsed"/> at <paramref name="AtMs"/>.</summary>
public sealed record ScheduleThrottle(long AtMs) : WorkspaceEffect;

/// <summary>
/// Put the keyboard into <paramref name="Zone"/> - onto its current line,
/// where it has one; its arrival is told as <paramref name="Reason"/>.
/// </summary>
public sealed record FocusZone(WindowZone Zone, ZoneReason Reason) : WorkspaceEffect;

/// <summary>
/// Put the keyboard on the line of <paramref name="Surface"/> standing on
/// <paramref name="Path"/> - once it is drawn, when it is not yet, and the
/// keyboard is still there. <paramref name="Scroll"/>: brought into view up
/// and down; otherwise a line off screen leaves the keyboard on the surface
/// itself and nothing moves under the user.
/// </summary>
public sealed record FocusRow(WindowZone Surface, string Path, bool Scroll = true) : WorkspaceEffect;

/// <summary>
/// Put <paramref name="List"/> on the list as the selection it shows - in
/// one call, not told back as the user's; <paramref name="Scroll"/> brings
/// its main row into view.
/// </summary>
public sealed record ApplyListSelection(ListState List, bool Scroll) : WorkspaceEffect;

/// <summary>Open the name editor on the list's row on <paramref name="Path"/>.</summary>
public sealed record OpenEditor(string Path) : WorkspaceEffect;
