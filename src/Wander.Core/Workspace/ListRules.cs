using System.Collections.Immutable;
using Wander.Core.Listing;

namespace Wander.Core.Workspace;

/// <summary>
/// Module 3 of the reducer (REDESIGN 4.5): the list's selection and caret.
/// What the user does to them is taken as it comes; what a landing of rows
/// makes of them is <see cref="ListingArrival"/>'s answer, put back on the
/// list in one call (<see cref="ApplyListSelection"/>) - the list drops
/// rows it rebuilds out of its selection on its own, and this is what puts
/// them back. Where the keyboard goes is the next module's.
/// </summary>
public static class ListRules {
    public static WorkspaceState Apply(WorkspaceState state, WorkspaceEvent e, ICollection<WorkspaceEffect> effects) {
        switch (e) {
            case ListSelectionChanged changed:
                return state with { List = new ListState(changed.Selection.ToImmutableArray(), changed.Primary, changed.Caret) };

            case ListCaretMoved moved:
                return state with { List = state.List with { Caret = moved.Path } };

            case ListingLanded landed:
                var landing = ListingArrival.Land(state.List, landed.Before, landed.After, landed.Reason, landed.Intent, landed.Renames);
                effects.Add(new ApplyListSelection(landing.List, landing.Scroll));
                if (landing.Editor is { } editor) {
                    effects.Add(new OpenEditor(editor));
                }

                return state with { List = landing.List };

            case Navigated { How: not NavigationKind.Rewrite }:
                // The caret belongs to the folder being left; the landing
                // puts it wherever the selection lands. A folder that only
                // moved keeps its rows, and the caret stays on its row.
                return state with { List = state.List with { Caret = null } };

            default:
                return state;
        }
    }
}
