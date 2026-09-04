namespace Wander.Core.Operations;

/// <summary>
/// Registry of currently-running file operations. UI binds to this to draw
/// the status-bar progress area and the operation window. Registered once
/// in the service locator so every async caller (VM commands, drop handler,
/// future scripting) reports into the same place - multiple ops can be in
/// flight at once and they are aggregated for display.
///
/// <para>
/// Thread model: file ops run on background threads, so all mutations and
/// snapshots are taken under a single lock. The <see cref="Changed"/> event
/// fires after each mutation; subscribers must marshal onto their UI
/// dispatcher themselves - the tracker stays UI-framework-agnostic.
/// </para>
///
/// <para>
/// Progress is counted twice over: in items (what the user selected) and in
/// bytes (what the disk actually moves). A copy of one 5 GB file is one
/// item and a bar that never moves, which is why the byte counter exists;
/// a delete moves no bytes at all, which is why the item counter stays.
/// Byte reports arrive per buffer, so <see cref="Changed"/> is throttled to
/// <see cref="MinInterval"/> - the last state is delivered by a trailing
/// timer rather than swallowed.
/// </para>
/// </summary>
public sealed class OperationTracker {
    /// <summary>Ten redraws a second is more than an eye needs and far less than a copy reports.</summary>
    public static readonly TimeSpan MinInterval = TimeSpan.FromMilliseconds(100);

    private readonly object _gate = new();
    private readonly List<OperationProgress> _ops = new();
    private readonly TimeSpan _minInterval;
    private readonly Func<DateTime> _now;

    private DateTime _lastRaise = DateTime.MinValue;
    private Timer? _trailing;
    private bool _trailingScheduled;
    private long _lastId;


    /// <param name="minInterval">How rarely <see cref="Changed"/> may fire for progress reports.</param>
    /// <param name="now">The clock, so a test can hold it still.</param>
    public OperationTracker(TimeSpan? minInterval = null, Func<DateTime>? now = null) {
        _minInterval = minInterval ?? MinInterval;
        _now = now ?? (() => DateTime.UtcNow);
    }


    /// <summary>Fires after any handle is created, advanced, or completed.</summary>
    public event EventHandler? Changed;


    /// <summary>
    /// Register a new operation. Dispose the returned handle when the op
    /// finishes (success or failure) - it removes the entry from the tracker.
    /// </summary>
    /// <param name="verb">Short user-facing label: "Move", "Copy", "Delete".</param>
    /// <param name="total">Expected total step count (one step per item, usually).</param>
    /// <param name="totalBytes">
    /// Expected total size, 0 when unknown or meaningless (a delete moves no
    /// bytes). Extraction reports the shell engine's own "work" units here
    /// instead, which is why the display asks
    /// <see cref="OperationSnapshot.BytesAreWork"/> before writing "MB".
    /// </param>
    /// <param name="bytesAreWork">The byte counters are abstract work units, shown as a percentage.</param>
    /// <param name="token">
    /// The token the operation runs under. It travels into the snapshot so
    /// the window that owns the token can tell its own operation from
    /// everybody else's: the handle is created several layers below the
    /// window, and the token is the one thing both of them already hold.
    /// </param>
    public IOperationHandle Begin(string verb, int total, long totalBytes = 0, bool bytesAreWork = false,
        CancellationToken token = default) {
        OperationProgress progress;
        lock (_gate) {
            progress = new OperationProgress(++_lastId, verb, total, totalBytes, bytesAreWork, _now(), token);
            _ops.Add(progress);
        }
        RaiseNow();

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
        RaiseNow();
    }

