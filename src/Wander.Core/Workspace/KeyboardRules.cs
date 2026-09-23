using Wander.Core.Layout;
using Wander.Core.Listing;
using Wander.Core.Panels;

namespace Wander.Core.Workspace;

/// <summary>
/// Module 4 of the reducer (REDESIGN 4.5): where the keyboard is, whether
/// the window is active, which menu is open - the facts every other module
/// reads - and where the keyboard is to go when the view cannot leave it
/// where it is. Last, and handed the state from before the event as well:
/// what it answers is what the modules before it made of the event - a
/// cursor moved by a key, a row gone from under the keyboard, rows an
/// operation brought.
/// </summary>
public static class KeyboardRules {
    public static WorkspaceState Apply(WorkspaceState before, WorkspaceState state, WorkspaceEvent e, ICollection<WorkspaceEffect> effects) {
        var keyboard = state.Keyboard;
        state = e switch {
            ZoneEntered entered => state with {
                Keyboard = keyboard with { Zone = entered.Zone, LastZone = entered.Zone ?? keyboard.LastZone },
            },
            WindowActivated => state with { Keyboard = keyboard with { WindowActive = true } },
            WindowDeactivated => state with { Keyboard = keyboard with { WindowActive = false } },
            MenuOpened opened => state with { Menu = opened.Context },
            // Only that menu: a right-click that opens the next one closes
            // this one after the next has opened.
            MenuClosed closed when ReferenceEquals(state.Menu, closed.Context) => state with { Menu = null },
            DialogOpened => state with { Keyboard = keyboard with { BeforeDialog = keyboard.Zone ?? keyboard.LastZone } },
            DialogClosed => state with { Keyboard = keyboard with { BeforeDialog = null } },
            _ => state,
        };

        Move(before, state, e, effects);

        return state;
    }


    /// <summary>
    /// Where the keyboard is to go. It fell out of a panel - the line it was
    /// on was taken away - and goes onto the panel's cursor, which the panel
    /// has moved to the neighbour (K-10); none left, into the list. It fell
    /// out of the list's row - rebuilt, replaced - and goes onto the caret,
    /// nothing scrolling (K-7). A zone going off screen with it hands it to
    /// the list (K-8). A dialog closing puts it back in the panel it was in,
    /// else in the list (K-3). Coming back from a menu or another window,
    /// WPF puts it back where it was, and nothing is asked (K-9). In a panel
    /// it stays on the line under the cursor: after a key moved the cursor,
    /// and when a key brought it in. In the list: see <see cref="Landed"/>;
    /// another view, the caret's row in it (K-5).
    /// </summary>
    private static void Move(WorkspaceState before, WorkspaceState state, WorkspaceEvent e, ICollection<WorkspaceEffect> effects) {
        switch (e) {
            case ZoneEntered { Reason: ZoneReason.FocusFell }:
                if (PaneOf(before.Keyboard.Zone) is { } fell) {
                    effects.Add(state.Panel(fell).Caret is { } caret
                        ? new FocusRow(WorkspaceState.ZoneOf(fell), caret)
                        : new FocusZone(WindowZone.FileList, ZoneReason.Programmatic));
                } else if (before.Keyboard.Zone == WindowZone.FileList) {
                    effects.Add(state.List.Caret is { } caret
                        ? new FocusRow(WindowZone.FileList, caret, Scroll: false)
                        : new FocusZone(WindowZone.FileList, ZoneReason.Programmatic));
                }

                return;

            case ListingLanded landed:
                Landed(before, state, landed, effects);

                return;

            case ViewModeChanged when state.Keyboard.Zone == WindowZone.FileList
                && (state.List.Caret ?? state.List.Primary) is { } row:
                effects.Add(new FocusRow(WindowZone.FileList, row));

                return;

            case PaneHidden hidden:
                if (state.Keyboard.Zone is { } zone && hidden.Zones.Contains(zone)) {
                    effects.Add(new FocusZone(WindowZone.FileList, ZoneReason.Programmatic));
                }

                return;

            case DialogClosed:
                var was = before.Keyboard.BeforeDialog ?? before.Keyboard.Zone ?? before.Keyboard.LastZone;
                effects.Add(new FocusZone(PaneOf(was) is not null ? was : WindowZone.FileList, ZoneReason.DialogReturn));

                return;
        }

        if (PaneOf(state.Keyboard.Zone) is { } pane && state.Panel(pane).Caret is { } at
            && (!PanelPaths.Same(before.Panel(pane).Caret, at)
                || e is ZoneEntered { Reason: ZoneReason.Tab or ZoneReason.PanelKey or ZoneReason.RevealKey })) {
            effects.Add(new FocusRow(WorkspaceState.ZoneOf(pane), at));
        }
    }

    /// <summary>
    /// The list's rows landed. Rows an operation or an undo brought, a folder
    /// walked into with a row to come back to: the keyboard goes onto the
    /// main one, brought into view - from the list; from nowhere too when the
    /// operation ran behind a dialog (K-3, K-6, L-6, L-7). The keyboard's row
    /// went - deleted, hidden by the filter, renamed: onto the row that took
    /// its place, nothing scrolling (K-1, K-11). Otherwise it stays (K-7,
    /// K-12); a keyboard in a panel always does.
    /// </summary>
    private static void Landed(WorkspaceState before, WorkspaceState state, ListingLanded landed, ICollection<WorkspaceEffect> effects) {
        var zone = state.Keyboard.Zone;
        if (state.List.Caret is not { } caret) {
            return;
        }

        if (landed.Intent.Outcome == ArrivalOutcome.SelectRows) {
            // A row landing with its name editor open: the editor takes the
            // keyboard itself, and a row focused after it would take it away
            // and commit the name untouched (L-12).
            if (landed.Intent.RenameTarget is null
                && (zone == WindowZone.FileList || (zone is null && landed.Intent.TakeFocus))) {
                effects.Add(new FocusRow(WindowZone.FileList, caret));
            }

            return;
        }

        if (zone == WindowZone.FileList && !PanelPaths.Same(before.List.Caret, caret)) {
            effects.Add(new FocusRow(WindowZone.FileList, caret, Scroll: false));
        }
    }

    private static Pane? PaneOf(WindowZone? zone) {
        return zone switch {
            WindowZone.Bookmarks => Pane.Bookmarks,
            WindowZone.Drives => Pane.Drives,
            _ => null,
        };
    }
}
