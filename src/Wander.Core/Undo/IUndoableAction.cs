namespace Wander.Core.Undo;

/// <summary>
/// A single reversible step pushed onto <see cref="UndoService"/>. Concrete
/// implementations live in <c>UndoableActions.cs</c> (rename, move,
/// create, delete, composite).
/// </summary>
public interface IUndoableAction {
    /// <summary>Short human-readable phrase for the status bar / tooltip.</summary>
    string Description { get; }

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

    /// <summary>
    /// What the undo carries from one path to another: where an item is
    /// now, and where it goes back to - in the order the undo moves them.
    /// Everything that remembers a path (the folder on screen, the history,
    /// a bookmark) follows these through <see cref="FileSystem.PathRewrite"/>,
    /// so undoing the move of the open folder takes the listing back with
    /// it instead of leaving it on a path that is now empty. Empty when the
    /// undo relocates nothing.
    /// </summary>
    IReadOnlyList<(string From, string To)> MovesOnUndo => Array.Empty<(string, string)>();

    /// <summary>Reverse the original effect. May throw — caller logs.</summary>
    void Undo();
}
