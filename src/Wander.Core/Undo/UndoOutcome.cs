namespace Wander.Core.Undo;

/// <summary>A step of an undo that did not come back, and why.</summary>
public sealed record UndoFailure(IUndoableAction Step, Exception Error);


/// <summary>
/// What <see cref="UndoService.UndoAsync"/> managed to do.
/// </summary>
/// <param name="Undone">
/// What did come back - the action itself, or a bundle of the steps that
/// made it; its <c>PathsAfterUndo</c> / <c>MovesOnUndo</c> /
/// <c>MetadataTargets</c> are what the UI follows. Null when nothing did.
/// </param>
/// <param name="Remaining">
/// The steps a cancel left undone, and the failed ones worth another try
/// (<see cref="IUndoStepGate.WorthAnotherTry"/>) - already back on the
/// stack. Null when nothing went back.
/// </param>
/// <param name="Failures">Steps that threw. On the stack again only when worth another try.</param>
/// <param name="Cancelled">Stopped before the last step.</param>
public sealed record UndoOutcome(
    IUndoableAction? Undone, IUndoableAction? Remaining, IReadOnlyList<UndoFailure> Failures, bool Cancelled);
