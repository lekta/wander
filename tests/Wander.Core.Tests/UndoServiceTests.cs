using Wander.Core.Undo;

namespace Wander.Core.Tests;

public class UndoServiceTests {
    /// <summary>Counts how many times Undo() was invoked + captures the order.</summary>
    private sealed class TrackingAction : IUndoableAction {
        public TrackingAction(string desc, List<string>? log = null) {
            Description = desc;
            Log = log ?? new List<string>();
        }
        public string Description { get; }
        public int UndoCount { get; private set; }
        public List<string> Log { get; }
        public Action? OnUndo { get; set; }
        public void Undo() {
            UndoCount++;
            Log.Add(Description);
            OnUndo?.Invoke();
        }
    }


    // --- Basic stack semantics -----------------------------------------

    [Fact]
    public void NewService_HasNothingToUndo() {
        var svc = new UndoService();

        Assert.False(svc.CanUndo);
        Assert.False(svc.IsBusy);
        Assert.Equal(0, svc.Depth);
        Assert.Null(svc.NextDescription);
    }

    [Fact]
    public void Push_EnablesUndo_AndExposesDescription() {
        var svc = new UndoService();

        svc.Push(new TrackingAction("rename A"));

        Assert.True(svc.CanUndo);
        Assert.Equal(1, svc.Depth);
        Assert.Equal("rename A", svc.NextDescription);
    }

    [Fact]
    public void Push_Null_Throws() {
        var svc = new UndoService();

        Assert.Throws<ArgumentNullException>(() => svc.Push(null!));
    }

    [Fact]
    public void Undo_PopsAndInvokesAction_ReturnsIt() {
        var svc = new UndoService();
        var a = new TrackingAction("a");
        svc.Push(a);

        var returned = svc.Undo();

        Assert.Same(a, returned);
        Assert.Equal(1, a.UndoCount);
        Assert.Equal(0, svc.Depth);
        Assert.False(svc.CanUndo);
    }

    [Fact]
    public void Undo_OnEmptyStack_ReturnsNull_NoSideEffects() {
        var svc = new UndoService();
        int fired = 0;
        svc.Changed += (_, _) => fired++;

        var result = svc.Undo();

        Assert.Null(result);
        Assert.Equal(0, fired);
    }

    [Fact]
    public void Undo_IsLifo() {
        var svc = new UndoService();
        var log = new List<string>();
        svc.Push(new TrackingAction("first", log));
        svc.Push(new TrackingAction("second", log));
        svc.Push(new TrackingAction("third", log));

        svc.Undo();
        svc.Undo();
        svc.Undo();

        Assert.Equal(new[] { "third", "second", "first" }, log);
    }

    [Fact]
    public void Clear_EmptiesStack_AndFiresChanged() {
        var svc = new UndoService();
        svc.Push(new TrackingAction("a"));
        svc.Push(new TrackingAction("b"));
        int fired = 0;
        svc.Changed += (_, _) => fired++;

        svc.Clear();

        Assert.Equal(0, svc.Depth);
        Assert.False(svc.CanUndo);
        Assert.Equal(1, fired);
    }

    [Fact]
    public void Clear_OnEmptyStack_DoesNotFireChanged() {
        var svc = new UndoService();
        int fired = 0;
        svc.Changed += (_, _) => fired++;

        svc.Clear();

        Assert.Equal(0, fired);
    }

    [Fact]
    public void Undo_PropagatesExceptionFromAction_AndStillFiresChanged() {
        var svc = new UndoService();
        var a = new TrackingAction("boom") {
            OnUndo = () => throw new InvalidOperationException("nope"),
        };
        svc.Push(a);
        int firedAfterPush = 0;
        svc.Changed += (_, _) => firedAfterPush++;

        Assert.Throws<InvalidOperationException>(() => svc.Undo());

        // The exception didn't swallow Changed — UI still updates.
        Assert.Equal(1, firedAfterPush);
        // And the failing action was already popped before Undo ran.
        Assert.Equal(0, svc.Depth);
    }


    // --- BeginOperation busy-guard -------------------------------------

