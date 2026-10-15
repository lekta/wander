using Wander.Core.Localization;

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
///
/// <para>
/// A finished operation stays as an <see cref="OperationRecord"/> until the
/// user clears it away (<see cref="Dismiss"/>), at most
/// <see cref="MaxFinished"/> of them: what went wrong waits for the user to
/// read it, what went right is there to look back at. Only the running ones
/// are anybody's business but the status bar's - <see cref="Snapshot"/> and
/// <see cref="WhenIdleAsync"/> know nothing of the finished.
/// </para>
/// </summary>
public sealed class OperationTracker {
    /// <summary>Ten redraws a second is more than an eye needs and far less than a copy reports.</summary>
    public static readonly TimeSpan MinInterval = TimeSpan.FromMilliseconds(100);

    /// <summary>How many finished operations are kept; the oldest give way.</summary>
    public const int MaxFinished = 50;


    private readonly Lock _gate = new();
    private readonly List<OperationProgress> _ops = new();

    /// <summary>Newest first.</summary>
    private readonly List<OperationRecord> _finished = new();
    private readonly TimeSpan _minInterval;
    private readonly Func<DateTime> _now;
    private readonly IPowerGuard? _power;

    private DateTime _lastRaise = DateTime.MinValue;
    private Timer? _trailing;
    private bool _trailingScheduled;
    private long _lastId;

    /// <summary>Held from the first running operation to the last: the computer does not sleep under a copy.</summary>
    private IDisposable? _awake;


    /// <param name="minInterval">How rarely <see cref="Changed"/> may fire for progress reports.</param>
    /// <param name="now">The clock, so a test can hold it still.</param>
    /// <param name="power">Keeps the computer awake while anything runs; null lets it sleep.</param>
    public OperationTracker(TimeSpan? minInterval = null, Func<DateTime>? now = null, IPowerGuard? power = null) {
        _minInterval = minInterval ?? MinInterval;
        _now = now ?? (() => DateTime.UtcNow);
        _power = power;
    }


    /// <summary>Fires after any handle is created, advanced, or completed, and after a record is dismissed.</summary>
    public event EventHandler? Changed;


    /// <summary>The finished operations not yet cleared away, newest first.</summary>
    public IReadOnlyList<OperationRecord> Finished {
        get {
            lock (_gate) {
                return _finished.ToArray();
            }
        }
    }


    /// <summary>
    /// Register a new operation. Finish the returned handle with its items
    /// when the work is over (<see cref="IOperationHandle.Finish"/>), or
    /// dispose it: either moves the entry from the running to the finished.
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
            if (_power is not null && _awake is null) {
                _awake = _power.KeepAwake(Text.Get("PowerRequestReason"));
            }
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

    /// <summary>One finished operation off the list - the row's "Убрать", and what a retry does to the record it replaces.</summary>
    public void Dismiss(long id) {
        int removed;
        lock (_gate) {
            removed = _finished.RemoveAll(r => r.Id == id);
        }
        if (removed > 0) {
            RaiseNow();
        }
    }

    /// <summary>
    /// Clears the finished operations away: the ones that went well, or with
    /// <paramref name="problemsToo"/> every one of them.
    /// </summary>
    public void DismissFinished(bool problemsToo) {
        int removed;
        lock (_gate) {
            removed = _finished.RemoveAll(r => problemsToo || !r.HasProblems);
        }
        if (removed > 0) {
            RaiseNow();
        }
    }

    /// <summary>
    /// Completes with true once no operation is running - at once when
    /// none is - and with false when <paramref name="timeout"/> runs out
    /// first. What an exit waits on: the handles close only after the work
    /// has let go of its files.
    ///
    /// <para>
    /// Never resumes on the caller's context, so a crash handler may block
    /// on it from the UI thread without waiting on that same thread.
    /// </para>
    /// </summary>
    public async Task<bool> WhenIdleAsync(TimeSpan timeout) {
        var idle = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        void OnChanged(object? sender, EventArgs e) {
            if (IsIdle()) {
                idle.TrySetResult();
            }
        }

        // Subscribed before the first look, so an operation finishing in
        // between is not missed.
        Changed += OnChanged;
        try {
            OnChanged(this, EventArgs.Empty);
            await idle.Task.WaitAsync(timeout).ConfigureAwait(false);

            return true;
        } catch (TimeoutException) {
            return false;
        } finally {
            Changed -= OnChanged;
        }
    }


