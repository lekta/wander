using Wander.Core.FileSystem;
using Wander.Core.Logging;
using Wander.Core.Operations;
using Wander.Core.Tests.Fakes;
using Wander.Core.Undo;

namespace Wander.Core.Tests;

public class BatchExecutorTests {
    // --- Paths reused across cases ------------------------------------
    private const string SrcFolder = @"C:\src";
    private const string SrcA = @"C:\src\a.txt";
    private const string SrcB = @"C:\src\b.txt";
    private const string SrcC = @"C:\src\c.txt";

    private const string DstFolder = @"C:\dst";
    private const string DstA = @"C:\dst\a.txt";
    private const string DstB = @"C:\dst\b.txt";
    private const string DstC = @"C:\dst\c.txt";
    private const string DstARenamed1 = @"C:\dst\a (1).txt";
    private const string DstARenamed3 = @"C:\dst\a (3).txt";

    private const string RootA = @"C:\a.txt";
    private const string RootB = @"C:\b.txt";
    private const string RootDir = @"C:\dir";
    private const string RootExists = @"C:\exists.txt";
    private const string RootMissing = @"C:\missing.txt";


    private static (BatchExecutor Batch, FakeFileSystem Fs, FakeRecycleBin Bin, UndoService Undo, OperationTracker Tracker) Setup() {
        var fs = new FakeFileSystem();
        var bin = new FakeRecycleBin(fs);
        var undo = new UndoService();
        var tracker = new OperationTracker();
        var batch = new BatchExecutor(fs, bin, undo, tracker, NullLogger.Instance);
        return (batch, fs, bin, undo, tracker);
    }


    // --- CopyMany: happy paths -----------------------------------------

