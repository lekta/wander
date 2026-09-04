using Wander.Core.FileSystem;

namespace Wander.Core.Tests;

public class ConflictBatchTests {
    private static readonly DateTime _noon = new(2026, 9, 3, 12, 0, 0, DateTimeKind.Utc);


    private static FileSystemEntry File(string name, long? size, DateTime modified, string folder = @"C:\src") {
        return new FileSystemEntry(name, System.IO.Path.Combine(folder, name), EntryKind.File, size, modified, false, false, false, false);
    }

    private static FileSystemEntry Folder(string name, string folder = @"C:\src") {
        return new FileSystemEntry(name, System.IO.Path.Combine(folder, name), EntryKind.Directory, null, _noon, false, false, false, false);
    }

    private static FileConflictInfo Pair(long sourceSize, long targetSize, bool isMove = false, DateTime? sourceDate = null, string name = "a.txt") {
        return new FileConflictInfo(
            File(name, sourceSize, sourceDate ?? _noon),
            File(name, targetSize, _noon, @"C:\dst"),
            isMove);
    }

    private static FileConflictInfo FolderPair(string name = "docs", bool reachable = true) {
        return new FileConflictInfo(Folder(name), Folder(name, @"C:\dst"), SourceReachable: reachable);
    }

    private static ConflictBatch Batch(bool skipIdentical = false, params FileConflictInfo[] conflicts) {
        return new ConflictBatch(new ConflictRequest(conflicts, ItemCount: 10), skipIdentical);
    }

    /// <summary>What a walk of C:\src\docs against C:\dst\docs found: one file colliding, one nested folder with a collision of its own, some free files.</summary>
    private static MergeScanner.Result DocsScan() {
        var inner = new MergeScanner.Node(
            new FileConflictInfo(File("deep.txt", 5, _noon, @"C:\src\docs\sub"), File("deep.txt", 5, _noon, @"C:\dst\docs\sub")),
            Array.Empty<MergeScanner.Node>(), 0);
        var sub = new MergeScanner.Node(
            new FileConflictInfo(Folder("sub", @"C:\src\docs"), Folder("sub", @"C:\dst\docs")),
            new[] { inner }, FreeFiles: 2);
        var same = new MergeScanner.Node(
            new FileConflictInfo(File("same.txt", 1, _noon, @"C:\src\docs"), File("same.txt", 2, _noon, @"C:\dst\docs")),
            Array.Empty<MergeScanner.Node>(), 0);

        return new MergeScanner.Result(new[] { same, sub }, FreeFiles: 7);
    }


    // --- Answers and decidedness ----------------------------------------

    [Fact]
    public void EmptyBatch_IsRefused() {
        Assert.Throws<ArgumentException>(() => Batch());
    }

    [Fact]
    public void NothingIsDecided_UntilEveryPairHasAnAnswer() {
        var batch = Batch(conflicts: new[] { Pair(10, 10), Pair(20, 30) });

        Assert.False(batch.AllDecided);
        Assert.Equal(0, batch.DecidedCount);
        Assert.Equal(10, batch.ItemCount);
        Assert.Throws<InvalidOperationException>(() => batch.Answers());

        batch.Choose(batch.Roots[0], ConflictResolution.Replace);
        Assert.False(batch.AllDecided);

        batch.Choose(batch.Roots[1], ConflictResolution.Rename);
        Assert.True(batch.AllDecided);
        Assert.Equal(
            new[] { ConflictResolution.Replace, ConflictResolution.Rename },
            batch.Answers().Select(a => a.Resolution));
        Assert.Equal(batch.Roots[1].Conflict, batch.Answers()[1].Conflict);
    }

    [Fact]
    public void AnAnswerCanBeTakenBack() {
        var batch = Batch(conflicts: new[] { Pair(10, 10) });
        batch.Choose(batch.Roots[0], ConflictResolution.Skip);
        batch.Choose(batch.Roots[0], null);

        Assert.Equal(0, batch.DecidedCount);
        Assert.Null(batch.Roots[0].Choice);
    }


    // --- Quick actions ---------------------------------------------------

    [Fact]
    public void QuickAction_LeavesConsideredAnswersAlone() {
        var batch = Batch(conflicts: new[] { Pair(10, 10), Pair(20, 30), Pair(40, 50) });
        batch.Choose(batch.Roots[1], ConflictResolution.Rename);

        var changed = batch.Apply(ConflictBulkAction.Replace);

        Assert.Equal(new[] { batch.Roots[0], batch.Roots[2] }, changed);
        Assert.Equal(ConflictResolution.Rename, batch.Roots[1].Choice);
        Assert.True(batch.AllDecided);
    }