    private bool IsIdle() {
        lock (_gate) {
            return _ops.Count == 0;
        }
    }

    /// <summary>
    /// The operation stops running and becomes a record: from its items when
    /// it gave them, from its counters and its token when it did not.
    /// </summary>
    private OperationRecord Complete(OperationProgress op, IReadOnlyList<OperationItem>? items, string? target) {
        OperationRecord record;
        IDisposable? asleep = null;
        lock (_gate) {
            _ops.Remove(op);
            if (_ops.Count == 0) {
                asleep = _awake;
                _awake = null;
            }
            var now = _now();
            var elapsed = now - op.StartedAtUtc;
            long bytes = op.BytesAreWork ? 0 : op.BytesDone;
            record = items is null
                ? OperationRecord.Unitemised(op.Id, op.Verb, op.Total, op.Completed, op.Token.IsCancellationRequested, bytes, elapsed, now)
                : OperationRecord.FromItems(op.Id, op.Verb, items, bytes, elapsed, now, target);
            _finished.Insert(0, record);
            if (_finished.Count > MaxFinished) {
                _finished.RemoveRange(MaxFinished, _finished.Count - MaxFinished);
            }
        }
        asleep?.Dispose();
        RaiseNow();

        return record;
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

        public bool IsWeighing { get; set; }

        public int Attempt { get; set; }


        public OperationSnapshot ToSnapshot() =>
            new(Id, Verb, Completed, Total, CurrentPath, BytesDone, BytesTotal, BytesAreWork, StartedAtUtc, Token, IsWeighing, Attempt);
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

        public void SetWeighing(bool weighing) {
            if (_done) {
                return;
            }
            lock (_owner._gate) {
                _progress.IsWeighing = weighing;
            }
            _owner.RaiseThrottled();
        }

        public void SetAttempt(int attempt) {
            if (_done) {
                return;
            }
            lock (_owner._gate) {
                _progress.Attempt = attempt;
            }
            _owner.RaiseThrottled();
        }

        public OperationRecord? Finish(IReadOnlyList<OperationItem> items, string? target) {
            if (_done) {
                return null;
            }
            _done = true;

            return _owner.Complete(_progress, items, target);
        }

        public void Dispose() {
            if (_done) {
                return;
            }
            _done = true;
            _owner.Complete(_progress, null, null);
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

    /// <summary>
    /// The sources are being weighed before anything moves: there are no
    /// numbers yet worth showing, and the display says "counting" instead of
    /// a bar standing at zero.
    /// </summary>
    void SetWeighing(bool weighing);

    /// <summary>
    /// The item in hand is being tried again after a transient error: this
    /// is try number <paramref name="attempt"/>; 0 once it is over. A number,
    /// not words - Core has none; the display writes "try 2 of 3".
    /// </summary>
    void SetAttempt(int attempt);

    /// <summary>
    /// The work is over, and this is how each item went: the operation
    /// leaves the running and stays as a record with an outcome worked out
    /// from <paramref name="items"/>. A later <see cref="IDisposable.Dispose"/>
    /// does nothing; a dispose without this records the operation as done,
    /// or as cancelled when its token was.
    /// </summary>
    /// <param name="target">The folder it went into; null for a delete.</param>
    /// <returns>The record it left; null when the handle was already finished or disposed.</returns>
    OperationRecord? Finish(IReadOnlyList<OperationItem> items, string? target);
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
/// <param name="IsWeighing">The sources are still being weighed; the counts mean nothing yet.</param>
/// <param name="Attempt">The item in hand is on this try after a transient error; 0 when it is on its first.</param>
public sealed record OperationSnapshot(
    long Id, string Verb, int Completed, int Total, string? CurrentPath,
    long BytesDone, long BytesTotal, bool BytesAreWork, DateTime StartedAtUtc,
    CancellationToken Token = default, bool IsWeighing = false, int Attempt = 0) {

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
