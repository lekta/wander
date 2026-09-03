using Wander.Core.Companions;
using Wander.Core.FileSystem;
using Wander.Core.Logging;
using Wander.Core.Operations;
using Wander.Core.Tests.Fakes;
using Wander.Core.Undo;

namespace Wander.Core.Tests;

/// <summary>
/// Batch operations over a main file plus its companions. The point of the
/// grouping is that the user is asked once and told once — everything here
/// is a variation on that.
/// </summary>
public class BatchGroupTests {
    private const string SrcFolder = @"C:\src";
    private const string DstFolder = @"C:\dst";
    private const string SrcPng = @"C:\src\Sprite.png";
    private const string SrcMeta = @"C:\src\Sprite.png.meta";
    private const string DstPng = @"C:\dst\Sprite.png";
    private const string DstMeta = @"C:\dst\Sprite.png.meta";


    private static (BatchExecutor Batch, FakeFileSystem Fs, FakeRecycleBin Bin, UndoService Undo) Setup(
        bool targetHasPng = false, bool targetHasMeta = false) {

        var fs = new FakeFileSystem();
        var bin = new FakeRecycleBin(fs);
        var undo = new UndoService();
        var batch = new BatchExecutor(fs, bin, undo, new OperationTracker(), NullLogger.Instance);

        fs.Directories.Add(SrcFolder);
        fs.Directories.Add(DstFolder);
        fs.Files[SrcPng] = new byte[] { 1 };
        fs.Files[SrcMeta] = new byte[] { 2 };
        if (targetHasPng) {
            fs.Files[DstPng] = new byte[] { 9 };
        }
        if (targetHasMeta) {
            fs.Files[DstMeta] = new byte[] { 9 };
        }

        return (batch, fs, bin, undo);
    }

    private static IReadOnlyList<BatchGroup> SpriteGroup() {
        return new[] { new BatchGroup(SrcPng, new[] { SrcMeta }) };
    }


    // --- Every member is its own collision ------------------------------

    [Fact]
    public void CopyMany_AsksAboutEachCollidingMember_InOneCall() {
        // A sidecar's content changes independently of its main file's, so
        // the two are two questions - asked together, answered apart.
        var (batch, _, _, _) = Setup(targetHasPng: true, targetHasMeta: true);
        var resolver = new FakeConflictResolver(batchOverride: ConflictResolution.Replace);

        batch.CopyMany(SpriteGroup(), DstFolder, resolver);

        Assert.Equal(2, Assert.Single(resolver.ResolveAllCalls));
        Assert.Equal(new[] { SrcPng, SrcMeta }, resolver.Conflicts.Select(c => c.Source.FullPath));
    }

    [Fact]
    public void CopyMany_AsksOnlyAboutTheCompanion_WhenOnlyItCollides() {
        var (batch, fs, _, _) = Setup(targetHasMeta: true);
        var resolver = new FakeConflictResolver(perItem: ConflictResolution.Skip);

        var results = batch.CopyMany(SpriteGroup(), DstFolder, resolver);

        Assert.Equal(SrcMeta, Assert.Single(resolver.Conflicts).Source.FullPath);
        // The main file had a free name and simply went; the group reads by it.
        Assert.Equal(BatchItemStatus.Ok, Assert.Single(results).Status);
        Assert.True(fs.Files.ContainsKey(DstPng));
        Assert.Equal(new byte[] { 9 }, fs.Files[DstMeta]);
    }

    [Fact]
    public void CopyMany_MixedAnswers_LandMemberByMember() {
        // png already there -> keep; meta changed -> replace. The case the
        // per-file rows exist for.
        var (batch, fs, bin, _) = Setup(targetHasPng: true, targetHasMeta: true);
        var resolver = new FakeConflictResolver(batchOverride: null, ConflictResolution.Skip, ConflictResolution.Replace);

        batch.CopyMany(SpriteGroup(), DstFolder, resolver);

        Assert.Equal(new byte[] { 9 }, fs.Files[DstPng]);
        Assert.Equal(new byte[] { 2 }, fs.Files[DstMeta]);
        Assert.Equal($"Recycle:{DstMeta}", Assert.Single(bin.CallLog, c => c.StartsWith("Recycle:")));
    }