    [Fact]
    public void QuickAction_None_AnswersNothing() {
        var batch = Batch(conflicts: new[] { Pair(10, 10) });

        Assert.Empty(batch.Apply(ConflictBulkAction.None));
        Assert.Null(batch.Roots[0].Choice);
    }

    [Fact]
    public void ReplaceIfNewer_TouchesOnlyThePairsWhoseSourceIsNewer() {
        var batch = Batch(conflicts: new[] {
            Pair(10, 10, sourceDate: _noon.AddDays(1)),
            Pair(10, 10, sourceDate: _noon.AddDays(-1)),
            Pair(10, 10),
        });

        var changed = batch.Apply(ConflictBulkAction.ReplaceIfSourceNewer);

        Assert.Equal(new[] { batch.Roots[0] }, changed);
        Assert.Equal(ConflictResolution.Replace, batch.Roots[0].Choice);
        Assert.Null(batch.Roots[1].Choice);
        Assert.Null(batch.Roots[2].Choice);
    }

    [Fact]
    public void ReplaceAll_OverrulesAnswersAlreadyGiven() {
        // "Заменить все" is an answer for the list, not for what is left of
        // it - the whole point of the button (PLAN, Q8).
        var batch = Batch(conflicts: new[] { Pair(10, 10), Pair(20, 30), Pair(40, 50) });
        batch.Choose(batch.Roots[1], ConflictResolution.Rename);
        batch.Choose(batch.Roots[2], ConflictResolution.Skip);

        var changed = batch.Apply(ConflictBulkAction.Replace, includeDecided: true);

        Assert.Equal(3, changed.Count);
        Assert.All(batch.Roots, p => Assert.Equal(ConflictResolution.Replace, p.Choice));
        Assert.True(batch.AllDecided);
    }

    [Fact]
    public void ReplaceAll_ReportsOnlyWhatItActuallyChanged() {
        var batch = Batch(conflicts: new[] { Pair(10, 10), Pair(20, 30) });
        batch.Choose(batch.Roots[0], ConflictResolution.Replace);

        var changed = batch.Apply(ConflictBulkAction.Replace, includeDecided: true);

        Assert.Equal(new[] { batch.Roots[1] }, changed);
    }

    [Fact]
    public void ReplaceAll_TakesBackAPolicyAnswer() {
        // "Skip identical" answered this pair; the user then said "replace
        // everything", and meant it.
        var batch = Batch(skipIdentical: true, conflicts: new[] { Pair(10, 10) });
        batch.SetCompared(batch.Roots[0], identical: true);
        Assert.Equal(ConflictResolution.Skip, batch.Roots[0].Choice);

        batch.Apply(ConflictBulkAction.Replace, includeDecided: true);

        Assert.Equal(ConflictResolution.Replace, batch.Roots[0].Choice);
        Assert.False(batch.Roots[0].FromPolicy);
    }

    [Fact]
    public void ReplaceAll_OverACollapsedMerge_AnswersTheWholeList() {
        // A folder the user had opened up and answered inside: replacing it
        // folds the children away, and what is left is decided.
        var batch = Batch(conflicts: new[] { FolderPair(), Pair(10, 10) });
        batch.Choose(batch.Roots[0], ConflictResolution.Merge);
        batch.AttachScan(batch.Roots[0], DocsScan());

        batch.Apply(ConflictBulkAction.Replace, includeDecided: true);

        Assert.Equal(ConflictResolution.Replace, batch.Roots[0].Choice);
        Assert.True(batch.AllDecided);
        Assert.Equal(2, batch.Effective().Count);
    }

    [Fact]
    public void SkipAll_AnswersEverythingWithSkip() {
        var batch = Batch(conflicts: new[] { FolderPair(), Pair(10, 10) });
        batch.Choose(batch.Roots[1], ConflictResolution.Replace);

        batch.Apply(ConflictBulkAction.Skip, includeDecided: true);

        Assert.All(batch.Roots, p => Assert.Equal(ConflictResolution.Skip, p.Choice));
        Assert.True(batch.AllDecided);
    }

    [Fact]
    public void QuickAction_WithoutTheFlag_StillLeavesAnswersAlone() {
        var batch = Batch(conflicts: new[] { Pair(10, 10), Pair(20, 30) });
        batch.Choose(batch.Roots[0], ConflictResolution.Rename);

        batch.Apply(ConflictBulkAction.Replace);

        Assert.Equal(ConflictResolution.Rename, batch.Roots[0].Choice);
    }

