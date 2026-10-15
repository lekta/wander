using Wander.Core.Operations;
using Wander.Core.Tests.Fakes;

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

    [Fact]
    public async Task WhenIdle_EmptyTracker_IsTrueAtOnce() {
        var tracker = new OperationTracker();

        var idle = tracker.WhenIdleAsync(TimeSpan.FromSeconds(5));

        Assert.True(idle.IsCompletedSuccessfully);
        Assert.True(await idle);
    }

    [Fact]
    public async Task WhenIdle_LiveOperation_IsFalseAfterTheTimeout() {
        var tracker = new OperationTracker();
        using var op = tracker.Begin("Copy", 1);

        bool idle = await tracker.WhenIdleAsync(TimeSpan.FromMilliseconds(50));

        Assert.False(idle);
        Assert.Single(tracker.Snapshot());
    }

    // --- Finished operations -------------------------------------------

    [Fact]
    public void Finish_LeavesTheRunning_AndKeepsARecord_WithTheCounts() {
        var clock = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var tracker = new OperationTracker(TimeSpan.Zero, () => clock);
        var op = tracker.Begin("Copy", 3, totalBytes: 30);
        op.AdvanceBytes(20);
        clock = clock.AddSeconds(5);

        op.Finish(new[] {
            new OperationItem(@"C:\a", @"D:\a", BatchItemStatus.Ok, null),
            new OperationItem(@"C:\b", @"D:\b (1)", BatchItemStatus.Renamed, null),
            new OperationItem(@"C:\c", @"D:\c", BatchItemStatus.Failed, new IOException("boom")),
        }, @"D:\");

        Assert.Empty(tracker.Snapshot());
        var record = Assert.Single(tracker.Finished);
        Assert.Equal(OperationOutcome.Problems, record.Outcome);
        Assert.Equal((3, 2, 1, 0, 0), (record.Total, record.Done, record.Failed, record.Skipped, record.Cancelled));
        Assert.Equal(20, record.Bytes);
        Assert.Equal(TimeSpan.FromSeconds(5), record.Elapsed);
        Assert.Equal(@"D:\", record.Target);
        Assert.True(record.HasProblems);
    }

    [Theory]
    [InlineData(new[] { BatchItemStatus.Ok, BatchItemStatus.Skipped }, OperationOutcome.Done)]
    [InlineData(new[] { BatchItemStatus.Ok, BatchItemStatus.Cancelled }, OperationOutcome.Cancelled)]
    [InlineData(new[] { BatchItemStatus.Failed, BatchItemStatus.Cancelled }, OperationOutcome.Failed)]
    [InlineData(new[] { BatchItemStatus.Merged, BatchItemStatus.Failed }, OperationOutcome.Problems)]
    [InlineData(new BatchItemStatus[0], OperationOutcome.Done)]
    public void Finish_WorksOutTheOutcome_FromTheItems(BatchItemStatus[] statuses, OperationOutcome expected) {
        var tracker = new OperationTracker();
        var op = tracker.Begin("Copy", statuses.Length);

        op.Finish(statuses.Select((s, i) => new OperationItem($@"C:\{i}", null, s,
            s == BatchItemStatus.Failed ? new IOException("boom") : null)).ToList(), null);

        Assert.Equal(expected, tracker.Finished[0].Outcome);
    }

    [Fact]
    public void Finish_ASkipWithAReason_IsAProblem() {
        // A junction left out of a folder copy is skipped, not failed - and
        // still something the user has to be told about.
        var tracker = new OperationTracker();
        var op = tracker.Begin("Copy", 2);

        op.Finish(new[] {
            new OperationItem(@"C:\a", @"D:\a", BatchItemStatus.Ok, null),
            new OperationItem(@"C:\link", @"D:\link", BatchItemStatus.Skipped, new IOException("a link")),
        }, @"D:\");

        Assert.Equal(OperationOutcome.Problems, tracker.Finished[0].Outcome);
    }

    [Fact]
    public void Dispose_WithoutFinish_RecordsDone_OrCancelled_ByTheToken() {
        var tracker = new OperationTracker();
        using var cts = new CancellationTokenSource();
        var done = tracker.Begin("Undo", 2);
        var stopped = tracker.Begin("Extract", 4, token: cts.Token);
        done.Advance();
        done.Advance();
        stopped.Advance();
        cts.Cancel();

        done.Dispose();
        stopped.Dispose();

        var records = tracker.Finished;
        Assert.Equal(2, records.Count);
        // Newest first.
        Assert.Equal(("Extract", OperationOutcome.Cancelled, 1, 3), (records[0].Verb, records[0].Outcome, records[0].Done, records[0].Cancelled));
        Assert.Equal(("Undo", OperationOutcome.Done, 2), (records[1].Verb, records[1].Outcome, records[1].Done));
        Assert.Empty(records[0].Items);
    }

    [Fact]
    public void Finish_ThenDispose_KeepsOneRecord() {
        var tracker = new OperationTracker();
        int fired = 0;
        tracker.Changed += (_, _) => fired++;
        var op = tracker.Begin("Copy", 1);

        op.Finish(new[] { new OperationItem(@"C:\a", @"D:\a", BatchItemStatus.Ok, null) }, @"D:\");
        op.Dispose();

        Assert.Single(tracker.Finished);
        Assert.Equal(2, fired);
    }

    [Fact]
    public void Finished_KeepsTheNewest_UpToTheCeiling() {
        var tracker = new OperationTracker();
        for (int i = 0; i < OperationTracker.MaxFinished + 5; i++) {
            tracker.Begin($"Op{i}", 1).Dispose();
        }

        var records = tracker.Finished;
        Assert.Equal(OperationTracker.MaxFinished, records.Count);
        Assert.Equal($"Op{OperationTracker.MaxFinished + 4}", records[0].Verb);
        Assert.Equal("Op5", records[^1].Verb);
    }

    [Fact]
    public void Record_KeepsTheFirstItems_AndEveryProblem() {
        var tracker = new OperationTracker();
        var op = tracker.Begin("Copy", 500);
        var items = Enumerable.Range(0, 500)
            .Select(i => new OperationItem($@"C:\{i}", $@"D:\{i}",
                i >= 400 ? BatchItemStatus.Cancelled : BatchItemStatus.Ok, null))
            .ToList();

        op.Finish(items, @"D:\");

        var record = tracker.Finished[0];
        Assert.Equal(500, record.Total);
        Assert.Equal(200, record.Items.Count);
        Assert.Equal(300, record.ItemsTruncated);
        Assert.Equal(100, record.Items.Count(i => i.Status == BatchItemStatus.Ok));
        Assert.Equal(@"C:\0", record.Items[0].Source);
        Assert.Equal(100, record.Items.Count(i => i.IsRetriable));
    }

    [Fact]
    public void Dismiss_TakesOneRecordAway_AndSaysSo() {
        var tracker = new OperationTracker();
        tracker.Begin("A", 1).Dispose();
        tracker.Begin("B", 1).Dispose();
        long id = tracker.Finished.Single(r => r.Verb == "A").Id;
        int fired = 0;
        tracker.Changed += (_, _) => fired++;

        tracker.Dismiss(id);
        tracker.Dismiss(id);

        Assert.Equal("B", Assert.Single(tracker.Finished).Verb);
        Assert.Equal(1, fired);
    }

    [Fact]
    public void DismissFinished_LeavesTheProblems_UnlessAskedForAll() {
        var tracker = new OperationTracker();
        tracker.Begin("Fine", 1).Dispose();
        tracker.Begin("Bad", 1).Finish(new[] {
            new OperationItem(@"C:\a", @"D:\a", BatchItemStatus.Failed, new IOException("boom")),
        }, @"D:\");

        tracker.DismissFinished(problemsToo: false);
        Assert.Equal("Bad", Assert.Single(tracker.Finished).Verb);

        tracker.DismissFinished(problemsToo: true);
        Assert.Empty(tracker.Finished);
    }

    [Fact]
    public void TheComputerIsKeptAwake_FromTheFirstOperation_ToTheLast() {
        var power = new FakePowerGuard();
        var tracker = new OperationTracker(power: power);

        var copy = tracker.Begin("Copy", 1);
        var move = tracker.Begin("Move", 1);
        Assert.Equal(1, power.Held);

        copy.Dispose();
        Assert.Equal(1, power.Held);

        move.Finish(Array.Empty<OperationItem>(), null);
        Assert.Equal(0, power.Held);

        // A finished record holds nothing; the next operation asks again.
        using var next = tracker.Begin("Copy", 1);
        Assert.Equal(1, power.Held);
        // No string table in a test: the key comes back as it is.
        Assert.Equal(new[] { "PowerRequestReason", "PowerRequestReason" }, power.Reasons);
    }

    [Fact]
    public async Task WhenIdle_DoesNotWaitForTheFinished() {
        var tracker = new OperationTracker();
        tracker.Begin("Copy", 1).Finish(new[] {
            new OperationItem(@"C:\a", @"D:\a", BatchItemStatus.Failed, new IOException("boom")),
        }, @"D:\");

        Assert.True(await tracker.WhenIdleAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task WhenIdle_OperationFinishesInTime_IsTrue() {
        // What an exit waits for: the last handle closing, from whatever
        // thread the work ends on.
        var tracker = new OperationTracker();
        var first = tracker.Begin("Copy", 1);
        var second = tracker.Begin("Move", 1);

        var idle = tracker.WhenIdleAsync(TimeSpan.FromSeconds(5));
        first.Dispose();
        Assert.False(idle.IsCompleted);
        _ = Task.Run(second.Dispose);

        Assert.True(await idle);
    }
}
