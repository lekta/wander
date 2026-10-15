namespace Wander.Core.Undo;

/// <summary>
/// How the steps of one undo are run: through the same wait for held files
/// the operations go through - <c>Ctrl+Z</c> on a file another program
/// holds waits for it, out of the same budget, and names the holder when it
/// gives up - and which failures are worth leaving on the stack for the
/// next <c>Ctrl+Z</c>. The undo knows neither files nor holders; the file
/// operations hand it one of these (<c>FileOperationService.UndoGate</c>).
/// </summary>
public interface IUndoStepGate : IDisposable {
    /// <summary>Runs <paramref name="undo"/>, again and again while what it touches answers "in use".</summary>
    /// <param name="path">What the step is about - the wait's report names it.</param>
    /// <exception cref="OperationCanceledException">Cancelled during the wait.</exception>
    void Run(string path, Action undo, CancellationToken ct);

    /// <summary>
    /// The step failed in a way that may pass - still held, another
    /// operation's, a transient read: it stays on the stack and the next
    /// <c>Ctrl+Z</c> tries it again. Anything else - the item is gone - and
    /// it is left behind, or it would wedge every older step under it.
    /// </summary>
    bool WorthAnotherTry(Exception error);
}
