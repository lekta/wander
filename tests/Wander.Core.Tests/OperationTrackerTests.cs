using Wander.Core.Operations;

namespace Wander.Core.Tests;

public class OperationTrackerTests {
    [Fact]
    public void Begin_AddsSnapshotEntry_WithZeroCompleted() {
        var tracker = new OperationTracker();

        using var op = tracker.Begin("Copy", total: 10);

        var snap = tracker.Snapshot();
        Assert.Single(snap);
        Assert.Equal("Copy", snap[0].Verb);
        Assert.Equal(0, snap[0].Completed);
        Assert.Equal(10, snap[0].Total);
        Assert.Null(snap[0].CurrentPath);
        Assert.False(snap[0].HasBytes);
    }

    [Fact]
    public void Advance_BumpsCompleted_AndCapturesCurrentPath() {
        var tracker = new OperationTracker();
        using var op = tracker.Begin("Move", 3);

        op.Advance(@"C:\a.txt");
        op.Advance(@"C:\b.txt");

        var snap = tracker.Snapshot();
        Assert.Equal(2, snap[0].Completed);
        Assert.Equal(@"C:\b.txt", snap[0].CurrentPath);
    }

    [Fact]
    public void Dispose_RemovesFromTracker() {
        var tracker = new OperationTracker();
        var op = tracker.Begin("Delete", 1);
        Assert.Single(tracker.Snapshot());

        op.Dispose();

        Assert.Empty(tracker.Snapshot());
    }

    [Fact]
    public void Dispose_Twice_IsSafe() {
        var tracker = new OperationTracker();
        var op = tracker.Begin("Copy", 1);

        op.Dispose();
        op.Dispose();  // must not double-remove or throw

        Assert.Empty(tracker.Snapshot());
    }

    [Fact]
    public void Advance_AfterDispose_IsNoOp() {
        var tracker = new OperationTracker();
        var op = tracker.Begin("Copy", 5);

        op.Dispose();
        op.Advance(@"C:\x");  // late progress report — must not crash

        Assert.Empty(tracker.Snapshot());
    }

    [Fact]
    public void Snapshot_IsImmutable_PostHoc() {
        var tracker = new OperationTracker();
        using var op = tracker.Begin("Copy", 10);

        var snap = tracker.Snapshot();
        op.Advance(@"C:\x");

        // The previously-captured snapshot does NOT see the new Advance.
        Assert.Equal(0, snap[0].Completed);
        Assert.Null(snap[0].CurrentPath);
        // A fresh snapshot does.
        var snap2 = tracker.Snapshot();
        Assert.Equal(1, snap2[0].Completed);
        Assert.Equal(@"C:\x", snap2[0].CurrentPath);
    }

    [Fact]
    public void MultipleOps_CoexistIndependently_InSnapshot() {
        var tracker = new OperationTracker();
        using var copy = tracker.Begin("Copy", 3);
        using var move = tracker.Begin("Move", 5);

        copy.Advance(@"C:\a");
        move.Advance(@"C:\b");
        move.Advance(@"C:\c");

        var snap = tracker.Snapshot();
        Assert.Equal(2, snap.Count);
        var copySnap = snap.First(s => s.Verb == "Copy");
        var moveSnap = snap.First(s => s.Verb == "Move");
        Assert.Equal(1, copySnap.Completed);
        Assert.Equal(2, moveSnap.Completed);
    }

    [Fact]
    public void Changed_Fires_OnBegin_Advance_Dispose() {
        // The clock steps past the throttle window between calls, so every
        // one of them goes out.
        var clock = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var tracker = new OperationTracker(TimeSpan.FromMilliseconds(100), () => clock);
        int fired = 0;
        tracker.Changed += (_, _) => fired++;

        var op = tracker.Begin("Copy", 2);   // +1 (begin)
        clock = clock.AddSeconds(1);
        op.Advance(@"C:\a");                 // +1 (advance)
        clock = clock.AddSeconds(1);
        op.Advance(@"C:\b");                 // +1 (advance)
        op.Dispose();                        // +1 (remove)

        Assert.Equal(4, fired);
    }

    [Fact]
    public void Changed_IsThrottled_ForProgressReports() {
        // A copy reports every buffer; the screen does not need to know.
        var clock = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var tracker = new OperationTracker(TimeSpan.FromMilliseconds(100), () => clock);
        int fired = 0;
        tracker.Changed += (_, _) => fired++;

        var op = tracker.Begin("Copy", 1, totalBytes: 1000);   // +1 (begin)
        for (int i = 0; i < 50; i++) {
            op.AdvanceBytes(10);
        }

        // Every report in between was folded away - the notifications, not
        // the numbers: the snapshot has all 500 bytes.
        Assert.Equal(1, fired);
        Assert.Equal(500, tracker.Snapshot()[0].BytesDone);

        op.Dispose();                                          // +1 (remove)
        Assert.Equal(2, fired);
    }

    [Fact]
    public void Changed_StructuralEvents_AreNeverThrottled() {
        // Two operations beginning in the same millisecond must both show
        // up: a window opens on this event.
        var clock = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var tracker = new OperationTracker(TimeSpan.FromMilliseconds(100), () => clock);
        int fired = 0;
        tracker.Changed += (_, _) => fired++;

        using var a = tracker.Begin("Copy", 1);
        using var b = tracker.Begin("Move", 1);

        Assert.Equal(2, fired);
    }

