using Wander.Core.Undo;

namespace Wander.Core.FileSystem;

/// <summary>
/// <see cref="IUndoStepGate"/> over one <see cref="BusyGate"/>: an undo is a
/// file operation run backwards, and waits for held files the way the
/// operations do (OPERATIONS, step 8).
/// </summary>
internal sealed class UndoStepGate : IUndoStepGate {
    private readonly BusyGate _gate;


    public UndoStepGate(BusyGate gate) {
        _gate = gate;
    }


    public void Run(string path, Action undo, CancellationToken ct) {
        _gate.Retry(path, undo, ct);
    }

    public bool WorthAnotherTry(Exception error) {
        return FileInUse.Is(error) || error is ClaimedByOperationException || TransientIo.IsTransient(error);
    }

    public void Dispose() {
        _gate.Dispose();
    }
}
