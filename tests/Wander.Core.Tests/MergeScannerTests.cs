using Wander.Core.FileSystem;
using Wander.Core.Tests.Fakes;

namespace Wander.Core.Tests;

public class MergeScannerTests {
    private const string Src = @"C:\src\docs";
    private const string Dst = @"C:\dst\docs";


    private static FakeFileSystem Folders() {
        var fs = new FakeFileSystem();
        fs.Directories.Add(Src);
        fs.Directories.Add(Dst);

        return fs;
    }


    [Fact]
    public void NothingInCommon_IsNoCollisions_AndEveryFileIsFree() {
        var fs = Folders();
        fs.Files[@"C:\src\docs\a.txt"] = new byte[] { 1 };
        fs.Files[@"C:\src\docs\b.txt"] = new byte[] { 2 };
        fs.Files[@"C:\dst\docs\c.txt"] = new byte[] { 3 };

        var scan = MergeScanner.Scan(fs, Src, Dst, isMove: false);

        Assert.Empty(scan.Conflicts);
        Assert.Equal(2, scan.FreeFiles);
    }

    [Fact]
    public void ANameOnBothSides_IsACollision_WithBothEntries() {
        var fs = Folders();
        fs.Files[@"C:\src\docs\a.txt"] = new byte[] { 1 };
        fs.Files[@"C:\dst\docs\a.txt"] = new byte[] { 9, 9 };

        var scan = MergeScanner.Scan(fs, Src, Dst, isMove: true);

        var only = Assert.Single(scan.Conflicts);
        Assert.Equal(@"C:\src\docs\a.txt", only.Conflict.Source.FullPath);
        Assert.Equal(@"C:\dst\docs\a.txt", only.Conflict.ExistingTarget.FullPath);
        Assert.Equal(2, only.Conflict.ExistingTarget.Size);
        Assert.True(only.Conflict.IsMove);
        Assert.Equal(0, scan.FreeFiles);
    }

    [Fact]
    public void AFolderOnBothSides_CarriesItsOwnCollisionsUnderneath() {
        var fs = Folders();
        fs.Directories.Add(@"C:\src\docs\sub");
        fs.Directories.Add(@"C:\dst\docs\sub");
        fs.Files[@"C:\src\docs\sub\deep.txt"] = new byte[] { 1 };
        fs.Files[@"C:\src\docs\sub\free.txt"] = new byte[] { 2 };
        fs.Files[@"C:\dst\docs\sub\deep.txt"] = new byte[] { 9 };

        var scan = MergeScanner.Scan(fs, Src, Dst, isMove: false);

        var sub = Assert.Single(scan.Conflicts);
        Assert.Equal(@"C:\src\docs\sub", sub.Conflict.Source.FullPath);
        Assert.Equal(@"C:\src\docs\sub\deep.txt", Assert.Single(sub.Children).Conflict.Source.FullPath);
        // The free file inside counts for the folder and for the whole scan.
        Assert.Equal(1, sub.FreeFiles);
        Assert.Equal(1, scan.FreeFiles);
    }

    [Fact]
    public void AFreeFolder_CountsTheFilesInsideIt() {
        var fs = Folders();
        fs.Directories.Add(@"C:\src\docs\new");
        fs.Directories.Add(@"C:\src\docs\new\deeper");
        fs.Files[@"C:\src\docs\new\a.txt"] = new byte[] { 1 };
        fs.Files[@"C:\src\docs\new\deeper\b.txt"] = new byte[] { 2 };

        var scan = MergeScanner.Scan(fs, Src, Dst, isMove: false);

        Assert.Empty(scan.Conflicts);
        Assert.Equal(2, scan.FreeFiles);
    }

    [Fact]
    public void AFileMeetingAFolder_IsACollision_NotAMerge() {
        var fs = Folders();
        fs.Files[@"C:\src\docs\thing"] = new byte[] { 1 };
        fs.Directories.Add(@"C:\dst\docs\thing");

        var scan = MergeScanner.Scan(fs, Src, Dst, isMove: false);

        var only = Assert.Single(scan.Conflicts);
        Assert.Empty(only.Children);
        Assert.Equal(EntryKind.Directory, only.Conflict.ExistingTarget.Kind);
    }

    [Fact]
    public void Cancelled_Throws() {
        var fs = Folders();
        fs.Files[@"C:\src\docs\a.txt"] = new byte[] { 1 };
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() => MergeScanner.Scan(fs, Src, Dst, isMove: false, cts.Token));
    }
}
