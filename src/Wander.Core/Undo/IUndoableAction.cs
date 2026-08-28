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

    /// <summary>
    /// Files whose <em>metadata</em> this action changes — the photographs,
    /// not the sidecars beside them. Non-empty only for actions that leave
    /// the folder listing itself alone: nothing appears, disappears or
    /// changes name, so the UI can re-read those few rows instead of
    /// re-listing the folder around the user.
    ///
    /// <para>
    /// Empty is the safe answer and the default. A caller that gets an
    /// empty list refreshes everything, which is always correct and only
    /// sometimes wasteful.
    /// </para>
    /// </summary>
    IReadOnlyList<string> MetadataTargets => Array.Empty<string>();
}
