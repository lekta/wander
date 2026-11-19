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
    private const string InnerHuge = @"C:\packs\nested.zip\huge.bin";

    private const string Target = @"C:\out";
    private const string TargetReadme = @"C:\out\readme.txt";
    private const string TargetHuge = @"C:\out\huge.bin";
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
    public async Task IntoNewFolder_MakesAFolderNamedAfterTheArchive_AndUndoRecyclesItWhole() {
        var (service, _, fs, bin, undo) = Setup();

        var (folder, results) = await service.ExtractIntoNewFolderAsync(
            Archive, new[] { InnerReadme, InnerDocs }, Target, CancellationToken.None);

        Assert.Equal(@"C:\out\nested", folder);
        Assert.All(results, r => Assert.Equal(BatchItemStatus.Ok, r.Status));
        Assert.True(fs.FileExists(@"C:\out\nested\readme.txt"));
        Assert.True(fs.FileExists(@"C:\out\nested\docs\manual.txt"));
        Assert.Equal(1, undo.Depth);

        undo.Undo();

        Assert.Equal(new[] { @"Recycle:C:\out\nested" }, bin.CallLog);
    }

    [Fact]
    public async Task IntoNewFolder_ATakenName_GetsANumber() {
        var (service, _, fs, _, _) = Setup();
        fs.Files[@"C:\out\nested"] = Array.Empty<byte>();

        var (folder, _) = await service.ExtractIntoNewFolderAsync(
            Archive, new[] { InnerReadme }, Target, CancellationToken.None);

        Assert.Equal(@"C:\out\nested (1)", folder);
        Assert.True(fs.FileExists(@"C:\out\nested (1)\readme.txt"));
    }

    [Fact]
    public async Task IntoNewFolder_NothingCameOut_RemovesTheEmptyFolder_AndPushesNoUndo() {
        var (service, ns, fs, _, undo) = Setup();
        ns.CopyOutFailure = new IOException("locked");

        var (folder, results) = await service.ExtractIntoNewFolderAsync(
            Archive, new[] { InnerReadme }, Target, CancellationToken.None);

        Assert.Null(folder);
        Assert.Equal(BatchItemStatus.Failed, Assert.Single(results).Status);
        Assert.Contains(@"DeleteDirectory:C:\out\nested:False", fs.CallLog);
        Assert.Equal(0, undo.Depth);
    }

    [Fact]
    public async Task Extract_ClaimsTheEntriesAndWhereTheyLand_WhileItRuns() {
        var fs = new FakeFileSystem();
        fs.Directories.Add(Target);
        var ns = new FakeShellNamespace(fs);
        ns.AddFile(InnerReadme, "readme");
        var claims = new PathClaims();
        var service = new ExtractionService(
            ns, fs, new FakeRecycleBin(fs), new UndoService(), new OperationTracker(), NullLogger.Instance, claims);
        bool claimedDuring = false;
        claims.Changed += (_, _) => claimedDuring |=
            claims.IsClaimed(InnerReadme, ClaimKind.UserOperation) && claims.IsClaimed(TargetReadme, ClaimKind.UserOperation);

        await service.ExtractAsync(new[] { InnerReadme }, Target, new FakeConflictResolver(), CancellationToken.None);

        Assert.True(claimedDuring);
        Assert.Equal(0, claims.Count);
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
    public async Task Extract_TwoCollisionsInOneFolder_ListTheArchiveOnce() {
        var (service, ns, fs, _, _) = Setup();
        ns.AddFile(InnerHuge, "huge");
        fs.Files[TargetReadme] = "already here"u8.ToArray();
        fs.Files[TargetHuge] = "already here"u8.ToArray();

        await service.ExtractAsync(
            new[] { InnerReadme, InnerHuge }, Target, new FakeConflictResolver(ConflictResolution.Skip), CancellationToken.None);

        Assert.Equal(1, ns.EnumerateCalls);
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

    /// <summary>
    /// Q7: same size on both sides is the one case where reading the bytes
    /// decides anything, so the entry is unpacked into scratch space first
    /// and the window is told where to read it.
    /// </summary>
    [Fact]
    public async Task Extract_SameSizePair_IsUnpackedSoItsBytesCanBeCompared() {
        var (service, _, fs, _, _) = Setup();
        fs.Files[TargetReadme] = "readme"u8.ToArray();
        var resolver = new FakeConflictResolver(ConflictResolution.Skip);

        await service.ExtractAsync(new[] { InnerReadme }, Target, resolver, CancellationToken.None);

        var conflict = Assert.Single(resolver.Conflicts);
        Assert.True(conflict.SourceReachable);
        Assert.NotNull(conflict.ReadablePath);
        Assert.NotEqual(InnerReadme, conflict.SourceReadPath);
        Assert.True(fs.FileExists(conflict.SourceReadPath));
        // And what the window would do with it: the two turn out identical,
        // which is what "do not ask about identical files" acts on.
        Assert.True(FileContentComparer.AreIdentical(fs, conflict.SourceReadPath, TargetReadme));
    }

    [Fact]
    public async Task Extract_SameSizeButDifferentBytes_ComparesAsDifferent() {
        var (service, _, fs, _, _) = Setup();
        fs.Files[TargetReadme] = "README"u8.ToArray();
        var resolver = new FakeConflictResolver(ConflictResolution.Skip);

        await service.ExtractAsync(new[] { InnerReadme }, Target, resolver, CancellationToken.None);

        var conflict = Assert.Single(resolver.Conflicts);
        Assert.False(FileContentComparer.AreIdentical(fs, conflict.SourceReadPath, TargetReadme));
    }

    [Fact]
    public async Task Extract_PairOverTheCompareLimit_StaysUnreadable() {
        var (service, ns, fs, _, _) = Setup();
        long tooBig = FileContentComparer.AutoCompareLimit + 1;
        ns.AddFile(InnerHuge, "huge", size: tooBig);
        fs.Files[TargetHuge] = new byte[tooBig];
        var resolver = new FakeConflictResolver(ConflictResolution.Skip);

        await service.ExtractAsync(new[] { InnerHuge }, Target, resolver, CancellationToken.None);

        var conflict = Assert.Single(resolver.Conflicts);
        Assert.False(conflict.SourceReachable);
        Assert.Null(conflict.ReadablePath);
        Assert.Equal(InnerHuge, conflict.SourceReadPath);
    }

    /// <summary>
    /// A folder inside an archive is never unpacked for a question: merging
    /// one means walking it, and only the shell can. It keeps "replace,
    /// keep, or a new name".
    /// </summary>
    [Fact]
    public async Task Extract_FolderPair_IsNotUnpacked() {
        var (service, _, fs, _, _) = Setup();
        fs.Directories.Add(TargetDocs);
        var resolver = new FakeConflictResolver(ConflictResolution.Skip);

        await service.ExtractAsync(new[] { InnerDocs }, Target, resolver, CancellationToken.None);

        Assert.False(Assert.Single(resolver.Conflicts).SourceReachable);
    }

    /// <summary>"Извлечь рядом": nobody is asked, nothing is replaced - a file and a folder already there both stay, the newcomers get "(1)".</summary>
    [Fact]
    public async Task Extract_WithAFixedKeepBoth_AsksNobody_AndReplacesNothing() {
        var (service, ns, fs, _, _) = Setup();
        fs.Files[TargetReadme] = "already here"u8.ToArray();
        fs.Directories.Add(TargetDocs);

        var results = await service.ExtractAsync(
            new[] { InnerReadme, InnerDocs }, Target, new FixedConflictResolver(ConflictResolution.Rename), CancellationToken.None);

        Assert.All(results, r => Assert.Equal(BatchItemStatus.Renamed, r.Status));
        Assert.Equal(new[] { "readme (1).txt", "docs (1)" }, ns.CopiedOut.Select(c => c.NewName));
        Assert.Equal("already here"u8.ToArray(), fs.Files[TargetReadme]);
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

    /// <summary>
    /// "Extract here" on a flash drive: the archive sits in its root, and
    /// the root takes the extraction. The guard is about taking a root
    /// away, not about adding to it.
    /// </summary>
    [Fact]
    public async Task Extract_IntoADriveRoot_Works() {
        var (service, _, fs, _, _) = Setup();
        fs.Directories.Add(@"E:\");

        var results = await service.ExtractAsync(
            new[] { InnerReadme }, @"E:\", new FakeConflictResolver(), CancellationToken.None);

        Assert.Equal(BatchItemStatus.Ok, Assert.Single(results).Status);
        Assert.True(fs.FileExists(@"E:\readme.txt"));
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

    /// <summary>
    /// The window says "password" only when the shell named no cause, and it
    /// can tell only by the item's error: that is where the kind of
    /// failure has to arrive, not flattened into a message.
    /// </summary>
    [Fact]
    public async Task Extract_WhenTheShellStopsWithoutACause_TheItemCarriesItAsLocked() {
        var (service, ns, _, _, _) = Setup();
        ns.CopyOutFailure = new ArchiveLockedException();

        var results = await service.ExtractAsync(
            new[] { InnerReadme }, Target, new FakeConflictResolver(), CancellationToken.None);

        var result = Assert.Single(results);
        Assert.Equal(BatchItemStatus.Failed, result.Status);
        Assert.IsType<ArchiveLockedException>(result.Error);
    }


    // --- Progress ----------------------------------------------------------

    [Fact]
    public async Task Extract_FromAZip_CountsItsFilesAndBytes() {
        var fs = new FakeFileSystem();
        fs.Directories.Add(Target);
        fs.Files[Archive] = Zip(("readme.txt", "readme"), ("docs/manual.txt", "manual!!"));
        var ns = new FakeShellNamespace(fs);
        ns.AddFile(InnerReadme, "readme");
        ns.AddFile(InnerManual, "manual!!");
        var tracker = new OperationTracker();
        var service = new ExtractionService(ns, fs, new FakeRecycleBin(fs), new UndoService(), tracker, NullLogger.Instance);
        var seen = new List<OperationSnapshot>();
        ns.AfterReport = _ => seen.Add(Assert.Single(tracker.Snapshot()));

        await service.ExtractAsync(new[] { InnerDocs, InnerReadme }, Target, new FakeConflictResolver(), CancellationToken.None);

        // manual.txt, then the docs folder, then readme.txt.
        Assert.Equal(3, seen.Count);
        Assert.All(seen, s => Assert.Equal((2, 14L, false), (s.Total, s.BytesTotal, s.BytesAreWork)));
        Assert.Equal((1, 8L), (seen[0].Completed, seen[0].BytesDone));
        Assert.Equal((1, 8L), (seen[1].Completed, seen[1].BytesDone));
        Assert.Equal((2, 14L), (seen[2].Completed, seen[2].BytesDone));
    }

    [Fact]
    public async Task Extract_WithoutAZipDirectory_CountsOnlyTheSelectedEntries() {
        // The fake archive has no bytes on disk: no directory to read, as
        // with a .rar or a zip that will not open.
        var (_, ns, fs, _, _) = Setup();
        var tracker = new OperationTracker();
        var service = new ExtractionService(ns, fs, new FakeRecycleBin(fs), new UndoService(), tracker, NullLogger.Instance);
        var seen = new List<OperationSnapshot>();
        ns.AfterReport = _ => seen.Add(Assert.Single(tracker.Snapshot()));

        await service.ExtractAsync(new[] { InnerDocs }, Target, new FakeConflictResolver(), CancellationToken.None);

        Assert.Equal(2, seen.Count);
        Assert.Equal((0, 1, InnerManual), (seen[0].Completed, seen[0].Total, seen[0].CurrentPath));
        Assert.Equal((1, 1), (seen[1].Completed, seen[1].Total));
        Assert.True(seen[0].BytesAreWork);
    }


    // --- The temporary copy behind "open" -------------------------------

    [Fact]
    public async Task ExtractToTemp_WritesTheCopy_AndLeavesNoUndoStep() {
        var (service, _, fs, _, undo) = Setup();

        var copy = await service.ExtractToTempAsync(InnerReadme, @"C:\tmp\abcd", CancellationToken.None);

        Assert.Equal(@"C:\tmp\abcd\readme.txt", copy.Path);
        Assert.True(fs.FileExists(copy.Path));
        Assert.Equal(0, undo.Depth);
    }

    [Fact]
    public async Task ExtractToTemp_ACopyEditedSinceItWasMade_GoesToTheBin_AndAFreshOneTakesItsPlace() {
        // notes.txt opened from a zip, edited and saved in the editor, then
        // opened again: the edit is the user's, not scratch (audit 2026-11-18, F4).
        var (service, _, fs, bin, _) = Setup();
        var first = await service.ExtractToTempAsync(InnerReadme, @"C:\tmp\edit", CancellationToken.None);
        fs.Files[first.Path] = "readme, edited"u8.ToArray();

        var again = await service.ExtractToTempAsync(InnerReadme, @"C:\tmp\edit", CancellationToken.None);

        Assert.Equal(first.Path, again.Path);
        Assert.True(again.EditedCopyBinned);
        Assert.Contains($"Recycle:{first.Path}", bin.CallLog);
        Assert.Equal("readme", System.Text.Encoding.UTF8.GetString(fs.Files[again.Path]));
    }

    [Fact]
    public async Task ExtractToTemp_ACopyAsItWasMade_IsSimplyMadeAgain() {
        var (service, _, fs, bin, _) = Setup();
        await service.ExtractToTempAsync(InnerReadme, @"C:\tmp\same", CancellationToken.None);

        var again = await service.ExtractToTempAsync(InnerReadme, @"C:\tmp\same", CancellationToken.None);

        Assert.False(again.EditedCopyBinned);
        Assert.Empty(bin.CallLog);
        Assert.True(fs.FileExists(again.Path));
    }

    [Fact]
    public async Task ExtractToTemp_ACopyFromAnEarlierSession_IsReplacedAsBefore() {
        var (service, _, fs, bin, _) = Setup();
        fs.Directories.Add(@"C:\tmp\old");
        fs.Files[@"C:\tmp\old\readme.txt"] = "from yesterday"u8.ToArray();

        var copy = await service.ExtractToTempAsync(InnerReadme, @"C:\tmp\old", CancellationToken.None);

        Assert.False(copy.EditedCopyBinned);
        Assert.Empty(bin.CallLog);
        Assert.Equal("readme", System.Text.Encoding.UTF8.GetString(fs.Files[copy.Path]));
    }

    [Fact]
    public async Task ExtractToTemp_AnEditedCopyTheBinRefuses_StaysAndTheExtractionFails() {
        var (service, _, fs, bin, _) = Setup();
        var first = await service.ExtractToTempAsync(InnerReadme, @"C:\tmp\kept", CancellationToken.None);
        fs.Files[first.Path] = "readme, edited"u8.ToArray();
        bin.Unavailable.Add(first.Path);

        await Assert.ThrowsAsync<RecycleUnavailableException>(
            () => service.ExtractToTempAsync(InnerReadme, @"C:\tmp\kept", CancellationToken.None));

        Assert.Equal("readme, edited", System.Text.Encoding.UTF8.GetString(fs.Files[first.Path]));
    }


    private static byte[] Zip(params (string Name, string Content)[] entries) {
        using var buffer = new MemoryStream();
        using (var zip = new System.IO.Compression.ZipArchive(buffer, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true)) {
            foreach (var (name, content) in entries) {
                using var writer = new StreamWriter(zip.CreateEntry(name).Open());
                writer.Write(content);
            }
        }

        return buffer.ToArray();
    }
}
