namespace Wander.Core.Undo;

/// <summary>
/// LIFO stack of undoable file operations. Single instance per app,
/// registered in the service locator.
///
/// <para>
/// Async-readiness: <see cref="BeginOperation"/> bumps a busy counter
/// while a long-running op is in flight. <see cref="CanUndo"/> returns
/// false while busy, so a Ctrl+Z pressed mid-operation is silently
/// ignored (Explorer parity — it doesn't let you undo a copy in
/// progress either). For today's synchronous ops the counter is bumped
/// and dropped before the call returns, so the guard is effectively a
/// no-op until we go async.
/// </para>
/// </summary>
public sealed class UndoService {
    private readonly Stack<IUndoableAction> _stack = new();
    private int _busy;


    /// <summary>Fires whenever the stack or busy state changes — VM uses this to refresh CanExecute.</summary>
    public event EventHandler? Changed;


    public bool CanUndo => _busy == 0 && _stack.Count > 0;

    public bool IsBusy => _busy > 0;

    public int Depth => _stack.Count;

    public string? NextDescription => _stack.TryPeek(out var a) ? a.Description : null;


    public void Push(IUndoableAction action) {
        ArgumentNullException.ThrowIfNull(action);
        _stack.Push(action);
        RaiseChanged();
    }


    /// <summary>
    /// Pop and undo the most recent action. Returns the action that was
    /// undone (so callers can log / report), or null if the stack was empty
    /// or busy.
    /// </summary>
    public IUndoableAction? Undo() {
        if (!CanUndo) {
            return null;
        }
        var action = _stack.Pop();
        try {
            action.Undo();
            return action;
        } finally {
            RaiseChanged();
        }
    }


    /// <summary>Drops the entire history — used after permanent delete.</summary>
    public void Clear() {
        if (_stack.Count == 0) {
            return;
        }
        _stack.Clear();
        RaiseChanged();
    }


    /// <summary>
    /// Marks the start of a long-running operation. Dispose the returned token
    /// when the op completes so Ctrl+Z becomes available again.
    /// </summary>
    public IDisposable BeginOperation() {
        _busy++;
        RaiseChanged();
        return new Guard(this);
    }

    private void EndOperation() {
        if (_busy > 0) {
            _busy--;
            RaiseChanged();
        }
    }


    private void RaiseChanged() {
        Changed?.Invoke(this, EventArgs.Empty);
    }


    private sealed class Guard : IDisposable {
        private readonly UndoService _owner;
        private bool _released;

        public Guard(UndoService owner) {
            _owner = owner;
        }

        public void Dispose() {
            if (_released) {
                return;
            }
            _released = true;
            _owner.EndOperation();
        }
    }
}
