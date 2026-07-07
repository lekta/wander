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
/// progress either).
/// </para>
///
/// <para>
/// Thread model: batch executors push from thread-pool workers while the
/// UI thread reads state and pops, so every stack/busy access goes through
/// one lock. <see cref="Changed"/> is raised outside the lock and may fire
/// on a background thread — subscribers marshal to their dispatcher
/// themselves.
/// </para>
/// </summary>
public sealed class UndoService {
    private readonly object _gate = new();
    private readonly Stack<IUndoableAction> _stack = new();
    private int _busy;


    /// <summary>Fires whenever the stack or busy state changes — VM uses this to refresh CanExecute.</summary>
    public event EventHandler? Changed;


    public bool CanUndo {
        get {
            lock (_gate) {
                return _busy == 0 && _stack.Count > 0;
            }
        }
    }

    public bool IsBusy {
        get {
            lock (_gate) {
                return _busy > 0;
            }
        }
    }

    public int Depth {
        get {
            lock (_gate) {
                return _stack.Count;
            }
        }
    }

    public string? NextDescription {
        get {
            lock (_gate) {
                return _stack.TryPeek(out var a) ? a.Description : null;
            }
        }
    }


    public void Push(IUndoableAction action) {
        ArgumentNullException.ThrowIfNull(action);
        lock (_gate) {
            _stack.Push(action);
        }
        RaiseChanged();
    }


    /// <summary>
    /// Pop and undo the most recent action. Returns the action that was
    /// undone (so callers can log / report), or null if the stack was empty
    /// or busy.
    /// </summary>
    public IUndoableAction? Undo() {
        IUndoableAction action;
        lock (_gate) {
            if (_busy > 0 || !_stack.TryPop(out action!)) {
                return null;
            }
        }
        try {
            action.Undo();
            return action;
        } finally {
            RaiseChanged();
        }
    }


    /// <summary>Drops the entire history — used after permanent delete.</summary>
    public void Clear() {
        lock (_gate) {
            if (_stack.Count == 0) {
                return;
            }
            _stack.Clear();
        }
        RaiseChanged();
    }


    /// <summary>
    /// Marks the start of a long-running operation. Dispose the returned token
    /// when the op completes so Ctrl+Z becomes available again.
    /// </summary>
    public IDisposable BeginOperation() {
        lock (_gate) {
            _busy++;
        }
        RaiseChanged();
        return new Guard(this);
    }

    private void EndOperation() {
        lock (_gate) {
            if (_busy == 0) {
                return;
            }
            _busy--;
        }
        RaiseChanged();
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