    [Fact]
    public async Task Changed_TrailingUpdate_Arrives_AfterTheWindow() {
        // The last state before a caller goes quiet must still reach the
        // screen, or a bar freezes at whatever the throttle let through.
        var tracker = new OperationTracker(TimeSpan.FromMilliseconds(20));
        using var op = tracker.Begin("Copy", 1, totalBytes: 100);

        var arrived = new TaskCompletionSource();
        tracker.Changed += (_, _) => {
            if (tracker.Snapshot() is [{ BytesDone: 100 }]) {
                arrived.TrySetResult();
            }
        };
        op.AdvanceBytes(100);   // inside the window right after Begin: suppressed

        await arrived.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void AdvanceBytes_AddsUp_AndAcceptsATrueUp() {
        var tracker = new OperationTracker();
        using var op = tracker.Begin("Copy", 1, totalBytes: 1000);

        op.AdvanceBytes(400);
        op.AdvanceBytes(300);
        op.AdvanceBytes(-100);   // the estimate was high; settle it

        var snap = tracker.Snapshot()[0];
        Assert.Equal(600, snap.BytesDone);
        Assert.Equal(1000, snap.BytesTotal);
        Assert.True(snap.HasBytes);
        Assert.Equal(60.0, snap.Percent, 3);
    }

    [Fact]
    public void Percent_FallsBackToItems_WhenThereAreNoBytes() {
        var tracker = new OperationTracker();
        using var op = tracker.Begin("Recycle", total: 4);

        op.Advance(@"C:\a");

        var snap = tracker.Snapshot()[0];
        Assert.False(snap.HasBytes);
        Assert.Equal(25.0, snap.Percent, 3);
    }

    [Fact]
    public void Percent_IsClamped_WhenTheEstimateWasLow() {
        var tracker = new OperationTracker();
        using var op = tracker.Begin("Copy", 1, totalBytes: 100);

        op.AdvanceBytes(250);

        Assert.Equal(100.0, tracker.Snapshot()[0].Percent, 3);
    }

    [Fact]
    public void SetTotalBytes_CorrectsTheEstimate() {
        var tracker = new OperationTracker();
        using var op = tracker.Begin("Copy", 1);

        Assert.False(tracker.Snapshot()[0].HasBytes);
        op.SetTotalBytes(2048);

        Assert.True(tracker.Snapshot()[0].HasBytes);
        Assert.Equal(2048, tracker.Snapshot()[0].BytesTotal);
    }

    [Fact]
    public void SetCurrentPath_NamesTheFile_WithoutCountingIt() {
        var tracker = new OperationTracker();
        using var op = tracker.Begin("Copy", 3);

        op.SetCurrentPath(@"C:\big.iso");

        var snap = tracker.Snapshot()[0];
        Assert.Equal(@"C:\big.iso", snap.CurrentPath);
        Assert.Equal(0, snap.Completed);
    }

    [Fact]
    public void BytesAreWork_TravelsToTheSnapshot() {
        // Extraction has no byte counts to give - the shell engine reports
        // its own units, and the display has to know not to write "MB".
        var tracker = new OperationTracker();
        using var op = tracker.Begin("Extract", 2, totalBytes: 0, bytesAreWork: true);

        op.SetTotalBytes(10);
        op.AdvanceBytes(5);

        var snap = tracker.Snapshot()[0];
        Assert.True(snap.BytesAreWork);
        Assert.Equal(50.0, snap.Percent, 3);
    }

    [Fact]
    public void AdvanceBytes_AfterDispose_IsNoOp() {
        var tracker = new OperationTracker();
        var op = tracker.Begin("Copy", 1, totalBytes: 10);

        op.Dispose();
        op.AdvanceBytes(5);
        op.SetCurrentPath(@"C:\x");
        op.SetTotalBytes(99);

        Assert.Empty(tracker.Snapshot());
    }

    [Fact]
    public async Task ConcurrentAdvances_AreSerialised_NoLostUpdates() {
        // The tracker is shared between batch operations running on the
        // thread pool; Advance must be atomic so the Completed count is
        // exactly the number of advances called, with no torn reads.
        var tracker = new OperationTracker();
        using var op = tracker.Begin("Stress", total: 1000);

        var tasks = Enumerable.Range(0, 1000)
            .Select(i => Task.Run(() => op.Advance($"item-{i}")))
            .ToArray();
        await Task.WhenAll(tasks);

        var snap = tracker.Snapshot();
        Assert.Equal(1000, snap[0].Completed);
    }

    [Fact]
    public async Task ConcurrentAdvanceBytes_AreSerialised_NoLostBytes() {
        var tracker = new OperationTracker();
        using var op = tracker.Begin("Stress", total: 1, totalBytes: 1000);

        var tasks = Enumerable.Range(0, 1000)
            .Select(_ => Task.Run(() => op.AdvanceBytes(1)))
            .ToArray();
        await Task.WhenAll(tasks);

        Assert.Equal(1000, tracker.Snapshot()[0].BytesDone);
    }

    [Fact]
    public void Begin_CarriesTheTokenIntoTheSnapshot() {
        // The window that handed out the token finds its operation by it -
        // two operations in flight, and each window has to know its own.
        var tracker = new OperationTracker();
        using var cts = new CancellationTokenSource();

        using var mine = tracker.Begin("Copy", 1, token: cts.Token);
        using var other = tracker.Begin("Move", 1);

        var snaps = tracker.Snapshot();
        Assert.Equal(cts.Token, snaps[0].Token);
        Assert.Equal(CancellationToken.None, snaps[1].Token);
        Assert.NotEqual(snaps[0].Token, snaps[1].Token);
    }
}