    /// <summary>
    /// A structural change - an operation appeared or finished - goes out at
    /// once: it is rare, and a window that opens or closes on it must not
    /// wait out a throttle window.
    /// </summary>
    private void RaiseNow() {
        lock (_gate) {
            _lastRaise = _now();
            _trailingScheduled = false;
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// A progress report: fires when the window has passed, otherwise arms a
    /// one-shot timer for the remainder, so the state a caller stopped on
    /// still reaches the screen.
    /// </summary>
    private void RaiseThrottled() {
        TimeSpan wait;
        lock (_gate) {
            var now = _now();
            var since = now - _lastRaise;
            if (since >= _minInterval) {
                _lastRaise = now;
                _trailingScheduled = false;
                wait = TimeSpan.Zero;
            } else {
                if (_trailingScheduled) {
                    return;
                }
                _trailingScheduled = true;
                wait = _minInterval - since;
                // Made under the gate: two reporters arriving together must
                // not each make a timer of their own.
                _trailing ??= new Timer(_ => OnTrailing());
            }
        }

        if (wait == TimeSpan.Zero) {
            Changed?.Invoke(this, EventArgs.Empty);

            return;
        }

        _trailing!.Change(wait, Timeout.InfiniteTimeSpan);
    }

    private void OnTrailing() {
        lock (_gate) {
            if (!_trailingScheduled) {
                return;
            }
            _trailingScheduled = false;
            _lastRaise = _now();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }


    private sealed class OperationProgress {
        public OperationProgress(long id, string verb, int total, long totalBytes, bool bytesAreWork,
            DateTime startedAtUtc, CancellationToken token) {
            Id = id;
            Verb = verb;
            Total = total;
            BytesTotal = totalBytes;
            BytesAreWork = bytesAreWork;
            StartedAtUtc = startedAtUtc;
            Token = token;
        }


        public long Id { get; }

        public string Verb { get; }

        public int Total { get; }

        public bool BytesAreWork { get; }

        public DateTime StartedAtUtc { get; }

        public CancellationToken Token { get; }

        public long BytesTotal { get; set; }

        public int Completed { get; set; }

        public long BytesDone { get; set; }

        public string? CurrentPath { get; set; }


        public OperationSnapshot ToSnapshot() =>
            new(Id, Verb, Completed, Total, CurrentPath, BytesDone, BytesTotal, BytesAreWork, StartedAtUtc, Token);
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
            _owner.RaiseThrottled();
        }

        public void AdvanceBytes(long delta) {
            if (_done || delta == 0) {
                return;
            }
            lock (_owner._gate) {
                _progress.BytesDone += delta;
            }
            _owner.RaiseThrottled();
        }

        public void SetCurrentPath(string? currentPath) {
            if (_done) {
                return;
            }
            lock (_owner._gate) {
                _progress.CurrentPath = currentPath;
            }
            _owner.RaiseThrottled();
        }

        public void SetTotalBytes(long totalBytes) {
            if (_done) {
                return;
            }
            lock (_owner._gate) {
                _progress.BytesTotal = totalBytes;
            }
            _owner.RaiseThrottled();
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

    /// <summary>
    /// Add to the bytes-moved counter. Negative is allowed, and is how an
    /// estimate is trued up: the plan said 4 MB, the copy reported 4.1, the
    /// difference is settled when the item finishes.
    /// </summary>
    void AdvanceBytes(long delta);

    /// <summary>Name the file being worked on without counting it as finished.</summary>
    void SetCurrentPath(string? currentPath);

    /// <summary>
    /// Correct the expected total. The estimate comes from a walk of the
    /// source, and a walk can be wrong - a folder grew, a file was
    /// unreadable.
    /// </summary>
    void SetTotalBytes(long totalBytes);
}


/// <summary>Frozen point-in-time view of one operation. Safe to hand to UI threads.</summary>
/// <param name="BytesAreWork">
/// The two byte numbers are the shell engine's abstract work units, not
/// bytes: show a percentage, never "MB".
/// </param>
/// <param name="Token">
/// The token the operation was started under - how the window that handed
/// it out recognises its own operation among the others.
/// </param>
public sealed record OperationSnapshot(
    long Id, string Verb, int Completed, int Total, string? CurrentPath,
    long BytesDone, long BytesTotal, bool BytesAreWork, DateTime StartedAtUtc,
    CancellationToken Token = default) {

    /// <summary>Bytes are the honest measure where there are any; items otherwise.</summary>
    public bool HasBytes => BytesTotal > 0;

    /// <summary>0..100 by bytes where there are bytes, by items where there are not.</summary>
    public double Percent {
        get {
            if (HasBytes) {
                return Math.Clamp((double)BytesDone * 100.0 / BytesTotal, 0.0, 100.0);
            }

            return Total > 0 ? Math.Clamp((double)Completed * 100.0 / Total, 0.0, 100.0) : 0.0;
        }
    }
}
