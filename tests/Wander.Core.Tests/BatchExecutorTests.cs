using Wander.Core.FileSystem;
using Wander.Core.Logging;
using Wander.Core.Operations;
using Wander.Core.Tests.Fakes;
using Wander.Core.Undo;

namespace Wander.Core.Tests;

public class BatchExecutorTests {
    // --- Paths reused across cases ------------------------------------
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


    /// <summary>
    /// <see cref="Setup"/> with the held-path machinery in reach: the claims
    /// table, a probe that is told what is held, and a wait that records its
    /// pauses instead of sleeping them.
    /// </summary>
    private static (BatchExecutor Batch, FakeFileSystem Fs, FakeRecycleBin Bin, UndoService Undo, PathClaims Claims,
        FakeBusyProbe Probe, List<TimeSpan> Pauses) SetupHeld() {
        var fs = new FakeFileSystem();
        var bin = new FakeRecycleBin(fs);
        var undo = new UndoService();
        var claims = new PathClaims();
        var probe = new FakeBusyProbe();
        var pauses = new List<TimeSpan>();
        var held = new HeldPaths(claims, probe, newWait: () => new BusyWait(pauses.Add));
        var batch = new BatchExecutor(fs, bin, undo, new OperationTracker(TimeSpan.Zero), NullLogger.Instance, held);

        return (batch, fs, bin, undo, claims, probe, pauses);
    }

    private static (BatchExecutor Batch, FakeFileSystem Fs, FakeRecycleBin Bin, UndoService Undo, OperationTracker Tracker) Setup() {
        var fs = new FakeFileSystem();
        var bin = new FakeRecycleBin(fs);
        var undo = new UndoService();
        // No throttle: a test wants to see every report, and the fake copies
        // a batch faster than the real tracker's window.
        var tracker = new OperationTracker(TimeSpan.Zero);
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
        Assert.Empty(resolver.ResolveAllCalls);
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
        Assert.Empty(resolver.ResolveAllCalls);
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
        Assert.Equal(1, Assert.Single(resolver.ResolveAllCalls));   // exactly one conflict pre-detected
    }

    [Fact]
    public void CopyMany_BatchReplaceAll_RecyclesTargetsThenCopies() {
        var (batch, fs, bin, _, _) = Setup();
        fs.Files[SrcA] = new byte[] { 1 };
        fs.Files[SrcB] = new byte[] { 2 };
        fs.Directories.Add(DstFolder);
        fs.Files[DstA] = new byte[] { 9 };
        fs.Files[DstB] = new byte[] { 9 };
        var resolver = new FakeConflictResolver(batchOverride: ConflictResolution.Replace);

        var results = batch.CopyMany(new[] { SrcA, SrcB }, DstFolder, resolver);

        Assert.All(results, r => Assert.Equal(BatchItemStatus.Replaced, r.Status));
        // Replaced targets go to the recycle bin (undoable), then a plain
        // no-overwrite copy lands in their place.
        Assert.Contains($"Recycle:{DstA}", bin.CallLog);
        Assert.Contains($"Recycle:{DstB}", bin.CallLog);
        Assert.Contains($"CopyFile:{SrcA}->{DstA}:False", fs.CallLog);
        Assert.Contains($"CopyFile:{SrcB}->{DstB}:False", fs.CallLog);
        Assert.Equal(new byte[] { 1 }, fs.Files[DstA]);
        Assert.Equal(new byte[] { 2 }, fs.Files[DstB]);
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
        Assert.DoesNotContain(fs.CallLog, c => c.StartsWith("CopyFile", StringComparison.Ordinal));
        Assert.Equal(0, undo.Depth);
    }

    [Fact]
    public void CopyMany_CancelAmongTheAnswers_AppliesNothing() {
        var (batch, fs, _, undo, _) = Setup();
        fs.Files[SrcA] = new byte[] { 1 };
        fs.Files[SrcB] = new byte[] { 2 };
        fs.Files[SrcC] = new byte[] { 3 };
        fs.Directories.Add(DstFolder);
        // a and c conflict; b would copy freely.
        fs.Files[DstA] = new byte[] { 9 };
        fs.Files[DstC] = new byte[] { 9 };
        var resolver = new FakeConflictResolver(
            batchOverride: null,
            ConflictResolution.Replace,
            ConflictResolution.Cancel);

        var results = batch.CopyMany(new[] { SrcA, SrcB, SrcC }, DstFolder, resolver);

        // Every answer is collected before anything moves, so a Cancel
        // anywhere in the list is a Cancel of the whole batch: the item
        // answered Replace is not replaced, the free one is not copied.
        Assert.Equal(3, results.Count);
        Assert.All(results, r => Assert.Equal(BatchItemStatus.Cancelled, r.Status));
        Assert.DoesNotContain(fs.CallLog, c => c.StartsWith("CopyFile", StringComparison.Ordinal));
        Assert.Equal(new byte[] { 9 }, fs.Files[DstA]);
        Assert.Equal(0, undo.Depth);
    }

    [Fact]
    public void CopyMany_ResolverBackingOut_CancelsEverything() {
        var (batch, fs, _, undo, _) = Setup();
        fs.Files[SrcA] = new byte[] { 1 };
        fs.Files[SrcB] = new byte[] { 2 };
        fs.Directories.Add(DstFolder);
        fs.Files[DstA] = new byte[] { 9 };
        var resolver = new ScriptedResolver(_ => null);

        var results = batch.CopyMany(new[] { SrcA, SrcB }, DstFolder, resolver);

        Assert.All(results, r => Assert.Equal(BatchItemStatus.Cancelled, r.Status));
        Assert.DoesNotContain(fs.CallLog, c => c.StartsWith("CopyFile", StringComparison.Ordinal));
        Assert.Equal(0, undo.Depth);
    }

