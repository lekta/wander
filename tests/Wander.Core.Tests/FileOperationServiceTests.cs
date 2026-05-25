using Wander.Core.FileSystem;
using Wander.Core.Tests.Fakes;

namespace Wander.Core.Tests;

public class FileOperationServiceTests {
    [Fact]
    public void Copy_File_CallsCopyFile() {
        var fs = new FakeFileSystem();
        fs.Files[@"C:\a.txt"] = new byte[] { 1, 2, 3 };
        var ops = new FileOperationService(fs);

        ops.Copy(@"C:\a.txt", @"C:\b.txt");

        Assert.Contains(@"CopyFile:C:\a.txt->C:\b.txt:False", fs.CallLog);
    }

    [Fact]
    public void Copy_Directory_CallsCopyDirectory() {
        var fs = new FakeFileSystem();
        fs.Directories.Add(@"C:\src");
        var ops = new FileOperationService(fs);

        ops.Copy(@"C:\src", @"C:\dst");

        Assert.Contains(@"CopyDirectory:C:\src->C:\dst:False", fs.CallLog);
    }

    [Fact]
    public void Copy_MissingSource_Throws() {
        var fs = new FakeFileSystem();
        var ops = new FileOperationService(fs);

        Assert.Throws<FileNotFoundException>(() => ops.Copy(@"C:\missing", @"C:\x"));
    }

    [Fact]
    public void Delete_File_CallsDeleteFile() {
        var fs = new FakeFileSystem();
        fs.Files[@"C:\a.txt"] = new byte[0];
        var ops = new FileOperationService(fs);

        ops.Delete(@"C:\a.txt");

        Assert.Contains(@"DeleteFile:C:\a.txt", fs.CallLog);
    }

    [Fact]
    public void Delete_Directory_CallsDeleteDirectoryRecursive() {
        var fs = new FakeFileSystem();
        fs.Directories.Add(@"C:\dir");
        var ops = new FileOperationService(fs);

        ops.Delete(@"C:\dir");

        Assert.Contains(@"DeleteDirectory:C:\dir:True", fs.CallLog);
    }

    [Fact]
    public void Move_CallsMoveEntry() {
        var fs = new FakeFileSystem();
        fs.Files[@"C:\a.txt"] = new byte[0];
        var ops = new FileOperationService(fs);

        ops.Move(@"C:\a.txt", @"C:\b.txt");

        Assert.Contains(@"MoveEntry:C:\a.txt->C:\b.txt", fs.CallLog);
    }

    [Fact]
    public void Rename_EmptyName_Throws() {
        var fs = new FakeFileSystem();
        var ops = new FileOperationService(fs);

        Assert.Throws<ArgumentException>(() => ops.Rename(@"C:\a.txt", " "));
    }

    [Fact]
    public void CreateFolder_CombinesPath() {
        var fs = new FakeFileSystem();
        var ops = new FileOperationService(fs);

        ops.CreateFolder(@"C:\base", "new");

        Assert.Contains(@"CreateDirectory:C:\base\new", fs.CallLog);
    }
}