    [Fact]
    public void CopyMany_ReportsOneResultPerGroup() {
        var (batch, _, _, _) = Setup();
        var resolver = new FakeConflictResolver();

        var results = batch.CopyMany(SpriteGroup(), DstFolder, resolver);

        var only = Assert.Single(results);
        Assert.Equal(SrcPng, only.Source);
        Assert.Equal(BatchItemStatus.Ok, only.Status);
    }


    // --- What each answer does to the whole group ------------------------

    [Fact]
    public void CopyMany_CopiesEveryMember() {
        var (batch, fs, _, _) = Setup();

        batch.CopyMany(SpriteGroup(), DstFolder, new FakeConflictResolver());

        Assert.True(fs.Files.ContainsKey(DstPng));
        Assert.True(fs.Files.ContainsKey(DstMeta));
    }

    [Fact]
    public void CopyMany_Skip_SkipsOnlyTheMemberAsked() {
        // The sidecar has nothing in its way: it is not held back by the
        // main file's answer.
        var (batch, fs, _, _) = Setup(targetHasPng: true);
        var resolver = new FakeConflictResolver(perItem: ConflictResolution.Skip);

        var results = batch.CopyMany(SpriteGroup(), DstFolder, resolver);

        Assert.Equal(BatchItemStatus.Skipped, Assert.Single(results).Status);
        Assert.Equal(new byte[] { 9 }, fs.Files[DstPng]);
        Assert.True(fs.Files.ContainsKey(DstMeta));
    }

    [Fact]
    public void CopyMany_Replace_RecyclesEveryCollidingMember() {
        var (batch, fs, bin, _) = Setup(targetHasPng: true, targetHasMeta: true);
        var resolver = new FakeConflictResolver(batchOverride: ConflictResolution.Replace);

        batch.CopyMany(SpriteGroup(), DstFolder, resolver);

        Assert.Equal(2, bin.CallLog.Count(c => c.StartsWith("Recycle:")));
        Assert.Equal(new byte[] { 1 }, fs.Files[DstPng]);
        Assert.Equal(new byte[] { 2 }, fs.Files[DstMeta]);
    }

    [Fact]
    public void CopyMany_Rename_KeepsTheCompanionAttachedToTheNewName() {
        // The whole feature would be pointless if an auto-rename split the
        // pair: Sprite (1).png next to Sprite.png.meta (1) is two orphans.
        var (batch, fs, _, _) = Setup(targetHasPng: true);
        var resolver = new FakeConflictResolver(perItem: ConflictResolution.Rename);

        var results = batch.CopyMany(SpriteGroup(), DstFolder, resolver);

        Assert.Equal(@"C:\dst\Sprite (1).png", Assert.Single(results).FinalDestination);
        Assert.True(fs.Files.ContainsKey(@"C:\dst\Sprite (1).png"));
        Assert.True(fs.Files.ContainsKey(@"C:\dst\Sprite (1).png.meta"));
    }

    [Fact]
    public void CopyMany_RenamedMainFile_TakesACollidingCompanionAlong_WhateverItAnswered() {
        // Both collide; png -> keep both, meta -> replace. The meta belongs
        // to the png that just became Sprite (1).png, so it goes there, and
        // the meta at the old name - the old png's - is left alone.
        var (batch, fs, bin, _) = Setup(targetHasPng: true, targetHasMeta: true);
        var resolver = new FakeConflictResolver(batchOverride: null, ConflictResolution.Rename, ConflictResolution.Replace);

        batch.CopyMany(SpriteGroup(), DstFolder, resolver);

        Assert.Equal(new byte[] { 2 }, fs.Files[@"C:\dst\Sprite (1).png.meta"]);
        Assert.Equal(new byte[] { 9 }, fs.Files[DstMeta]);
        Assert.Empty(bin.CallLog);
    }

