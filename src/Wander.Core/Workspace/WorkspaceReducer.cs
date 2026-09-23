namespace Wander.Core.Workspace;

/// <summary>What one event did: the state after it, and what the application is asked to do.</summary>
public sealed record WorkspaceResult(WorkspaceState State, IReadOnlyList<WorkspaceEffect> Effects);


/// <summary>
/// The one way into the model (REDESIGN 4.5): an event goes through the
/// modules in a fixed order - which opens a folder, the panels, the list,
/// the keyboard - each owning its part of the state and reading what the
/// ones before it made of the event; the keyboard's, last, compares it with
/// the state before the event. None of them calls another, and none
/// reads the keyboard, the mouse, the clock or the disk: what they need is
/// in the state or in the event. The order is the contract, and the tests
/// hold it.
/// </summary>
public static class WorkspaceReducer {
    public static WorkspaceResult Apply(WorkspaceState state, WorkspaceEvent e) {
        var effects = new List<WorkspaceEffect>();
        var before = state;
        state = NavigationRules.Apply(state, e, effects);
        state = PanelRules.Apply(state, e, effects);
        state = ListRules.Apply(state, e, effects);
        state = KeyboardRules.Apply(before, state, e, effects);
        // The settings are copied in after the modules, which compare them
        // with the event to see what changed.
        if (e is OptionsChanged changed) {
            state = state with { Options = changed.Options };
        }

        return new WorkspaceResult(state, effects);
    }
}