    [Fact]
    public void KeepBoth_IsAMerge_ForTwoFolders_AndANewName_ForAFile() {
        var batch = Batch(conflicts: new[] { FolderPair(), Pair(10, 10), FolderPair("packed", reachable: false) });

        batch.Apply(ConflictBulkAction.KeepBoth);

        Assert.Equal(ConflictResolution.Merge, batch.Roots[0].Choice);
        Assert.Equal(ConflictResolution.Rename, batch.Roots[1].Choice);
        // A folder inside an archive cannot be walked, so it cannot be merged.
        Assert.Equal(ConflictResolution.Rename, batch.Roots[2].Choice);
    }


    // --- The policy ------------------------------------------------------

    [Fact]
    public void SkipIdentical_SwitchedOn_AnswersWhatIsAlreadyCompared() {
        var batch = Batch(conflicts: new[] { Pair(10, 10), Pair(10, 10), Pair(20, 30) });
        batch.SetCompared(batch.Roots[0], true);
        batch.SetCompared(batch.Roots[1], false);

        var changed = batch.SetSkipIdentical(true);

        Assert.Equal(new[] { batch.Roots[0] }, changed);
        Assert.True(batch.SkipIdentical);
        Assert.Equal(ConflictResolution.Skip, batch.Roots[0].Choice);
        Assert.Null(batch.Roots[1].Choice);
        Assert.Null(batch.Roots[2].Choice);
    }

    [Fact]
    public void SkipIdentical_SwitchedOff_TakesBackOnlyItsOwnAnswers() {
        var batch = Batch(conflicts: new[] { Pair(10, 10), Pair(10, 10) });
        batch.SetCompared(batch.Roots[0], true);
        batch.SetCompared(batch.Roots[1], true);
        batch.SetSkipIdentical(true);
        // The user overrules one of them by hand.
        batch.Choose(batch.Roots[1], ConflictResolution.Replace);

        var changed = batch.SetSkipIdentical(false);

        Assert.Equal(new[] { batch.Roots[0] }, changed);
        Assert.Null(batch.Roots[0].Choice);
        Assert.Equal(ConflictResolution.Replace, batch.Roots[1].Choice);
    }

    [Fact]
    public void IdenticalOnAMove_IsAPlainKeep_TheSourceStays() {
        // Explorer's Skip: nothing moves, nothing is deleted.
        var batch = Batch(skipIdentical: true, Pair(10, 10, isMove: true));

        Assert.True(batch.SetCompared(batch.Roots[0], true));
        Assert.Equal(ConflictResolution.Skip, batch.Roots[0].Choice);
    }


    // --- The setting -----------------------------------------------------

    [Fact]
    public void SkipIdenticalSetting_AnswersAsComparisonsLand() {
        var batch = Batch(skipIdentical: true, Pair(10, 10), Pair(10, 10));

        Assert.Equal(0, batch.DecidedCount);

        Assert.True(batch.SetCompared(batch.Roots[0], true));
        Assert.False(batch.SetCompared(batch.Roots[1], false));

        Assert.Equal(ConflictResolution.Skip, batch.Roots[0].Choice);
        Assert.Null(batch.Roots[1].Choice);
    }

    [Fact]
    public void SkipIdenticalSetting_NeverOverridesTheUser() {
        var batch = Batch(skipIdentical: true, Pair(10, 10));
        batch.Choose(batch.Roots[0], ConflictResolution.Rename);

        Assert.False(batch.SetCompared(batch.Roots[0], true));
        Assert.Equal(ConflictResolution.Rename, batch.Roots[0].Choice);
    }

    [Fact]
    public void WithoutTheSetting_AComparisonDecidesNothing() {
        var batch = Batch(conflicts: new[] { Pair(10, 10) });

        Assert.False(batch.SetCompared(batch.Roots[0], true));
        Assert.Null(batch.Roots[0].Choice);
        Assert.True(batch.Roots[0].Verdict.Identical);
    }


    // --- What is worth reading ------------------------------------------

    [Fact]
    public void NextToCompare_SmallFilesFirst_ThenTheLargeOnes() {
        var batch = Batch(conflicts: new[] {
            Pair(5_000, 5_000),      // large, same size
            Pair(20, 30),            // sizes settle it, nothing to read
            Pair(10, 10),            // small, same size
        });

        Assert.Same(batch.Roots[2], batch.NextToCompare(1_000));
        batch.SetCompared(batch.Roots[2], false);
        Assert.Same(batch.Roots[0], batch.NextToCompare(1_000));
        batch.SetCompared(batch.Roots[0], true);
        Assert.Null(batch.NextToCompare(1_000));
    }