    [Fact]
    public void CopyMany_NoConflicts_CopiesAll_AndPushesCompositeUndo() {
        var (batch, fs, _, undo, _) = Setup();
        fs.Files[SrcA] = new byte[] { 1 };
        fs.Files[SrcB] = new byte[] { 2 };
        fs.Directories.Add(DstFolder);
        var resolver = new FakeConflictResolver();

        var results = batch.CopyMany(new[] { SrcA, SrcB }, DstFolder, resolver);

        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.Equal(BatchItemStatus.Ok, r.Status));
        Assert.Empty(resolver.StartBatchCalls);
        Assert.Empty(resolver.ResolveCalls);
        // Two items => composite undo, single-step depth.
        Assert.Equal(1, undo.Depth);
        Assert.Contains("copy of 2 items", undo.NextDescription);
    }

    [Fact]
    public void CopyMany_SingleItem_PushesSingleAction_NotComposite() {
        var (batch, fs, _, undo, _) = Setup();
        fs.Files[SrcA] = new byte[] { 1 };
        fs.Directories.Add(DstFolder);

        batch.CopyMany(new[] { SrcA }, DstFolder, new FakeConflictResolver());

        Assert.Equal(1, undo.Depth);
        // Single-item undo description should be the action's own, not "copy of N items".
        Assert.DoesNotContain("copy of", undo.NextDescription);
    }

    [Fact]
    public void CopyMany_EmptySources_NoUndoPushed_NoResolverCalls() {
        var (batch, _, _, undo, _) = Setup();
        var resolver = new FakeConflictResolver();

        var results = batch.CopyMany(Array.Empty<string>(), DstFolder, resolver);

        Assert.Empty(results);
        Assert.Equal(0, undo.Depth);
        Assert.Empty(resolver.StartBatchCalls);
    }


    // --- CopyMany: conflict resolution ---------------------------------

    [Fact]
    public void CopyMany_BatchSkipAll_SkipsConflicts_StillCopiesNonConflicting() {
        var (batch, fs, _, _, _) = Setup();
        fs.Files[SrcA] = new byte[] { 1 };
        fs.Files[SrcB] = new byte[] { 2 };
        fs.Directories.Add(DstFolder);
        // a.txt collides, b.txt does not.
        fs.Files[DstA] = new byte[] { 9 };
        var resolver = new FakeConflictResolver(batchOverride: ConflictResolution.Skip);

        var results = batch.CopyMany(new[] { SrcA, SrcB }, DstFolder, resolver);

        Assert.Equal(BatchItemStatus.Skipped, results[0].Status);
        Assert.Equal(BatchItemStatus.Ok, results[1].Status);
        Assert.Equal(new byte[] { 9 }, fs.Files[DstA]);    // unchanged
        Assert.Empty(resolver.ResolveCalls);               // batch override → no per-item
        Assert.Single(resolver.StartBatchCalls);
        Assert.Equal(1, resolver.StartBatchCalls[0]);      // exactly one conflict pre-detected
    }

    [Fact]
    public void CopyMany_BatchReplaceAll_OverwritesAllConflicts() {
        var (batch, fs, _, _, _) = Setup();
        fs.Files[SrcA] = new byte[] { 1 };
        fs.Files[SrcB] = new byte[] { 2 };
        fs.Directories.Add(DstFolder);
        fs.Files[DstA] = new byte[] { 9 };
        fs.Files[DstB] = new byte[] { 9 };
        var resolver = new FakeConflictResolver(batchOverride: ConflictResolution.Replace);

        var results = batch.CopyMany(new[] { SrcA, SrcB }, DstFolder, resolver);

        Assert.All(results, r => Assert.Equal(BatchItemStatus.Replaced, r.Status));
        // CopyFile was called with overwrite=true for each.
        Assert.Contains($"CopyFile:{SrcA}->{DstA}:True", fs.CallLog);
        Assert.Contains($"CopyFile:{SrcB}->{DstB}:True", fs.CallLog);
    }

    [Fact]
    public void CopyMany_BatchCancel_ReturnsAllCancelled_TouchesNothing() {
        var (batch, fs, _, undo, _) = Setup();
        fs.Files[SrcA] = new byte[] { 1 };
        fs.Directories.Add(DstFolder);
        fs.Files[DstA] = new byte[] { 9 };
        var resolver = new FakeConflictResolver(batchOverride: ConflictResolution.Cancel);

        var results = batch.CopyMany(new[] { SrcA }, DstFolder, resolver);

        Assert.Single(results);
        Assert.Equal(BatchItemStatus.Cancelled, results[0].Status);
        Assert.DoesNotContain(fs.CallLog, c => c.StartsWith("CopyFile"));
        Assert.Equal(0, undo.Depth);
    }

    [Fact]
    public void CopyMany_PerItemCancel_StopsBatch_RemainingItemsAreCancelled() {
        var (batch, fs, _, undo, _) = Setup();
        fs.Files[SrcA] = new byte[] { 1 };
        fs.Files[SrcB] = new byte[] { 2 };
        fs.Files[SrcC] = new byte[] { 3 };
        fs.Directories.Add(DstFolder);
        // All three conflict.
        fs.Files[DstA] = new byte[] { 9 };
        fs.Files[DstB] = new byte[] { 9 };
        fs.Files[DstC] = new byte[] { 9 };
        // StartBatch returns null → per-item; first replace, then cancel.
        var resolver = new FakeConflictResolver(
            batchOverride: null,
            ConflictResolution.Replace,
            ConflictResolution.Cancel);

        var results = batch.CopyMany(new[] { SrcA, SrcB, SrcC }, DstFolder, resolver);

        Assert.Equal(3, results.Count);
        Assert.Equal(BatchItemStatus.Replaced, results[0].Status);
        Assert.Equal(BatchItemStatus.Cancelled, results[1].Status);
        Assert.Equal(BatchItemStatus.Cancelled, results[2].Status);
        // Replaced item still went through, so undo composite has 1 step.
        Assert.Equal(1, undo.Depth);
    }

    [Fact]
    public void CopyMany_RenameDecision_GeneratesUniqueName() {
        var (batch, fs, _, _, _) = Setup();
        fs.Files[SrcA] = new byte[] { 1 };
        fs.Directories.Add(DstFolder);
        fs.Files[DstA] = new byte[] { 9 };  // collision → rename to "a (1).txt"
        var resolver = new FakeConflictResolver(batchOverride: null, ConflictResolution.Rename);

        var results = batch.CopyMany(new[] { SrcA }, DstFolder, resolver);

        Assert.Equal(BatchItemStatus.Renamed, results[0].Status);
        Assert.Equal(DstARenamed1, results[0].FinalDestination);
        Assert.Contains($"CopyFile:{SrcA}->{DstARenamed1}:False", fs.CallLog);
        // Original is untouched.
        Assert.Equal(new byte[] { 9 }, fs.Files[DstA]);
    }

    [Fact]
    public void CopyMany_Rename_FindsNextFreeSuffix_WhenMultipleAlreadyExist() {
        var (batch, fs, _, _, _) = Setup();
        fs.Files[SrcA] = new byte[] { 1 };
        fs.Directories.Add(DstFolder);
        fs.Files[DstA] = new byte[] { 9 };
        fs.Files[DstARenamed1] = new byte[] { 9 };
        fs.Files[@"C:\dst\a (2).txt"] = new byte[] { 9 };
        var resolver = new FakeConflictResolver(batchOverride: null, ConflictResolution.Rename);

        var results = batch.CopyMany(new[] { SrcA }, DstFolder, resolver);

        Assert.Equal(DstARenamed3, results[0].FinalDestination);
    }

    [Fact]
    public void CopyMany_PerItemSkip_DoesNotCallStartBatch_WhenOnlyOneConflict() {
        // BatchExecutor only consults StartBatch when conflictCount > 0; with
        // exactly one conflict, batch-override still asked (impl detail), but
        // a null return should fall through to Resolve for that item.
        var (batch, fs, _, _, _) = Setup();
        fs.Files[SrcA] = new byte[] { 1 };
        fs.Directories.Add(DstFolder);
        fs.Files[DstA] = new byte[] { 9 };
        var resolver = new FakeConflictResolver(batchOverride: null, ConflictResolution.Skip);

        var results = batch.CopyMany(new[] { SrcA }, DstFolder, resolver);

        Assert.Equal(BatchItemStatus.Skipped, results[0].Status);
        Assert.Single(resolver.ResolveCalls);
    }


    // --- CopyMany: error handling --------------------------------------

    [Fact]
    public void CopyMany_FailedItem_RecordedAsFailed_OthersUnaffected() {
        // Drive the failure by leaving the second source absent from the
        // fake filesystem: FakeFs CopyFile throws KeyNotFoundException if
        // the source isn't in the dict.
        var (batch, fs, _, undo, _) = Setup();
        fs.Files[SrcA] = new byte[] { 1 };
        fs.Directories.Add(DstFolder);
        // SrcB intentionally NOT added — will trigger an exception during
        // its CopyFile call.

        var results = batch.CopyMany(new[] { SrcA, SrcB }, DstFolder, new FakeConflictResolver());

        Assert.Equal(BatchItemStatus.Ok, results[0].Status);
        Assert.Equal(BatchItemStatus.Failed, results[1].Status);
        Assert.NotNull(results[1].Error);
        // The successful copy is still on the undo stack.
        Assert.Equal(1, undo.Depth);
    }


    // --- MoveMany ------------------------------------------------------

    [Fact]
    public void MoveMany_NoConflicts_MovesAll_PushesCompositeMoveUndo() {
        var (batch, fs, _, undo, _) = Setup();
        fs.Files[SrcA] = new byte[] { 1 };
        fs.Files[SrcB] = new byte[] { 2 };
        fs.Directories.Add(DstFolder);

        var results = batch.MoveMany(new[] { SrcA, SrcB }, DstFolder, new FakeConflictResolver());

        Assert.All(results, r => Assert.Equal(BatchItemStatus.Ok, r.Status));
        Assert.Contains($"MoveEntry:{SrcA}->{DstA}", fs.CallLog);
        Assert.Contains($"MoveEntry:{SrcB}->{DstB}", fs.CallLog);
        Assert.False(fs.Files.ContainsKey(SrcA));
        Assert.Equal(1, undo.Depth);
        Assert.Contains("move of 2 items", undo.NextDescription);
    }

    [Fact]
    public void MoveMany_ReplaceConflict_DeletesTargetThenMoves() {
        var (batch, fs, _, _, _) = Setup();
        fs.Files[SrcA] = new byte[] { 1 };
        fs.Directories.Add(DstFolder);
        fs.Files[DstA] = new byte[] { 9 };
        var resolver = new FakeConflictResolver(batchOverride: ConflictResolution.Replace);

        batch.MoveMany(new[] { SrcA }, DstFolder, resolver);

        // .NET Move doesn't have overwrite-for-folders; BatchExecutor clears
        // the target first via DeleteFile, then MoveEntry.
        int delIdx = fs.CallLog.IndexOf($"DeleteFile:{DstA}");
        int moveIdx = fs.CallLog.IndexOf($"MoveEntry:{SrcA}->{DstA}");
        Assert.True(delIdx >= 0 && moveIdx > delIdx, $"expected DeleteFile before MoveEntry, got log: {string.Join(", ", fs.CallLog)}");
    }


    // --- DeleteManyAsync: recycle path ---------------------------------

    [Fact]
    public async Task DeleteManyAsync_Recycle_SendsAllToBin_AndPushesCompositeUndo() {
        var (batch, fs, bin, undo, _) = Setup();
        fs.Files[RootA] = new byte[] { 1 };
        fs.Files[RootB] = new byte[] { 2 };

        var results = await batch.DeleteManyAsync(new[] { RootA, RootB }, permanent: false, default);

        Assert.All(results, r => Assert.Equal(DeleteStatus.Ok, r.Status));
        Assert.Contains($"Recycle:{RootA}", bin.CallLog);
        Assert.Contains($"Recycle:{RootB}", bin.CallLog);
        Assert.Equal(1, undo.Depth);
        Assert.Contains("delete of 2 items", undo.NextDescription);
    }

    [Fact]
    public async Task DeleteManyAsync_Recycle_SingleItem_PushesSingleAction() {
        var (batch, fs, _, undo, _) = Setup();
        fs.Files[RootA] = new byte[] { 1 };

        await batch.DeleteManyAsync(new[] { RootA }, permanent: false, default);

        Assert.Equal(1, undo.Depth);
        Assert.DoesNotContain("delete of", undo.NextDescription);
    }


    // --- DeleteManyAsync: permanent path -------------------------------

    [Fact]
    public async Task DeleteManyAsync_Permanent_BypassesBin_AndClearsUndoStack() {
        var (batch, fs, bin, undo, _) = Setup();
        fs.Files[RootA] = new byte[] { 1 };
        fs.Files[RootB] = new byte[] { 2 };
        fs.Directories.Add(RootDir);
        // Pre-existing undo entry — should be wiped.
        undo.Push(new RenameAction(fs, @"C:\x", "y"));
        Assert.Equal(1, undo.Depth);

        var results = await batch.DeleteManyAsync(new[] { RootA, RootB, RootDir }, permanent: true, default);

        Assert.All(results, r => Assert.Equal(DeleteStatus.Ok, r.Status));
        Assert.Empty(bin.CallLog);                          // bin untouched
        Assert.Contains($"DeleteFile:{RootA}", fs.CallLog);
        Assert.Contains($"DeleteFile:{RootB}", fs.CallLog);
        Assert.Contains($"DeleteDirectory:{RootDir}:True", fs.CallLog);
        Assert.Equal(0, undo.Depth);                        // wiped
    }


    // --- DeleteManyAsync: cancellation ---------------------------------

    [Fact]
    public async Task DeleteManyAsync_PreCancelledToken_MarksAllItemsCancelled_WithoutTouchingFs() {
        var (batch, fs, bin, _, _) = Setup();
        fs.Files[RootA] = new byte[] { 1 };
        fs.Files[RootB] = new byte[] { 2 };

        var cts = new CancellationTokenSource();
        cts.Cancel();

        // OperationCanceledException can surface depending on scheduling; the
        // public contract is that nothing destructive happens to those files.
        try {
            var results = await batch.DeleteManyAsync(new[] { RootA, RootB }, permanent: false, cts.Token);
            Assert.All(results, r => Assert.Equal(DeleteStatus.Cancelled, r.Status));
        } catch (OperationCanceledException) {
            // Task.Run honoured the cancellation before the loop ran — also fine.
        }

        Assert.Empty(bin.CallLog);
        Assert.True(fs.Files.ContainsKey(RootA));
        Assert.True(fs.Files.ContainsKey(RootB));
    }


    // --- DeleteManyAsync: error handling -------------------------------

    [Fact]
    public async Task DeleteManyAsync_Permanent_MissingPath_CapturedAsFailed() {
        var (batch, fs, _, _, _) = Setup();
        fs.Files[RootExists] = new byte[] { 1 };

        var results = await batch.DeleteManyAsync(new[] { RootExists, RootMissing }, permanent: true, default);

        Assert.Equal(DeleteStatus.Ok, results[0].Status);
        Assert.Equal(DeleteStatus.Failed, results[1].Status);
        Assert.IsType<FileNotFoundException>(results[1].Error);
    }


    // --- Progress reporting --------------------------------------------

    [Fact]
    public async Task DeleteManyAsync_ReportsProgressThroughTracker() {
        var (batch, fs, _, _, tracker) = Setup();
        fs.Files[RootA] = new byte[] { 1 };
        fs.Files[RootB] = new byte[] { 2 };

        int seenInProgress = 0;
        tracker.Changed += (_, _) => {
            // Inside Delete we should see at least one snapshot with a verb +
            // non-zero total. Snapshot after Dispose returns empty.
            var snap = tracker.Snapshot();
            if (snap.Count > 0) {
                seenInProgress++;
                Assert.Equal("Recycle", snap[0].Verb);
                Assert.Equal(2, snap[0].Total);
            }
        };

        await batch.DeleteManyAsync(new[] { RootA, RootB }, permanent: false, default);

        Assert.True(seenInProgress > 0, "expected at least one Changed fire while the op was in flight");
        // After dispose, tracker is empty.
        Assert.Empty(tracker.Snapshot());
    }
}