    [Fact]
    public void BeginOperation_BlocksUndo_WhileHeld() {
        var svc = new UndoService();
        svc.Push(new TrackingAction("a"));

        using (var _ = svc.BeginOperation()) {
            Assert.True(svc.IsBusy);
            Assert.False(svc.CanUndo);
            Assert.Null(svc.Undo());  // silently no-ops while busy
            Assert.Equal(1, svc.Depth); // unchanged
        }

        Assert.False(svc.IsBusy);
        Assert.True(svc.CanUndo);
    }

    [Fact]
    public void BeginOperation_Nested_RefCounted() {
        var svc = new UndoService();
        svc.Push(new TrackingAction("a"));

        var outer = svc.BeginOperation();
        var inner = svc.BeginOperation();

        Assert.True(svc.IsBusy);
        inner.Dispose();
        Assert.True(svc.IsBusy);  // outer still holding
        outer.Dispose();
        Assert.False(svc.IsBusy);
        Assert.True(svc.CanUndo);
    }

    [Fact]
    public void BeginOperation_DoubleDispose_IsSafe() {
        var svc = new UndoService();
        var guard = svc.BeginOperation();
        guard.Dispose();
        guard.Dispose();  // must not underflow the counter

        Assert.False(svc.IsBusy);
        // A fresh begin/end cycle still works.
        using (svc.BeginOperation()) {
            Assert.True(svc.IsBusy);
        }
        Assert.False(svc.IsBusy);
    }

    [Fact]
    public void BeginOperation_FiresChanged_OnBeginAndEnd() {
        var svc = new UndoService();
        int fired = 0;
        svc.Changed += (_, _) => fired++;

        using (svc.BeginOperation()) {
            Assert.Equal(1, fired);  // begin
        }
        Assert.Equal(2, fired);      // end
    }


    // --- BeginOperation under async load -------------------------------

    [Fact]
    public async Task BeginOperation_HeldAcrossAwait_StillBlocksUndo() {
        var svc = new UndoService();
        svc.Push(new TrackingAction("a"));

        async Task LongRunningOp() {
            using var _ = svc.BeginOperation();
            await Task.Delay(20);
            // While in flight Undo must no-op.
            Assert.Null(svc.Undo());
        }

        var task = LongRunningOp();
        // Until the op completes, busy-guard is engaged.
        await Task.Yield();
        Assert.True(svc.IsBusy);

        await task;
        Assert.False(svc.IsBusy);
        Assert.True(svc.CanUndo);  // never got undone
    }

    [Fact]
    public async Task BeginOperation_ConcurrentOps_RaceFree_OnUndoVisibility() {
        // Two batch-like operations run in parallel; CanUndo must stay false
        // until BOTH release their guard. This is the race that motivates
        // the busy-counter being numeric, not boolean.
        var svc = new UndoService();
        svc.Push(new TrackingAction("a"));

        using var startGate = new ManualResetEventSlim(false);
        using var releaseA = new ManualResetEventSlim(false);
        using var releaseB = new ManualResetEventSlim(false);

        Task opA = Task.Run(() => {
            startGate.Wait();
            using var _ = svc.BeginOperation();
            releaseA.Wait();
        });
        Task opB = Task.Run(() => {
            startGate.Wait();
            using var _ = svc.BeginOperation();
            releaseB.Wait();
        });

        startGate.Set();
        // Spin until both ops have called Begin (busy-counter ≥ 2).
        // 200 ms is way more than enough on any reasonable machine.
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (svc.IsBusy == false || svc.CanUndo == true) {
            if (sw.ElapsedMilliseconds > 200) {
                break;
            }
            await Task.Delay(1);
        }
        Assert.True(svc.IsBusy);
        Assert.False(svc.CanUndo);

        // Release one — CanUndo must still be false (other still busy).
        releaseA.Set();
        await opA;
        Assert.True(svc.IsBusy);
        Assert.False(svc.CanUndo);

        // Release the second — guard fully off, CanUndo back.
        releaseB.Set();
        await opB;
        Assert.False(svc.IsBusy);
        Assert.True(svc.CanUndo);
    }
}