    [Fact]
    public void CopyMany_RenamedMainFile_LeavesASkippedCompanionBehind() {
        var (batch, fs, _, _) = Setup(targetHasPng: true, targetHasMeta: true);
        var resolver = new FakeConflictResolver(batchOverride: null, ConflictResolution.Rename, ConflictResolution.Skip);

        batch.CopyMany(SpriteGroup(), DstFolder, resolver);

        Assert.True(fs.Files.ContainsKey(@"C:\dst\Sprite (1).png"));
        Assert.False(fs.Files.ContainsKey(@"C:\dst\Sprite (1).png.meta"));
    }

    [Fact]
    public void CopyMany_Rename_HandlesTheStemSharingShape() {
        // IMG.CR2 + IMG.xmp: the sidecar shares the stem, not the full name.
        var (batch, fs, _, _) = Setup();
        fs.Files[@"C:\src\IMG.CR2"] = new byte[] { 1 };
        fs.Files[@"C:\src\IMG.xmp"] = new byte[] { 2 };
        fs.Files[@"C:\dst\IMG.CR2"] = new byte[] { 9 };
        var resolver = new FakeConflictResolver(perItem: ConflictResolution.Rename);

        batch.CopyMany(new[] { new BatchGroup(@"C:\src\IMG.CR2", new[] { @"C:\src\IMG.xmp" }) }, DstFolder, resolver);

        Assert.True(fs.Files.ContainsKey(@"C:\dst\IMG (1).CR2"));
        Assert.True(fs.Files.ContainsKey(@"C:\dst\IMG (1).xmp"));
    }

    [Fact]
    public void MoveMany_MovesEveryMember_AsOneUndoStep() {
        var (batch, fs, _, undo) = Setup();

        batch.MoveMany(SpriteGroup(), DstFolder, new FakeConflictResolver());
        Assert.Equal(1, undo.Depth);

        undo.Undo();
        Assert.True(fs.Files.ContainsKey(SrcPng));
        Assert.True(fs.Files.ContainsKey(SrcMeta));
    }


    // --- Regrouping a flat list ------------------------------------------

    [Fact]
    public void Group_PutsACompanionWithItsMainFile() {
        var groups = CompanionResolver.Default.Group(new[] { SrcPng, SrcMeta });

        var only = Assert.Single(groups);
        Assert.Equal(SrcPng, only.Primary);
        Assert.Equal(SrcMeta, Assert.Single(only.Companions));
    }

    [Fact]
    public void Group_LeavesAnOrphanCompanionOnItsOwn() {
        var groups = CompanionResolver.Default.Group(new[] { SrcMeta });

        Assert.Equal(SrcMeta, Assert.Single(groups).Primary);
    }

    [Fact]
    public void Group_DoesNotPairAcrossFolders() {
        // Same names, different folders — not a pair.
        var groups = CompanionResolver.Default.Group(new[] { SrcPng, @"C:\other\Sprite.png.meta" });

        Assert.Equal(2, groups.Count);
        Assert.All(groups, g => Assert.Empty(g.Companions));
    }

    [Fact]
    public void Group_PreservesTheOrderOfTheMainFiles() {
        var groups = CompanionResolver.Default.Group(new[] { @"C:\src\z.txt", SrcMeta, SrcPng });

        Assert.Equal(new[] { @"C:\src\z.txt", SrcPng }, groups.Select(g => g.Primary));
    }

    [Fact]
    public void Group_HandlesTheStemSharingShape() {
        var groups = CompanionResolver.Default.Group(new[] { @"C:\src\IMG.CR2", @"C:\src\IMG.xmp" });

        var only = Assert.Single(groups);
        Assert.Equal(@"C:\src\IMG.CR2", only.Primary);
        Assert.Equal(@"C:\src\IMG.xmp", Assert.Single(only.Companions));
    }
}
