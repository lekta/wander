using Wander.Core.FileSystem;
using Wander.Core.Tests.Fakes;
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


    /// <summary>A service with the real actions in reach; nothing is undone, so the fakes stay empty.</summary>
    private static (UndoService Svc, FakeFileSystem Fs, FakeRecycleBin Bin) Paths() {
        var fs = new FakeFileSystem();

        return (new UndoService(), fs, new FakeRecycleBin(fs));
    }

    /// <summary>
    /// The history from the newest step down, by description. Taken off by
    /// undoing: the fakes hold none of the files, and an undo that throws
    /// has been popped all the same.
    /// </summary>
    private static string[] Descriptions(UndoService svc) {
        var found = new List<string>();
        while (svc.NextDescription is { } next) {
            found.Add(next);
            try {
                svc.Undo();
            } catch (IOException) {
                // Nothing to restore or recycle in the fakes.
            }
        }

        return found.ToArray();
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

    // --- Forget: after a permanent delete ------------------------------

    [Fact]
    public void Forget_DropsTheChainThatLedToTheItem_KeepsTheRest() {
        var (svc, fs, bin) = Paths();
        svc.Push(new RenameAction(fs, @"C:\u\y.txt", "x.txt"));
        svc.Push(new RenameAction(fs, @"C:\a\b.txt", "a.txt"));
        svc.Push(new MoveAction(fs, @"C:\a\b.txt", @"C:\d\b.txt"));
        svc.Push(new CreateAction(bin, @"C:\d\new"));
        int fired = 0;
        svc.Changed += (_, _) => fired++;

        svc.Forget(new[] { @"C:\d\b.txt" });

        Assert.Equal(1, fired);
        Assert.Equal(new[] { "Create 'new'", "Rename to 'y.txt'" }, Descriptions(svc));
    }

    [Fact]
    public void Forget_Bundle_KeepsTheStepsNotTouched() {
        var (svc, fs, _) = Paths();
        var one = new MoveAction(fs, @"C:\a\1", @"C:\d\1");
        var two = new MoveAction(fs, @"C:\a\2", @"C:\d\2");
        var three = new MoveAction(fs, @"C:\a\3", @"C:\d\3");
        svc.Push(new CompositeAction("move of 3 items", new IUndoableAction[] { one, two, three }));

        svc.Forget(new[] { @"C:\d\2" });

        Assert.Equal("move of 3 items", svc.NextDescription);
        Assert.Equal(new IUndoableAction[] { one, three }, svc.Undo()!.Steps);
    }

    [Fact]
    public void Forget_Folder_TakesWhatWasInside() {
        var (svc, fs, _) = Paths();
        svc.Push(new MoveAction(fs, @"C:\a\x.txt", @"C:\d\sub\x.txt"));

        svc.Forget(new[] { @"C:\d\" });

        Assert.Equal(0, svc.Depth);
    }

    [Fact]
    public void Forget_NameUsedAgain_OlderStepStays() {
        // "New folder" made, renamed to Photos, another "New folder" made
        // and deleted for good: Photos is still the first one's to undo.
        var (svc, fs, bin) = Paths();
        svc.Push(new CreateAction(bin, @"C:\p\New folder"));
        svc.Push(new RenameAction(fs, @"C:\p\Photos", "New folder"));
        svc.Push(new CreateAction(bin, @"C:\p\New folder"));

        svc.Forget(new[] { @"C:\p\New folder" });

        Assert.Equal(new[] { "Rename to 'Photos'", "Create 'New folder'" }, Descriptions(svc));
    }

    [Fact]
    public void Forget_FollowsARenamedFolderBackInTime() {
        // sub made inside D, D renamed to E, E\sub deleted for good.
        var (svc, fs, bin) = Paths();
        svc.Push(new CreateAction(bin, @"C:\p\D\sub"));
        svc.Push(new RenameAction(fs, @"C:\p\E", "D"));

        svc.Forget(new[] { @"C:\p\E\sub" });

        Assert.Equal(new[] { "Rename to 'E'" }, Descriptions(svc));
    }

    [Fact]
    public void Forget_RestoreFromBin_Stays() {
        var (svc, _, bin) = Paths();
        svc.Push(new DeleteAction(bin, new RecycleHandle(@"C:\p\old.txt", DateTime.UtcNow)));

        svc.Forget(new[] { @"C:\p\old.txt" });

        Assert.Equal(1, svc.Depth);
    }

    [Fact]
    public void Forget_NothingTouched_DoesNotFireChanged() {
        var (svc, fs, _) = Paths();
        svc.Push(new RenameAction(fs, @"C:\a\b.txt", "a.txt"));
        int fired = 0;
        svc.Changed += (_, _) => fired++;

        svc.Forget(new[] { @"C:\elsewhere" });

        Assert.Equal(1, svc.Depth);
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

        using var releaseA = new ManualResetEventSlim(false);
        using var releaseB = new ManualResetEventSlim(false);
        // Each op announces that its guard is engaged. Awaiting both signals
        // is what makes the busy-counter observation deterministic: a timed
        // spin here passed or failed depending on how loaded the thread pool
        // was when the second op got scheduled.
        var enteredA = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var enteredB = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        Task opA = Task.Run(() => {
            using var _ = svc.BeginOperation();
            enteredA.SetResult();
            releaseA.Wait();
        });
        Task opB = Task.Run(() => {
            using var _ = svc.BeginOperation();
            enteredB.SetResult();
            releaseB.Wait();
        });

        // Both guards are engaged from here on (busy-counter == 2).
        await Task.WhenAll(enteredA.Task, enteredB.Task);
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
