using Wander.Core.Layout;
using Wander.Core.Panels;

namespace Wander.Core.Workspace;

/// <summary>
/// Module 1 of the reducer (REDESIGN 4.5): which events open a folder. A
/// click on a row, Enter on the cursor, and - with "arrows open folders" -
/// a cursor move, through <see cref="TreeNavThrottle"/>; Ctrl+1 from one
/// panel into the other onto the row it holds, with the same setting. A folder that
/// is already open is not opened again: the echo of a navigation used to
/// plant an intent that the next listing consumed. Owns the open folder's
/// facts and the throttle's clock.
/// </summary>
public static class NavigationRules {
    public static WorkspaceState Apply(WorkspaceState state, WorkspaceEvent e, ICollection<WorkspaceEffect> effects) {
        switch (e) {
            case Navigated navigated:
                return state with { Folder = new FolderFacts(navigated.Path, navigated.Source) };

            case RowClicked clicked:
                return Open(state with { PendingNavigation = null }, clicked.Pane, clicked.Path, clicked.NowMs, effects);

            case RowActivated activated:
                return Open(state with { PendingNavigation = null }, activated.Pane, state.Panel(activated.Pane).Caret, activated.NowMs, effects);

            case CaretMoveRequested move:
                return OnCaretMove(state, move, effects);

            case ThrottleElapsed elapsed:
                return OnThrottle(state, elapsed, effects);

            case ZoneEntered { Zone: WindowZone.Bookmarks or WindowZone.Drives, Reason: ZoneReason.PanelKey } entered
                when state.Options.ArrowsOpenFolders:
                // Ctrl+1 from one panel into the other goes back to the row
                // that panel holds, and with the arrows opening folders it
                // opens that row, as an arrow landing there would (P-22, both
                // ways since 2026-09-23).
                var pane = entered.Zone == WindowZone.Bookmarks ? Pane.Bookmarks : Pane.Drives;

                return state.HeldRow(pane) is { } held
                    ? Open(state, pane, held, nowMs: null, effects)
                    : state;

            default:
                return state;
        }
    }


    /// <summary>A panel's row opens - unless it is the folder already open.</summary>
    private static WorkspaceState Open(WorkspaceState state, Pane pane, string? path, long? nowMs, ICollection<WorkspaceEffect> effects) {
        if (path is null || PanelPaths.Same(path, state.Folder.Path)) {
            return state;
        }

        effects.Add(new Navigate(path, WorkspaceState.SourceOf(pane)));

        return nowMs is { } now ? state with { LastPanelNavigationMs = now } : state;
    }

    private static WorkspaceState OnCaretMove(WorkspaceState state, CaretMoveRequested move, ICollection<WorkspaceEffect> effects) {
        if (!state.Options.ArrowsOpenFolders) {
            return state;
        }

        var panel = state.Panel(move.Pane);
        var result = PanelKeyNavigation.Press(PanelView.Rows(panel), panel.Caret, move.Key, move.PageSize);
        if (result.Outcome != PanelKeyOutcome.MoveCaret) {
            return state;
        }

        // Back onto the open folder: nothing to open, and nothing pending.
        if (PanelPaths.Same(result.Path, state.Folder.Path)) {
            return state with { PendingNavigation = null };
        }

        var decision = TreeNavThrottle.Decide(state.Options.ArrowsOpenFolders, move.NowMs, state.LastPanelNavigationMs);
        switch (decision.Outcome) {
            case ThrottleOutcome.Now:
                return Open(state with { PendingNavigation = null }, move.Pane, result.Path, move.NowMs, effects);

            case ThrottleOutcome.At:
                effects.Add(new ScheduleThrottle(decision.AtMs));

                return state with { PendingNavigation = new PendingPanelNavigation(move.Pane, result.Path!, decision.AtMs) };

            default:
                return state;
        }
    }

    /// <summary>
    /// The cursor rested: the row it rested on opens - if the keyboard is
    /// still in that panel. A user who moved on mid-burst did not mean it.
    /// A tick before the time is an older timer; the newer one comes.
    /// </summary>
    private static WorkspaceState OnThrottle(WorkspaceState state, ThrottleElapsed elapsed, ICollection<WorkspaceEffect> effects) {
        if (state.PendingNavigation is not { } pending || elapsed.NowMs < pending.DueMs) {
            return state;
        }

        state = state with { PendingNavigation = null };

        return state.Keyboard.Zone == WorkspaceState.ZoneOf(pending.Pane)
            ? Open(state, pending.Pane, pending.Path, elapsed.NowMs, effects)
            : state;
    }
}
