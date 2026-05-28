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
        var tracker = new OperationTracker();
        int fired = 0;
        tracker.Changed += (_, _) => fired++;

        var op = tracker.Begin("Copy", 2);   // +1 (begin)
        op.Advance(@"C:\a");                 // +1 (advance)
        op.Advance(@"C:\b");                 // +1 (advance)
        op.Dispose();                        // +1 (remove)

        Assert.Equal(4, fired);
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
}
