using Wander.Core.FileSystem;
using Wander.Core.Logging;
using Wander.Core.Operations;
using Wander.Core.Shell;
using Wander.Core.Tests.Fakes;
using Wander.Core.Undo;

namespace Wander.Core.Tests;

public class ExtractionServiceTests {
    private const string Archive = @"C:\packs\nested.zip";
    private const string InnerReadme = @"C:\packs\nested.zip\readme.txt";
    private const string InnerDocs = @"C:\packs\nested.zip\docs";
    private const string InnerManual = @"C:\packs\nested.zip\docs\manual.txt";

    private const string Target = @"C:\out";
    private const string TargetReadme = @"C:\out\readme.txt";
    private const string TargetRenamed = @"C:\out\readme (1).txt";
    private const string TargetDocs = @"C:\out\docs";


    private static (ExtractionService Service, FakeShellNamespace Ns, FakeFileSystem Fs, FakeRecycleBin Bin, UndoService Undo) Setup() {
        var fs = new FakeFileSystem();
        fs.Directories.Add(Target);
        var ns = new FakeShellNamespace(fs);
        ns.AddFile(InnerReadme, "readme");
        ns.AddFile(InnerManual, "manual");

        var bin = new FakeRecycleBin(fs);
        var undo = new UndoService();
        var service = new ExtractionService(ns, fs, bin, undo, new OperationTracker(), NullLogger.Instance);

        return (service, ns, fs, bin, undo);
    }


    [Fact]
    public async Task Extract_WritesTheEntry_AndPushesUndo() {
        var (service, _, fs, _, undo) = Setup();

        var results = await service.ExtractAsync(
            new[] { InnerReadme }, Target, new FakeConflictResolver(), CancellationToken.None);

        Assert.Equal(BatchItemStatus.Ok, Assert.Single(results).Status);
        Assert.Equal(TargetReadme, results[0].FinalDestination);
        Assert.True(fs.FileExists(TargetReadme));
        Assert.Equal(1, undo.Depth);
        Assert.Contains("Extract", undo.NextDescription);
    }

    [Fact]
    public async Task Extract_Undo_SendsWhatArrivedToTheBin() {
        var (service, _, fs, bin, undo) = Setup();

        await service.ExtractAsync(new[] { InnerReadme }, Target, new FakeConflictResolver(), CancellationToken.None);
        undo.Undo();

        Assert.False(fs.FileExists(TargetReadme));
        Assert.Contains($"Recycle:{TargetReadme}", bin.CallLog);
    }

    [Fact]
    public async Task Extract_Folder_ComesOutWithItsContents() {
        var (service, _, fs, _, _) = Setup();

        var results = await service.ExtractAsync(
            new[] { InnerDocs }, Target, new FakeConflictResolver(), CancellationToken.None);

        Assert.Equal(BatchItemStatus.Ok, Assert.Single(results).Status);
        Assert.True(fs.DirectoryExists(TargetDocs));
        Assert.True(fs.FileExists(@"C:\out\docs\manual.txt"));
    }

    [Fact]
    public async Task Extract_ManyItems_LandAsOneUndoStep() {
        var (service, _, _, _, undo) = Setup();

        await service.ExtractAsync(
            new[] { InnerReadme, InnerDocs }, Target, new FakeConflictResolver(), CancellationToken.None);

        Assert.Equal(1, undo.Depth);
        Assert.Contains("extract of 2 items", undo.NextDescription);
    }


    // --- Conflicts ------------------------------------------------------

    [Fact]
    public async Task Extract_Skip_LeavesTheExistingFileAlone() {
        var (service, ns, fs, _, undo) = Setup();
        fs.Files[TargetReadme] = "already here"u8.ToArray();
        var resolver = new FakeConflictResolver(ConflictResolution.Skip);

        var results = await service.ExtractAsync(
            new[] { InnerReadme }, Target, resolver, CancellationToken.None);

        Assert.Equal(BatchItemStatus.Skipped, Assert.Single(results).Status);
        Assert.Equal("already here"u8.ToArray(), fs.Files[TargetReadme]);
        Assert.Empty(ns.CopiedOut);
        Assert.Equal(0, undo.Depth);
    }

    [Fact]
    public async Task Extract_Rename_CopiesUnderAFreeName() {
        var (service, ns, fs, _, _) = Setup();
        fs.Files[TargetReadme] = "already here"u8.ToArray();
        var resolver = new FakeConflictResolver(ConflictResolution.Rename);

        var results = await service.ExtractAsync(
            new[] { InnerReadme }, Target, resolver, CancellationToken.None);

        Assert.Equal(BatchItemStatus.Renamed, Assert.Single(results).Status);
        Assert.Equal(TargetRenamed, results[0].FinalDestination);
        Assert.Equal("readme (1).txt", Assert.Single(ns.CopiedOut).NewName);
        Assert.True(fs.FileExists(TargetRenamed));
        Assert.True(fs.FileExists(TargetReadme));
    }