    [Fact]
    public void CopyMany_TargetThatLandedMidBatch_IsAskedAboutOnItsOwn() {
        var (batch, fs, _, _, _) = Setup();
        fs.Files[SrcA] = new byte[] { 1 };
        fs.Files[SrcB] = new byte[] { 2 };
        fs.Directories.Add(DstFolder);
        fs.Files[DstA] = new byte[] { 9 };
        // While the user answers about a.txt, somebody drops a b.txt into
        // the target folder: the up-front list did not have it.
        var resolver = new ScriptedResolver(request => {
            fs.Files[DstB] = new byte[] { 9 };
            return request.Conflicts.Select(c => new ConflictAnswer(c, ConflictResolution.Skip)).ToList();
        });

        var results = batch.CopyMany(new[] { SrcA, SrcB }, DstFolder, resolver);

        Assert.Equal(new[] { 1, 1 }, resolver.Calls);
        Assert.All(results, r => Assert.Equal(BatchItemStatus.Skipped, r.Status));
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
    public void CopyMany_AsksAboutTheCollidingItemsOnly_AndInOrder() {
        var (batch, fs, _, _, _) = Setup();
        fs.Files[SrcA] = new byte[] { 1 };
        fs.Files[SrcB] = new byte[] { 2 };
        fs.Files[SrcC] = new byte[] { 3 };
        fs.Directories.Add(DstFolder);
        fs.Files[DstA] = new byte[] { 9 };
        fs.Files[DstC] = new byte[] { 9 };
        var resolver = new FakeConflictResolver(batchOverride: null, ConflictResolution.Skip, ConflictResolution.Replace);

        var results = batch.CopyMany(new[] { SrcA, SrcB, SrcC }, DstFolder, resolver);

        // One call, the two collisions in batch order; each answer lands on
        // the item it was given for, with the free item untouched between.
        Assert.Equal(2, Assert.Single(resolver.ResolveAllCalls));
        Assert.Equal(new[] { SrcA, SrcC }, resolver.Conflicts.Select(c => c.Source.FullPath));
        Assert.Equal(new[] { DstA, DstC }, resolver.Conflicts.Select(c => c.ExistingTarget.FullPath));
        Assert.Equal(BatchItemStatus.Skipped, results[0].Status);
        Assert.Equal(BatchItemStatus.Ok, results[1].Status);
        Assert.Equal(BatchItemStatus.Replaced, results[2].Status);
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
    public void MoveMany_ReplaceConflict_RecyclesTargetThenMoves() {
        var (batch, fs, bin, _, _) = Setup();
        fs.Files[SrcA] = new byte[] { 1 };
        fs.Directories.Add(DstFolder);
        fs.Files[DstA] = new byte[] { 9 };
        var resolver = new FakeConflictResolver(batchOverride: ConflictResolution.Replace);

        batch.MoveMany(new[] { SrcA }, DstFolder, resolver);

        // The replaced target must go through the recycle bin (undoable),
        // never a permanent delete.
        Assert.Contains($"Recycle:{DstA}", bin.CallLog);
        Assert.DoesNotContain($"DeleteFile:{DstA}", fs.CallLog);
        Assert.Contains($"MoveEntry:{SrcA}->{DstA}", fs.CallLog);
        Assert.Equal(new byte[] { 1 }, fs.Files[DstA]);
        Assert.False(fs.Files.ContainsKey(SrcA));
    }

    [Fact]
    public void MoveMany_TellsTheDialogItIsAMove() {
        var (batch, fs, _, _, _) = Setup();
        fs.Files[SrcA] = new byte[] { 1 };
        fs.Directories.Add(DstFolder);
        fs.Files[DstA] = new byte[] { 9 };
        var resolver = new FakeConflictResolver(batchOverride: ConflictResolution.Skip);

        batch.MoveMany(new[] { SrcA }, DstFolder, resolver);

        Assert.True(Assert.Single(resolver.Conflicts).IsMove);
    }

    [Fact]
    public void MoveMany_ReplaceConflict_UndoRestoresBothSides() {
        var (batch, fs, _, undo, _) = Setup();
        fs.Files[SrcA] = new byte[] { 1 };
        fs.Directories.Add(DstFolder);
        fs.Files[DstA] = new byte[] { 9 };
        var resolver = new FakeConflictResolver(batchOverride: ConflictResolution.Replace);

        batch.MoveMany(new[] { SrcA }, DstFolder, resolver);
        var undone = undo.Undo();

        // Undo runs the composite in reverse: move a.txt back to src, then
        // restore the replaced target from the bin — full original state.
        Assert.NotNull(undone);
        Assert.Equal(new byte[] { 1 }, fs.Files[SrcA]);
        Assert.Equal(new byte[] { 9 }, fs.Files[DstA]);
    }


    // --- Into the folder it is already in --------------------------------

    [Fact]
    public void CopyMany_IntoItsOwnFolder_MakesACopy_WithoutAsking() {
        var (batch, fs, _, _, _) = Setup();
        fs.Directories.Add(@"C:\src");
        fs.Files[SrcA] = new byte[] { 1 };
        var resolver = new FakeConflictResolver();

        var results = batch.CopyMany(new[] { SrcA }, @"C:\src", resolver);

        Assert.Empty(resolver.ResolveAllCalls);
        Assert.Equal(BatchItemStatus.Renamed, results[0].Status);
        Assert.Equal(new byte[] { 1 }, fs.Files[@"C:\src\a (1).txt"]);
        Assert.Equal(new byte[] { 1 }, fs.Files[SrcA]);
    }

    [Fact]
    public void CopyMany_IntoItsOwnFolder_TakesCompanionsAlong() {
        var (batch, fs, _, _, _) = Setup();
        fs.Directories.Add(@"C:\src");
        fs.Files[SrcA] = new byte[] { 1 };
        fs.Files[@"C:\src\a.txt.meta"] = new byte[] { 2 };
        var group = new BatchGroup(SrcA, new[] { @"C:\src\a.txt.meta" });

        batch.CopyMany(new[] { group }, @"C:\src", new FakeConflictResolver());

        Assert.Equal(new byte[] { 2 }, fs.Files[@"C:\src\a (1).txt.meta"]);
    }

    [Fact]
    public void MoveMany_IntoItsOwnFolder_DoesNothing_AndAsksNothing() {
        var (batch, fs, bin, _, _) = Setup();
        fs.Directories.Add(@"C:\src");
        fs.Files[SrcA] = new byte[] { 1 };
        var resolver = new FakeConflictResolver();

        var results = batch.MoveMany(new[] { SrcA }, @"C:\src", resolver);

        Assert.Empty(resolver.ResolveAllCalls);
        Assert.Equal(BatchItemStatus.Skipped, results[0].Status);
        Assert.Equal(new byte[] { 1 }, fs.Files[SrcA]);
        Assert.Empty(bin.CallLog);
    }


    // --- Merging two folders ---------------------------------------------

    [Fact]
    public void CopyMany_Merge_CombinesTheTwoFolders_AndAsksAboutWhatCollidesInside() {
        var (batch, fs, bin, _, _) = Setup();
        fs.Directories.Add(@"C:\src\docs");
        fs.Directories.Add(@"C:\dst\docs");
        fs.Files[@"C:\src\docs\same.txt"] = new byte[] { 1 };
        fs.Files[@"C:\src\docs\free.txt"] = new byte[] { 2 };
        fs.Files[@"C:\dst\docs\same.txt"] = new byte[] { 9 };
        fs.Files[@"C:\dst\docs\theirs.txt"] = new byte[] { 8 };
        // The folder pair first; the fake does not walk folders, so the
        // collision inside is asked about on its own once the merge
        // reaches it.
        var resolver = new FakeConflictResolver(batchOverride: null, ConflictResolution.Merge, ConflictResolution.Replace);

        var results = batch.CopyMany(new[] { @"C:\src\docs" }, DstFolder, resolver);

        Assert.Equal(BatchItemStatus.Merged, Assert.Single(results).Status);
        Assert.Equal(new[] { 1, 1 }, resolver.ResolveAllCalls);
        Assert.Equal(@"C:\src\docs\same.txt", resolver.Conflicts[1].Source.FullPath);
        Assert.Equal(new byte[] { 1 }, fs.Files[@"C:\dst\docs\same.txt"]);
        Assert.Equal(new byte[] { 2 }, fs.Files[@"C:\dst\docs\free.txt"]);
        Assert.Equal(new byte[] { 8 }, fs.Files[@"C:\dst\docs\theirs.txt"]);
        Assert.Equal(@"Recycle:C:\dst\docs\same.txt", Assert.Single(bin.CallLog, c => c.StartsWith("Recycle:", StringComparison.Ordinal)));
    }

    [Fact]
    public void CopyMany_Merge_UsesTheAnswersTheWindowGaveForTheInside() {
        // A resolver that walked the folder answers the inner pair up
        // front, by path; the batch does not ask again.
        var (batch, fs, _, _, _) = Setup();
        fs.Directories.Add(@"C:\src\docs");
        fs.Directories.Add(@"C:\dst\docs");
        fs.Files[@"C:\src\docs\same.txt"] = new byte[] { 1 };
        fs.Files[@"C:\src\docs\free.txt"] = new byte[] { 2 };
        fs.Files[@"C:\dst\docs\same.txt"] = new byte[] { 9 };
        var resolver = new ScriptedResolver(request => {
            var inner = new FileConflictInfo(fs.GetEntry(@"C:\src\docs\same.txt")!, fs.GetEntry(@"C:\dst\docs\same.txt")!);
            return new[] {
                new ConflictAnswer(request.Conflicts[0], ConflictResolution.Merge),
                new ConflictAnswer(inner, ConflictResolution.Skip),
            };
        });

        batch.CopyMany(new[] { @"C:\src\docs" }, DstFolder, resolver);

        Assert.Single(resolver.Calls);
        Assert.Equal(new byte[] { 9 }, fs.Files[@"C:\dst\docs\same.txt"]);
        Assert.Equal(new byte[] { 2 }, fs.Files[@"C:\dst\docs\free.txt"]);
    }

    [Fact]
    public void CopyMany_Merge_NestedFolders_MergeInTurn() {
        var (batch, fs, _, _, _) = Setup();
        fs.Directories.Add(@"C:\src\docs");
        fs.Directories.Add(@"C:\src\docs\sub");
        fs.Directories.Add(@"C:\dst\docs");
        fs.Directories.Add(@"C:\dst\docs\sub");
        fs.Files[@"C:\src\docs\sub\deep.txt"] = new byte[] { 3 };
        fs.Files[@"C:\dst\docs\sub\old.txt"] = new byte[] { 7 };
        var resolver = new FakeConflictResolver(batchOverride: ConflictResolution.Merge);

        batch.CopyMany(new[] { @"C:\src\docs" }, DstFolder, resolver);

        Assert.Equal(new byte[] { 3 }, fs.Files[@"C:\dst\docs\sub\deep.txt"]);
        Assert.Equal(new byte[] { 7 }, fs.Files[@"C:\dst\docs\sub\old.txt"]);
        Assert.Equal(new[] { 1, 1 }, resolver.ResolveAllCalls);
    }

    [Fact]
    public void MoveMany_Merge_EmptiesTheSourceFolder_AndUndoBringsItAllBack() {
        var (batch, fs, bin, undo, _) = Setup();
        fs.Directories.Add(@"C:\src\docs");
        fs.Directories.Add(@"C:\dst\docs");
        fs.Files[@"C:\src\docs\free.txt"] = new byte[] { 2 };
        var resolver = new FakeConflictResolver(batchOverride: ConflictResolution.Merge);

        var results = batch.MoveMany(new[] { @"C:\src\docs" }, DstFolder, resolver);

        Assert.Equal(BatchItemStatus.Merged, results[0].Status);
        Assert.Equal(new byte[] { 2 }, fs.Files[@"C:\dst\docs\free.txt"]);
        Assert.False(fs.Files.ContainsKey(@"C:\src\docs\free.txt"));
        // The emptied shell goes to the bin, not into oblivion.
        Assert.Contains(@"Recycle:C:\src\docs", bin.CallLog);
        Assert.DoesNotContain(@"C:\src\docs", fs.Directories);

        undo.Undo();
        Assert.Contains(@"C:\src\docs", fs.Directories);
        Assert.Equal(new byte[] { 2 }, fs.Files[@"C:\src\docs\free.txt"]);
    }

    [Fact]
    public void MoveMany_Merge_KeepsTheSourceFolder_WhenSomethingStaysBehind() {
        var (batch, fs, bin, _, _) = Setup();
        fs.Directories.Add(@"C:\src\docs");
        fs.Directories.Add(@"C:\dst\docs");
        fs.Files[@"C:\src\docs\same.txt"] = new byte[] { 1 };
        fs.Files[@"C:\dst\docs\same.txt"] = new byte[] { 9 };
        var resolver = new FakeConflictResolver(batchOverride: null, ConflictResolution.Merge, ConflictResolution.Skip);

        batch.MoveMany(new[] { @"C:\src\docs" }, DstFolder, resolver);

        Assert.True(fs.Files.ContainsKey(@"C:\src\docs\same.txt"));
        Assert.Contains(@"C:\src\docs", fs.Directories);
        Assert.Empty(bin.CallLog);
    }

    [Fact]
    public void CopyMany_Merge_OnAFile_MeansKeepBoth() {
        var (batch, fs, _, _, _) = Setup();
        fs.Files[SrcA] = new byte[] { 1 };
        fs.Directories.Add(DstFolder);
        fs.Files[DstA] = new byte[] { 9 };

        var results = batch.CopyMany(new[] { SrcA }, DstFolder, new FakeConflictResolver(batchOverride: ConflictResolution.Merge));

        Assert.Equal(BatchItemStatus.Renamed, results[0].Status);
        Assert.Equal(DstARenamed1, results[0].FinalDestination);
        Assert.Equal(new byte[] { 9 }, fs.Files[DstA]);
    }


    // --- System-path guard ----------------------------------------------

    [Fact]
    public async Task DeleteManyAsync_ProtectedSystemPath_FailsWithoutTouchingAnything() {
        var (batch, fs, bin, undo, _) = Setup();
        string protectedPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows), "notepad.exe");
        fs.Files[RootA] = new byte[] { 1 };

        var results = await batch.DeleteManyAsync(new[] { protectedPath, RootA }, permanent: false, CancellationToken.None);

        Assert.Equal(DeleteStatus.Failed, results[0].Status);
        Assert.IsType<IOException>(results[0].Error);
        Assert.DoesNotContain($"Recycle:{protectedPath}", bin.CallLog);
        // The unprotected item in the same batch still goes through.
        Assert.Equal(DeleteStatus.Ok, results[1].Status);
        Assert.Equal(1, undo.Depth);
    }

