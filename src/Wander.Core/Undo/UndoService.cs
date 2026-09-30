using Wander.Core.Operations;

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
    private readonly Lock _gate = new();
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


    /// <summary>
    /// Pop the most recent action and undo it as an operation of its own:
    /// off the caller's thread, in the tracker like a copy or a delete, step
    /// by step for a bundle. Null when the stack was empty or busy.
    ///
    /// <para>
    /// An undo is a file operation run backwards - a restore for a delete, a
    /// move back for a move - so it can take as long and is treated alike:
    /// it shows progress, <see cref="CanUndo"/> is false while it runs, and
    /// it can be cancelled. Cancelled, the steps not reached yet go back on
    /// the stack under the same description, and the next <c>Ctrl+Z</c> goes
    /// on from there. A step that fails is reported and left behind - the
    /// rest still comes back; putting the failed one on the stack again would
    /// wedge everything under it behind an item that is, say, no longer in
    /// the bin.
    /// </para>
    ///
    /// <para>
    /// What is left goes on top of whatever an operation finishing meanwhile
    /// has pushed: slightly out of order, and harmless - the two do not
    /// depend on each other, or the second could not have run.
    /// </para>
    /// </summary>
    /// <param name="tracker">Where the undo shows as an operation.</param>
    /// <param name="ct">Stops it between steps.</param>
    /// <param name="claims">
    /// Where what the undo puts back, and where from, is claimed while it
    /// runs - so another operation names the undo rather than fighting it.
    /// </param>
    public async Task<UndoOutcome?> UndoAsync(OperationTracker tracker, CancellationToken ct = default, PathClaims? claims = null) {
        IUndoableAction action;
        lock (_gate) {
            if (_busy > 0 || !_stack.TryPop(out action!)) {
                return null;
            }
            _busy++;
        }
        RaiseChanged();

        try {
            var steps = action.Steps;
            using var op = tracker.Begin(OperationVerbs.Undo, steps.Count, token: ct);
            var touched = action.MovesOnUndo.SelectMany(m => new[] { m.From, m.To }).Concat(action.PathsAfterUndo);
            using var claim = claims?.Claim(touched, ClaimKind.UserOperation, OperationVerbs.Undo);
            var outcome = await Task.Run(() => Unwind(action, steps, op, ct), CancellationToken.None).ConfigureAwait(false);
            if (outcome.Remaining is { } remaining) {
                lock (_gate) {
                    _stack.Push(remaining);
                }
            }

            return outcome;
        } finally {
            EndOperation();
        }
    }


    /// <summary>
    /// Drops what can no longer be undone now that <paramref name="gone"/>
    /// left the disk for good: every step whose undo would not find its
    /// item where it left it (<see cref="IUndoableAction.PathsBeforeUndo"/>),
    /// and every older step that depended on one of those. The rest stays -
    /// an unrelated rename is as undoable after a permanent delete as before.
    ///
    /// <para>
    /// Walks the history from the newest step back, carrying the paths whose
    /// content is gone as they were at each point in time. A step that drops
    /// adds what its undo would have brought back. A step that stays takes
    /// the gone paths back through its move, and clears the places its undo
    /// fills - whatever went from there arrived after it. A bundle keeps the
    /// steps that survive (<see cref="IUndoableAction.WithSteps"/>).
    /// </para>
    /// </summary>
    public void Forget(IReadOnlyCollection<string> gone) {
        bool changed = false;
        lock (_gate) {
            var dead = gone.ToList();
            var kept = new List<IUndoableAction>(_stack.Count);
            // A stack enumerates newest first.
            foreach (var action in _stack) {
                var steps = action.Steps;
                var survivors = new List<IUndoableAction>(steps.Count);
                for (int i = steps.Count - 1; i >= 0; i--) {
                    if (Survives(steps[i], dead)) {
                        survivors.Add(steps[i]);
                    }
                }

                if (survivors.Count == steps.Count) {
                    kept.Add(action);
                    continue;
                }

                changed = true;
                if (survivors.Count > 0) {
                    survivors.Reverse();
                    kept.Add(action.WithSteps(survivors));
                }
            }

            if (changed) {
                _stack.Clear();
                for (int i = kept.Count - 1; i >= 0; i--) {
                    _stack.Push(kept[i]);
                }
            }
        }
        if (changed) {
            RaiseChanged();
        }
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


    private static UndoOutcome Unwind(
        IUndoableAction action, IReadOnlyList<IUndoableAction> steps, IOperationHandle op, CancellationToken ct) {
        var undone = new List<IUndoableAction>(steps.Count);
        var failures = new List<UndoFailure>();
        int next = steps.Count - 1;

        // Last step first, so dependent ones unwind correctly.
        for (; next >= 0; next--) {
            if (ct.IsCancellationRequested) {
                break;
            }

            var step = steps[next];
            string? path = step.PathsAfterUndo.Count > 0 ? step.PathsAfterUndo[0] : null;
            if (path is not null) {
                op.SetCurrentPath(path);
            }
            try {
                step.Undo();
                undone.Add(step);
            } catch (Exception ex) {
                failures.Add(new UndoFailure(step, ex));
            }
            op.Advance(path ?? step.Description);
        }

        // Back into the order they were done in - that is what a bundle holds.
        undone.Reverse();
        bool whole = undone.Count == steps.Count;

        return new UndoOutcome(
            Undone: undone.Count == 0 ? null : whole ? action : action.WithSteps(undone),
            Remaining: next < 0 ? null : action.WithSteps(steps.Take(next + 1).ToList()),
            Failures: failures,
            Cancelled: next >= 0);
    }

    /// <summary>
    /// Whether <paramref name="step"/> can still be undone; on the way,
    /// <paramref name="dead"/> is taken back to the moment before the step
    /// was done.
    /// </summary>
    private static bool Survives(IUndoableAction step, List<string> dead) {
        var back = step.MovesOnUndo.Select(m => m.To).Concat(step.PathsAfterUndo).ToList();
        if (step.PathsBeforeUndo.Any(p => dead.Any(d => Rebase(p, d, d) is not null))) {
            // What it would have brought back is lost with it.
            dead.AddRange(back);

            return false;
        }

        dead.RemoveAll(d => back.Any(b => Rebase(d, b, b) is not null));
        foreach (var (from, to) in step.MovesOnUndo) {
            for (int i = 0; i < dead.Count; i++) {
                if (Rebase(dead[i], from, to) is { } before) {
                    dead[i] = before;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// <paramref name="path"/> with <paramref name="root"/> replaced by
    /// <paramref name="newRoot"/>; null when it is neither the root nor
    /// inside it. Without case and trailing separators - the rule of
    /// <c>FileSystem.PathRewrite</c>, which this folder does not see.
    /// </summary>
    private static string? Rebase(string path, string root, string newRoot) {
        string p = Path.TrimEndingDirectorySeparator(path);
        string r = Path.TrimEndingDirectorySeparator(root);
        if (string.Equals(p, r, StringComparison.OrdinalIgnoreCase)) {
            return Path.TrimEndingDirectorySeparator(newRoot);
        }

        string prefix = Path.EndsInDirectorySeparator(r) ? r : r + Path.DirectorySeparatorChar;

        return p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? Path.Combine(newRoot, p[prefix.Length..])
            : null;
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
