namespace Wander.Core.Operations;

/// <summary>
/// Registry of currently-running file operations. UI binds to this to draw
/// the status-bar progress bar and tooltip. Registered once in the service
/// locator so every async caller (VM commands, drop handler, future
/// scripting) reports into the same place — multiple ops can be in flight
/// at once and they are aggregated for display.
///
/// <para>
/// Thread model: file ops run on background threads, so all mutations and
/// snapshots are taken under a single lock. The <see cref="Changed"/> event
/// fires after each mutation; subscribers must marshal onto their UI
/// dispatcher themselves — the tracker stays UI-framework-agnostic.
/// </para>
/// </summary>
public sealed class OperationTracker {
    private readonly object _gate = new();
    private readonly List<OperationProgress> _ops = new();


    /// <summary>Fires after any handle is created, advanced, or completed.</summary>
    public event EventHandler? Changed;


    /// <summary>
    /// Register a new operation. Dispose the returned handle when the op
    /// finishes (success or failure) — it removes the entry from the tracker.
    /// </summary>
    /// <param name="verb">Short user-facing label: "Move", "Copy", "Delete".</param>
    /// <param name="total">Expected total step count (one step per item, usually).</param>
    public IOperationHandle Begin(string verb, int total) {
        var progress = new OperationProgress(verb, total);
        lock (_gate) {
            _ops.Add(progress);
        }
        RaiseChanged();
        return new Handle(this, progress);
    }


    /// <summary>Immutable point-in-time view of every running operation.</summary>
    public IReadOnlyList<OperationSnapshot> Snapshot() {
        lock (_gate) {
            var copy = new OperationSnapshot[_ops.Count];
            for (int i = 0; i < _ops.Count; i++) {
                copy[i] = _ops[i].ToSnapshot();
            }
            return copy;
        }
    }


    private void Remove(OperationProgress op) {
        lock (_gate) {
            _ops.Remove(op);
        }
        RaiseChanged();
    }

    private void RaiseChanged() {
        Changed?.Invoke(this, EventArgs.Empty);
    }


    private sealed class OperationProgress {
        public OperationProgress(string verb, int total) {
            Verb = verb;
            Total = total;
        }

        public string Verb { get; }
        public int Total { get; }
        public int Completed { get; set; }
        public string? CurrentPath { get; set; }

        public OperationSnapshot ToSnapshot() => new(Verb, Completed, Total, CurrentPath);
    }


    private sealed class Handle : IOperationHandle {
        private readonly OperationTracker _owner;
        private readonly OperationProgress _progress;
        private bool _done;

        public Handle(OperationTracker owner, OperationProgress progress) {
            _owner = owner;
            _progress = progress;
        }

        public void Advance(string? currentPath = null) {
            if (_done) {
                return;
            }
            lock (_owner._gate) {
                _progress.Completed++;
                _progress.CurrentPath = currentPath;
            }
            _owner.RaiseChanged();
        }

        public void Dispose() {
            if (_done) {
                return;
            }
            _done = true;
            _owner.Remove(_progress);
        }
    }
}


public interface IOperationHandle : IDisposable {
    /// <summary>Bump the completed counter and update the "current path" hint.</summary>
    void Advance(string? currentPath = null);
}


/// <summary>Frozen point-in-time view of one operation. Safe to hand to UI threads.</summary>
public sealed record OperationSnapshot(string Verb, int Completed, int Total, string? CurrentPath);
