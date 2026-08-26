namespace Wander.Core.Undo;

/// <summary>
/// A single reversible step pushed onto <see cref="UndoService"/>. Concrete
/// implementations live in <see cref="UndoableActions"/> (rename, move,
/// create, delete, composite).
/// </summary>
public interface IUndoableAction {
    /// <summary>Short human-readable phrase for the status bar / tooltip.</summary>
    string Description { get; }

    /// <summary>Reverse the original effect. May throw — caller logs.</summary>
    void Undo();

    /// <summary>
    /// Where the undone items end up. The UI re-selects these after the
    /// listing refreshes, so Ctrl+Z leaves the user pointing at what just
    /// came back instead of at nothing. Empty when undoing removes the item
    /// (undo of "create") or when the action cannot say.
    /// </summary>
    IReadOnlyList<string> PathsAfterUndo => Array.Empty<string>();
}