    [Fact]
    public async Task DeleteManyAsync_Recycle_KeepsTheOrderAsked_WhateverHappenedToEachItem() {
        // The bin takes the batch as a whole and the guard takes items out of
        // it beforehand; the results still line up with what was asked.
        var (batch, fs, _, undo, _) = Setup();
        string protectedPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows), "notepad.exe");
        string missing = Path.Combine(DstFolder, "never-was.txt");
        fs.Files[RootA] = new byte[] { 1 };
        fs.Files[RootB] = new byte[] { 2 };

        var results = await batch.DeleteManyAsync(
            new[] { RootA, missing, protectedPath, RootB }, permanent: false, CancellationToken.None);

        Assert.Equal(new[] { RootA, missing, protectedPath, RootB }, results.Select(r => r.Path));
        Assert.Equal(
            new[] { DeleteStatus.Ok, DeleteStatus.Failed, DeleteStatus.Failed, DeleteStatus.Ok },
            results.Select(r => r.Status));
        Assert.IsType<FileNotFoundException>(results[1].Error);
        Assert.Equal("delete of 2 items", undo.NextDescription);
    }

    [Fact]
    public void MoveMany_ProtectedSource_FailsThatItem() {
        var (batch, fs, _, _, _) = Setup();
        string protectedSrc = Environment.GetFolderPath(Environment.SpecialFolder.System);
        fs.Directories.Add(protectedSrc);
        fs.Directories.Add(DstFolder);

        var results = batch.MoveMany(new[] { protectedSrc }, DstFolder, new FakeConflictResolver());

        Assert.Equal(BatchItemStatus.Failed, results[0].Status);
        Assert.IsType<IOException>(results[0].Error);
        Assert.DoesNotContain(fs.CallLog, c => c.StartsWith("MoveEntry", StringComparison.Ordinal));
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
                Assert.Equal(OperationVerbs.Recycle, snap[0].Verb);
                Assert.Equal(2, snap[0].Total);
            }
        };

        await batch.DeleteManyAsync(new[] { RootA, RootB }, permanent: false, default);

        Assert.True(seenInProgress > 0, "expected at least one Changed fire while the op was in flight");
        // After dispose, tracker is empty.
        Assert.Empty(tracker.Snapshot());
    }


    // --- Bytes ---------------------------------------------------------

    [Fact]
    public async Task CopyManyAsync_WeighsTheSourcesUpFront_AndCountsBytesAsTheyGo() {
        var (batch, fs, _, _, tracker) = Setup();
        fs.Files[SrcA] = new byte[100];
        fs.Files[SrcB] = new byte[60];
        fs.Directories.Add(DstFolder);

        long biggestSeen = 0;
        long totalSeen = 0;
        tracker.Changed += (_, _) => {
            foreach (var snap in tracker.Snapshot()) {
                biggestSeen = Math.Max(biggestSeen, snap.BytesDone);
                totalSeen = Math.Max(totalSeen, snap.BytesTotal);
            }
        };

        await batch.CopyManyAsync(new[] { SrcA, SrcB }, DstFolder, new FakeConflictResolver(), default);

        Assert.Equal(160, totalSeen);
        Assert.Equal(160, biggestSeen);
    }

    [Fact]
    public async Task CopyManyAsync_AsksAboutCollisions_BeforeWeighingTheSources() {
        // The walk of a large source takes seconds; with the walk first the
        // operation stood there empty for all of them, and a Cancel in the
        // question made every one of them wasted.
        var (batch, fs, _, _, tracker) = Setup();
        fs.Files[SrcA] = new byte[100];
        fs.Directories.Add(DstFolder);
        fs.Files[Path.Combine(DstFolder, Path.GetFileName(SrcA))] = new byte[7];

        long totalWhenAsked = -1;
        long totalSeen = 0;
        tracker.Changed += (_, _) => {
            foreach (var snap in tracker.Snapshot()) {
                totalSeen = Math.Max(totalSeen, snap.BytesTotal);
            }
        };
        var resolver = new ScriptedResolver(request => {
            totalWhenAsked = tracker.Snapshot()[0].BytesTotal;

            return request.Conflicts.Select(c => new ConflictAnswer(c, ConflictResolution.Replace)).ToList();
        });

        await batch.CopyManyAsync(new[] { SrcA }, DstFolder, resolver, default);

        Assert.Equal(0, totalWhenAsked);
        Assert.Equal(100, totalSeen);
    }

    // --- Held paths (PLAN block 0, steps 4-5) ---------------------------

    [Fact]
    public async Task MoveManyAsync_AHeldFile_IsWaitedFor_ThenMoved_AndTheWaitReported() {
        var (batch, fs, _, _, _, probe, pauses) = SetupHeld();
        fs.Files[SrcA] = new byte[] { 1 };
        fs.Directories.Add(DstFolder);
        probe.HeldFor[SrcA] = 2;

        var results = await batch.MoveManyAsync(new[] { SrcA }, DstFolder, new FakeConflictResolver(), default);

        var result = Assert.Single(results);
        Assert.Equal(BatchItemStatus.Ok, result.Status);
        Assert.True(fs.FileExists(DstA));
        Assert.Equal(new BusyReport(SrcA, null, 2 * BusyWait.DefaultStep, Released: true), result.Busy);
        Assert.Equal(2, pauses.Count);
    }

    [Fact]
    public async Task MoveManyAsync_FilesNeverLetGo_CostTheBudgetOnce_AndFailInUse() {
        var (batch, fs, _, _, _, probe, pauses) = SetupHeld();
        fs.Files[SrcA] = new byte[] { 1 };
        fs.Files[SrcB] = new byte[] { 2 };
        fs.Directories.Add(DstFolder);
        probe.HeldFor[SrcA] = int.MaxValue;
        probe.HeldFor[SrcB] = int.MaxValue;

        var results = await batch.MoveManyAsync(new[] { SrcA, SrcB }, DstFolder, new FakeConflictResolver(), default);

        Assert.All(results, r => Assert.Equal(BatchItemStatus.Failed, r.Status));
        Assert.All(results, r => Assert.True(FileInUse.Is(r.Error)));
        Assert.True(fs.FileExists(SrcA));
        Assert.True(fs.FileExists(SrcB));
        Assert.Equal(BusyWait.DefaultBudget, pauses.Aggregate(TimeSpan.Zero, (a, b) => a + b));
    }

    [Fact]
    public async Task MoveManyAsync_AHeldFolder_IsTriedAgain_WhileTheMoveAnswersInUse() {
        var (batch, fs, _, _, _, _, pauses) = SetupHeld();
        fs.Directories.Add(RootDir);
        fs.Directories.Add(DstFolder);
        fs.MoveInUseFor[RootDir] = 1;

        var results = await batch.MoveManyAsync(new[] { RootDir }, DstFolder, new FakeConflictResolver(), default);

        Assert.Equal(BatchItemStatus.Ok, Assert.Single(results).Status);
        Assert.True(fs.DirectoryExists(DstFolder + @"\dir"));
        Assert.Single(pauses);
        Assert.True(results[0].Busy?.Released);
    }

    [Fact]
    public async Task MoveManyAsync_AHeldFolder_OnAnotherVolume_IsNotTriedAgain() {
        // Across volumes the move is a copy and then a delete: the delete
        // that met the held file has taken the rest, and a second go would
        // copy onto the copy - or, cancelled in the wait, have Ctrl+Z bin
        // the only whole copy as a "partial" one.
        var (batch, fs, _, undo, _, _, pauses) = SetupHeld();
        fs.Directories.Add(RootDir);
        fs.Directories.Add(@"D:\dst");
        fs.MoveInUseFor[RootDir] = 1;

        var results = await batch.MoveManyAsync(new[] { RootDir }, @"D:\dst", new FakeConflictResolver(), default);

        Assert.Equal(BatchItemStatus.Failed, Assert.Single(results).Status);
        Assert.True(FileInUse.Is(results[0].Error));
        Assert.Empty(pauses);
        Assert.False(undo.CanUndo);
    }

    [Fact]
    public async Task MoveManyAsync_EveryMemberOfAGroupIsWaitedFor_NotOnlyTheFirstHeldOne() {
        // The group reports its first wait; the wait itself is made for every
        // member - for a folder it is the move, and skipping it left the
        // folder behind under an "Ok".
        var (batch, fs, _, _, _, probe, _) = SetupHeld();
        fs.Files[SrcA] = new byte[] { 1 };
        fs.Directories.Add(RootDir);
        fs.Directories.Add(DstFolder);
        probe.HeldFor[SrcA] = 1;
        fs.MoveInUseFor[RootDir] = 1;

        var results = await batch.MoveManyAsync(
            new[] { new BatchGroup(SrcA, new[] { RootDir }) }, DstFolder, new FakeConflictResolver(), default);

        Assert.Equal(BatchItemStatus.Ok, Assert.Single(results).Status);
        Assert.True(fs.FileExists(DstA));
        Assert.False(fs.DirectoryExists(RootDir));
        Assert.True(fs.DirectoryExists(DstFolder + @"\dir"));
        Assert.Equal(SrcA, results[0].Busy?.Path);
    }

    [Fact]
    public async Task MoveManyAsync_ASourceAnotherOperationIsWorkingOn_IsLeftAlone_AndNamed() {
        var (batch, fs, _, _, claims, _, _) = SetupHeld();
        fs.Files[SrcA] = new byte[] { 1 };
        fs.Files[SrcB] = new byte[] { 2 };
        fs.Directories.Add(DstFolder);
        using var copy = claims.Claim(new[] { SrcA }, ClaimKind.UserOperation, OperationVerbs.Copy);

        var results = await batch.MoveManyAsync(new[] { SrcA, SrcB }, DstFolder, new FakeConflictResolver(), default);

        Assert.Equal(BatchItemStatus.Failed, results[0].Status);
        Assert.Equal(OperationVerbs.Copy, Assert.IsType<ClaimedByOperationException>(results[0].Error).Verb);
        Assert.True(fs.FileExists(SrcA));
        Assert.Equal(BatchItemStatus.Ok, results[1].Status);
    }

    [Fact]
    public async Task CopyManyAsync_ClaimsSourcesAndTargets_ForAsLongAsItRuns() {
        var (batch, fs, _, _, claims, _, _) = SetupHeld();
        fs.Files[SrcA] = new byte[] { 1 };
        fs.Directories.Add(DstFolder);
        bool claimedWhileAsked = false;
        var resolver = new ScriptedResolver(request => {
            claimedWhileAsked = claims.IsClaimed(SrcA, ClaimKind.UserOperation) && claims.IsClaimed(DstA, ClaimKind.UserOperation);

            return request.Conflicts.Select(c => new ConflictAnswer(c, ConflictResolution.Replace)).ToList();
        });
        fs.Files[DstA] = new byte[] { 9 };

        await batch.CopyManyAsync(new[] { SrcA }, DstFolder, resolver, default);

        Assert.True(claimedWhileAsked);
        Assert.Equal(0, claims.Count);
    }

    [Fact]
    public async Task DeleteManyAsync_WhatTheBinFindsHeld_IsSentAgain_UntilLetGo() {
        var (batch, fs, bin, undo, _, _, pauses) = SetupHeld();
        fs.Files[RootA] = new byte[] { 1 };
        fs.Files[RootB] = new byte[] { 2 };
        bin.InUseFor[RootB] = 2;

        var results = await batch.DeleteManyAsync(new[] { RootA, RootB }, permanent: false, default);

        Assert.All(results, r => Assert.Equal(DeleteStatus.Ok, r.Status));
        Assert.Null(results[0].Busy);
        Assert.Equal(new BusyReport(RootB, null, 2 * BusyWait.DefaultStep, Released: true), results[1].Busy);
        Assert.Equal(new[] { $"Recycle:{RootA}", $"Recycle:{RootB}", $"Recycle:{RootB}", $"Recycle:{RootB}" }, bin.CallLog);
        Assert.Equal(2, pauses.Count);
        Assert.Equal("delete of 2 items", undo.NextDescription);
    }

    [Fact]
    public async Task DeleteManyAsync_HeldForGood_FailsInUse_AfterTheBudget() {
        var (batch, fs, bin, _, _, _, pauses) = SetupHeld();
        fs.Files[RootA] = new byte[] { 1 };
        bin.InUseFor[RootA] = int.MaxValue;

        var result = Assert.Single(await batch.DeleteManyAsync(new[] { RootA }, permanent: false, default));

        Assert.Equal(DeleteStatus.Failed, result.Status);
        Assert.True(FileInUse.Is(result.Error));
        Assert.False(result.Busy?.Released);
        Assert.Equal(BusyWait.DefaultBudget, pauses.Aggregate(TimeSpan.Zero, (a, b) => a + b));
    }

    [Fact]
    public async Task DeleteManyAsync_CountsAHeldItemOnce_HoweverOftenItIsSent() {
        var fs = new FakeFileSystem();
        var bin = new FakeRecycleBin(fs);
        var tracker = new OperationTracker(TimeSpan.Zero);
        var held = new HeldPaths(new PathClaims(), newWait: () => new BusyWait(_ => { }));
        var batch = new BatchExecutor(fs, bin, new UndoService(), tracker, NullLogger.Instance, held);
        fs.Files[RootA] = new byte[] { 1 };
        bin.InUseFor[RootA] = 3;
        int seen = 0;
        tracker.Changed += (_, _) => {
            foreach (var snap in tracker.Snapshot()) {
                seen = Math.Max(seen, snap.Completed);
            }
        };

        await batch.DeleteManyAsync(new[] { RootA }, permanent: false, default);

        Assert.Equal(1, seen);
    }

    [Fact]
    public async Task DeleteManyAsync_AFolderAnotherOperationIsWorkingInside_NeverReachesTheBin() {
        var (batch, fs, bin, _, claims, _, _) = SetupHeld();
        fs.Directories.Add(RootDir);
        fs.Files[RootDir + @"\inner.bin"] = new byte[] { 1 };
        using var copy = claims.Claim(new[] { RootDir + @"\inner.bin" }, ClaimKind.UserOperation, OperationVerbs.Copy);

        var result = Assert.Single(await batch.DeleteManyAsync(new[] { RootDir }, permanent: false, default));

        Assert.Equal(DeleteStatus.Failed, result.Status);
        Assert.IsType<ClaimedByOperationException>(result.Error);
        Assert.True(fs.DirectoryExists(RootDir));
        Assert.Empty(bin.CallLog);
    }

    [Fact]
    public async Task CopyManyAsync_SaysItIsWeighing_WhileItWalksTheSources_AndNotOnceBytesMove() {
        // "Counting" instead of a bar standing at zero: the walk of a large
        // folder takes seconds before a single byte moves.
        var tracker = new OperationTracker(TimeSpan.Zero);
        var fs = new ListingProbe(() => tracker.Snapshot().Single().IsWeighing);
        var batch = new BatchExecutor(fs, new FakeRecycleBin(fs), new UndoService(), tracker, NullLogger.Instance);
        fs.Directories.Add(RootDir);
        fs.Files[RootDir + @"\inner.bin"] = new byte[50];
        fs.Files[SrcA] = new byte[100];
        fs.Directories.Add(DstFolder);

        var whileMoving = new List<bool>();
        tracker.Changed += (_, _) => {
            foreach (var snap in tracker.Snapshot().Where(s => s.BytesDone > 0)) {
                whileMoving.Add(snap.IsWeighing);
            }
        };

        await batch.CopyManyAsync(new[] { RootDir, SrcA }, DstFolder, new FakeConflictResolver(), default);

        Assert.NotEmpty(fs.SeenWhileListing);
        Assert.All(fs.SeenWhileListing, Assert.True);
        Assert.NotEmpty(whileMoving);
        Assert.All(whileMoving, Assert.False);
    }

    [Fact]
    public async Task CopyManyAsync_ReportsBytes_WhileOneFileIsStillGoing() {
        // The point of the whole exercise: a single large file has to move
        // the bar, not sit at zero until it lands.
        var (batch, fs, _, _, tracker) = Setup();
        fs.Files[SrcA] = new byte[1000];
        fs.CopyChunk = 100;
        fs.Directories.Add(DstFolder);

        var partials = new List<long>();
        tracker.Changed += (_, _) => {
            var snap = tracker.Snapshot();
            if (snap.Count > 0 && snap[0].Completed == 0 && snap[0].BytesDone > 0) {
                partials.Add(snap[0].BytesDone);
            }
        };

        await batch.CopyManyAsync(new[] { SrcA }, DstFolder, new FakeConflictResolver(), default);

        // Part-way readings, not one jump from nothing to everything.
        Assert.Contains(partials, b => b > 0 && b < 1000);
        Assert.True(partials.Count > 2, $"expected several readings inside the file, got {partials.Count}");
    }

    [Fact]
    public async Task CopyManyAsync_SettlesTheCounter_WhenTheCopyReportsNothing() {
        // A folder: the fake copies it in one call and reports no bytes at
        // all. The counter still has to end where the estimate said.
        var (batch, fs, _, _, tracker) = Setup();
        fs.Directories.Add(RootDir);
        fs.Files[RootDir + @"\inner.bin"] = new byte[512];
        fs.Directories.Add(DstFolder);

        long lastDone = 0;
        tracker.Changed += (_, _) => {
            var snap = tracker.Snapshot();
            if (snap.Count > 0) {
                lastDone = Math.Max(lastDone, snap[0].BytesDone);
            }
        };

        await batch.CopyManyAsync(new[] { RootDir }, DstFolder, new FakeConflictResolver(), default);

        Assert.Equal(512, lastDone);
    }

    [Fact]
    public async Task MoveManyAsync_CountsBytes_ForARenameThatMovesNone() {
        var (batch, fs, _, _, tracker) = Setup();
        fs.Files[SrcA] = new byte[250];
        fs.Directories.Add(DstFolder);

        long lastDone = 0;
        tracker.Changed += (_, _) => {
            var snap = tracker.Snapshot();
            if (snap.Count > 0) {
                lastDone = Math.Max(lastDone, snap[0].BytesDone);
            }
        };

        await batch.MoveManyAsync(new[] { SrcA }, DstFolder, new FakeConflictResolver(), default);

        Assert.Equal(250, lastDone);
    }

    [Fact]
    public async Task DeleteManyAsync_HasNoBytes_AndCountsItems() {
        var (batch, fs, _, _, tracker) = Setup();
        fs.Files[RootA] = new byte[10];
        fs.Files[RootB] = new byte[10];

        bool sawBytes = false;
        tracker.Changed += (_, _) => {
            foreach (var snap in tracker.Snapshot()) {
                sawBytes |= snap.HasBytes;
            }
        };

        await batch.DeleteManyAsync(new[] { RootA, RootB }, permanent: false, default);

        Assert.False(sawBytes);
    }

    [Fact]
    public async Task CopyManyAsync_CancelledInsideAFile_IsCancelled_NotFailed_AndUndoable() {
        // Cancel used to be possible only between items; now it lands in the
        // middle of one. The partial copy is a cancellation, not an error,
        // and Ctrl+Z has to be able to clear it away.
        var (batch, fs, bin, undo, tracker) = Setup();
        fs.Files[SrcA] = new byte[1000];
        fs.Files[SrcB] = new byte[1000];
        fs.CopyChunk = 100;
        fs.Directories.Add(DstFolder);

        using var cts = new CancellationTokenSource();
        tracker.Changed += (_, _) => {
            if (tracker.Snapshot() is [{ BytesDone: > 0 }]) {
                cts.Cancel();
            }
        };

        var results = await batch.CopyManyAsync(
            new[] { SrcA, SrcB }, DstFolder, new FakeConflictResolver(), cts.Token);

        Assert.All(results, r => Assert.Equal(BatchItemStatus.Cancelled, r.Status));
        Assert.All(results, r => Assert.Null(r.Error));

        // What landed part-way is on the undo stack, so Ctrl+Z clears it.
        Assert.Equal(1, undo.Depth);
        undo.Undo();
        Assert.Contains($"Recycle:{DstA}", bin.CallLog);
        Assert.False(fs.FileExists(DstA));
    }

    [Fact]
    public async Task CopyManyAsync_NamesTheCurrentFile_BeforeItIsFinished() {
        var (batch, fs, _, _, tracker) = Setup();
        fs.Files[SrcA] = new byte[100];
        fs.CopyChunk = 10;
        fs.Directories.Add(DstFolder);

        var namedWhileUnfinished = new List<string>();
        tracker.Changed += (_, _) => {
            var snap = tracker.Snapshot();
            if (snap.Count > 0 && snap[0].Completed == 0 && snap[0].CurrentPath is { } path) {
                namedWhileUnfinished.Add(path);
            }
        };

        await batch.CopyManyAsync(new[] { SrcA }, DstFolder, new FakeConflictResolver(), default);

        Assert.Contains(SrcA, namedWhileUnfinished);
    }


    /// <summary>
    /// Asks <c>probe</c> every time a folder is listed - which, in a copy
    /// with nothing to merge, only the weighing does.
    /// </summary>
    private sealed class ListingProbe : FakeFileSystem {
        private readonly Func<bool> _probe;


        public ListingProbe(Func<bool> probe) {
            _probe = probe;
        }


        public List<bool> SeenWhileListing { get; } = new();


        public override IReadOnlyList<FileSystemEntry> Enumerate(string path, SortOptions? sort = null, CancellationToken ct = default) {
            SeenWhileListing.Add(_probe());

            return base.Enumerate(path, sort, ct);
        }
    }

    /// <summary>
    /// A resolver whose answer is a function of what it was shown - for the
    /// cases the fake's fixed script cannot express.
    /// </summary>
    private sealed class ScriptedResolver : IConflictResolver {
        private readonly Func<ConflictRequest, IReadOnlyList<ConflictAnswer>?> _answer;


        public ScriptedResolver(Func<ConflictRequest, IReadOnlyList<ConflictAnswer>?> answer) {
            _answer = answer;
        }


        /// <summary>How many conflicts each call brought.</summary>
        public List<int> Calls { get; } = new();


        public IReadOnlyList<ConflictAnswer>? ResolveAll(ConflictRequest request) {
            Calls.Add(request.Conflicts.Count);

            return _answer(request);
        }
    }
}
