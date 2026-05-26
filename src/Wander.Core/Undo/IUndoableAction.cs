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
}