    [Fact]
    public void AFileThatCouldNotBeRead_IsNotOfferedAgain() {
        var batch = Batch(conflicts: new[] { Pair(10, 10) });

        Assert.False(batch.SetCompared(batch.Roots[0], null));

        Assert.Null(batch.NextToCompare(long.MaxValue));
        Assert.Null(batch.Roots[0].Verdict.Identical);
        Assert.False(batch.Roots[0].Verdict.SourceReachable);
    }


    // --- Merging a folder ------------------------------------------------

    [Fact]
    public void AMergedFolder_ListsWhatCollidesInside_NestedFoldersMergingInTurn() {
        var batch = Batch(conflicts: new[] { FolderPair(), Pair(10, 10) });
        var docs = batch.Roots[0];
        batch.Choose(docs, ConflictResolution.Merge);
        Assert.Equal(MergeScanState.NotScanned, docs.Scan);

        batch.AttachScan(docs, DocsScan());

        Assert.Equal(MergeScanState.Scanned, docs.Scan);
        Assert.Equal(7, docs.FreeFiles);
        Assert.Equal(3, docs.InnerConflicts);
        // Tree order: the folder, what is inside it, then the next root.
        var listed = batch.Effective();
        Assert.Equal(
            new[] { "docs", "same.txt", "sub", @"sub\deep.txt", "a.txt" },
            listed.Select(p => p.DisplayPath));
        Assert.Equal(new[] { 0, 1, 1, 2, 0 }, listed.Select(p => p.Depth));

        var sub = docs.Children[1];
        Assert.Equal(ConflictResolution.Merge, sub.Choice);
        Assert.Equal(MergeScanState.Scanned, sub.Scan);
        Assert.Equal(2, sub.FreeFiles);
        Assert.Null(docs.Children[0].Choice);
    }

    [Fact]
    public void OkNeedsTheInsideAnswered_AndHandsItBack() {
        var batch = Batch(conflicts: new[] { FolderPair() });
        var docs = batch.Roots[0];
        batch.Choose(docs, ConflictResolution.Merge);
        batch.AttachScan(docs, DocsScan());
        Assert.False(batch.AllDecided);
        Assert.Equal(4, batch.Effective().Count);

        batch.Apply(ConflictBulkAction.Skip);

        Assert.True(batch.AllDecided);
        Assert.Equal(
            new[] { ConflictResolution.Merge, ConflictResolution.Skip, ConflictResolution.Merge, ConflictResolution.Skip },
            batch.Answers().Select(a => a.Resolution));
        Assert.Equal(@"C:\src\docs\sub\deep.txt", batch.Answers()[3].Conflict.Source.FullPath);
    }

    [Fact]
    public void AFolderSwitchedAwayFromMerge_HidesWhatIsInside_AndKeepsIt() {
        var batch = Batch(conflicts: new[] { FolderPair() });
        var docs = batch.Roots[0];
        batch.Choose(docs, ConflictResolution.Merge);
        batch.AttachScan(docs, DocsScan());
        batch.Choose(docs.Children[0], ConflictResolution.Replace);

        batch.Choose(docs, ConflictResolution.Replace);

        Assert.Single(batch.Effective());
        Assert.True(batch.AllDecided);
        Assert.Single(batch.Answers());
        Assert.False(docs.Children[0].IsEffective);

        batch.Choose(docs, ConflictResolution.Merge);
        Assert.Equal(4, batch.Effective().Count);
        Assert.Equal(ConflictResolution.Replace, docs.Children[0].Choice);
    }

    [Fact]
    public void QuickActions_AndReading_ReachTheInside_OnlyWhileMerging() {
        var batch = Batch(conflicts: new[] { FolderPair() });
        var docs = batch.Roots[0];
        batch.Choose(docs, ConflictResolution.Merge);
        batch.AttachScan(docs, DocsScan());
        var deep = docs.Children[1].Children[0];
        Assert.Same(deep, batch.NextToCompare(long.MaxValue));

        batch.Choose(docs, ConflictResolution.Skip);

        Assert.Null(batch.NextToCompare(long.MaxValue));
        Assert.Empty(batch.Apply(ConflictBulkAction.Replace));
    }

    [Fact]
    public void AFolderThatCouldNotBeRead_KeepsItsAnswer() {
        var batch = Batch(conflicts: new[] { FolderPair() });
        var docs = batch.Roots[0];
        batch.Choose(docs, ConflictResolution.Merge);
        batch.MarkScanning(docs);
        Assert.Equal(MergeScanState.Scanning, docs.Scan);

        batch.MarkScanFailed(docs);

        Assert.Equal(MergeScanState.Failed, docs.Scan);
        Assert.Equal(ConflictResolution.Merge, docs.Choice);
        Assert.True(batch.AllDecided);
    }
}