    [Fact]
    public async Task Extract_Replace_RecyclesTheOldFile_AndUndoBringsItBack() {
        var (service, _, fs, bin, undo) = Setup();
        fs.Files[TargetReadme] = "already here"u8.ToArray();
        var resolver = new FakeConflictResolver(ConflictResolution.Replace);

        var results = await service.ExtractAsync(
            new[] { InnerReadme }, Target, resolver, CancellationToken.None);

        Assert.Equal(BatchItemStatus.Replaced, Assert.Single(results).Status);
        Assert.Contains($"Recycle:{TargetReadme}", bin.CallLog);
        Assert.Equal("readme"u8.ToArray(), fs.Files[TargetReadme]);

        undo.Undo();
        Assert.Equal("already here"u8.ToArray(), fs.Files[TargetReadme]);
    }

    [Fact]
    public async Task Extract_CancelledUpFront_CopiesNothing() {
        var (service, ns, fs, _, undo) = Setup();
        fs.Files[TargetReadme] = "already here"u8.ToArray();
        var resolver = new FakeConflictResolver(ConflictResolution.Cancel);

        var results = await service.ExtractAsync(
            new[] { InnerReadme, InnerDocs }, Target, resolver, CancellationToken.None);

        Assert.All(results, r => Assert.Equal(BatchItemStatus.Cancelled, r.Status));
        Assert.Empty(ns.CopiedOut);
        Assert.Equal(0, undo.Depth);
    }

    [Fact]
    public async Task Extract_AsksOnceForTheWholeBatch() {
        var (service, _, fs, _, _) = Setup();
        fs.Files[TargetReadme] = "already here"u8.ToArray();
        fs.Directories.Add(TargetDocs);
        var resolver = new FakeConflictResolver(ConflictResolution.Skip);

        await service.ExtractAsync(
            new[] { InnerReadme, InnerDocs }, Target, resolver, CancellationToken.None);

        Assert.Equal(2, Assert.Single(resolver.ResolveAllCalls));
    }

    [Fact]
    public async Task Extract_ShowsTheArchiveEntryInTheDialog() {
        var (service, _, fs, _, _) = Setup();
        fs.Files[TargetReadme] = "already here"u8.ToArray();
        var resolver = new FakeConflictResolver(batchOverride: null, ConflictResolution.Skip);

        await service.ExtractAsync(new[] { InnerReadme }, Target, resolver, CancellationToken.None);

        var conflict = Assert.Single(resolver.Conflicts);
        Assert.Equal(InnerReadme, conflict.Source.FullPath);
        Assert.Equal(TargetReadme, conflict.ExistingTarget.FullPath);
        Assert.False(conflict.IsMove);
        // Only the shell can open what is inside: no byte comparison, no merge.
        Assert.False(conflict.SourceReachable);
    }

    [Fact]
    public async Task Extract_MergeOnAFolder_MeansANewName() {
        var (service, ns, fs, _, _) = Setup();
        fs.Directories.Add(TargetDocs);
        var resolver = new FakeConflictResolver(ConflictResolution.Merge);

        var results = await service.ExtractAsync(new[] { InnerDocs }, Target, resolver, CancellationToken.None);

        Assert.Equal(BatchItemStatus.Renamed, Assert.Single(results).Status);
        Assert.Equal("docs (1)", Assert.Single(ns.CopiedOut).NewName);
    }


    // --- Refusals -------------------------------------------------------

    [Fact]
    public async Task Extract_IntoAProtectedPath_Refuses() {
        var (service, ns, _, _, _) = Setup();

        await Assert.ThrowsAsync<IOException>(() => service.ExtractAsync(
            new[] { InnerReadme }, @"C:\Windows\System32", new FakeConflictResolver(), CancellationToken.None));
        Assert.Empty(ns.CopiedOut);
    }

    [Fact]
    public async Task Extract_WhenTheShellRefuses_ReportsFailureAndPushesNoUndo() {
        var (service, ns, fs, _, undo) = Setup();
        ns.CopyOutFailure = new IOException("password-protected");

        var results = await service.ExtractAsync(
            new[] { InnerReadme }, Target, new FakeConflictResolver(), CancellationToken.None);

        Assert.Equal(BatchItemStatus.Failed, Assert.Single(results).Status);
        Assert.False(fs.FileExists(TargetReadme));
        Assert.Equal(0, undo.Depth);
    }


    // --- The temporary copy behind "open" -------------------------------

    [Fact]
    public async Task ExtractToTemp_WritesTheCopy_AndLeavesNoUndoStep() {
        var (service, _, fs, _, undo) = Setup();

        string copy = await service.ExtractToTempAsync(InnerReadme, @"C:\tmp\abcd", CancellationToken.None);

        Assert.Equal(@"C:\tmp\abcd\readme.txt", copy);
        Assert.True(fs.FileExists(copy));
        Assert.Equal(0, undo.Depth);
    }
}
