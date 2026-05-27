using Wander.Core.FileSystem;
using Wander.Core.Logging;
using Wander.Core.Operations;
using Wander.Core.Tests.Fakes;
using Wander.Core.Undo;

namespace Wander.Core.Tests;

public class FileOperationServiceTests {
    private static (FileOperationService Ops, FakeFileSystem Fs, FakeRecycleBin Bin, UndoService Undo) Setup() {
        var fs = new FakeFileSystem();
        var bin = new FakeRecycleBin(fs);
        var undo = new UndoService();
        var tracker = new OperationTracker();
        var ops = new FileOperationService(fs, bin, undo, tracker, NullLogger.Instance);
        return (ops, fs, bin, undo);
    }


    [Fact]
    public void Copy_File_CallsCopyFile() {
        var (ops, fs, _, _) = Setup();
        fs.Files[@"C:\a.txt"] = new byte[] { 1, 2, 3 };

        ops.Copy(@"C:\a.txt", @"C:\b.txt");

        Assert.Contains(@"CopyFile:C:\a.txt->C:\b.txt:False", fs.CallLog);
    }

    [Fact]
    public void Copy_Directory_CallsCopyDirectory() {
        var (ops, fs, _, _) = Setup();
        fs.Directories.Add(@"C:\src");

        ops.Copy(@"C:\src", @"C:\dst");

        Assert.Contains(@"CopyDirectory:C:\src->C:\dst:False", fs.CallLog);
    }

    [Fact]
    public void Copy_MissingSource_Throws() {
        var (ops, _, _, _) = Setup();

        Assert.Throws<FileNotFoundException>(() => ops.Copy(@"C:\missing", @"C:\x"));
    }

    [Fact]
    public void Delete_File_RoutesToRecycleBin() {
        var (ops, fs, bin, _) = Setup();
        fs.Files[@"C:\a.txt"] = new byte[0];

        ops.Delete(@"C:\a.txt");

        Assert.Contains(@"Recycle:C:\a.txt", bin.CallLog);
        Assert.DoesNotContain(@"DeleteFile:C:\a.txt", fs.CallLog);
    }

    [Fact]
    public void Delete_Directory_RoutesToRecycleBin() {
        var (ops, fs, bin, _) = Setup();
        fs.Directories.Add(@"C:\dir");

        ops.Delete(@"C:\dir");

        Assert.Contains(@"Recycle:C:\dir", bin.CallLog);
        Assert.DoesNotContain(@"DeleteDirectory:C:\dir:True", fs.CallLog);
    }

    [Fact]
    public void PermanentDelete_File_BypassesRecycleBin() {
        var (ops, fs, bin, _) = Setup();
        fs.Files[@"C:\a.txt"] = new byte[0];

        ops.PermanentDelete(@"C:\a.txt");

        Assert.Contains(@"DeleteFile:C:\a.txt", fs.CallLog);
        Assert.Empty(bin.CallLog);
    }

    [Fact]
    public void PermanentDelete_Directory_BypassesRecycleBin() {
        var (ops, fs, bin, _) = Setup();
        fs.Directories.Add(@"C:\dir");

        ops.PermanentDelete(@"C:\dir");

        Assert.Contains(@"DeleteDirectory:C:\dir:True", fs.CallLog);
        Assert.Empty(bin.CallLog);
    }

    [Fact]
    public void PermanentDelete_ClearsUndoStack() {
        var (ops, fs, _, undo) = Setup();
        fs.Files[@"C:\a.txt"] = new byte[0];
        fs.Files[@"C:\b.txt"] = new byte[0];

        ops.Rename(@"C:\a.txt", "renamed.txt");
        Assert.Equal(1, undo.Depth);

        ops.PermanentDelete(@"C:\b.txt");
        Assert.Equal(0, undo.Depth);
        Assert.False(undo.CanUndo);
    }

    [Fact]
    public void Move_CallsMoveEntry() {
        var (ops, fs, _, _) = Setup();
        fs.Files[@"C:\a.txt"] = new byte[0];

        ops.Move(@"C:\a.txt", @"C:\b.txt");

        Assert.Contains(@"MoveEntry:C:\a.txt->C:\b.txt", fs.CallLog);
    }

    [Fact]
    public void Rename_EmptyName_Throws() {
        var (ops, _, _, _) = Setup();

        Assert.Throws<ArgumentException>(() => ops.Rename(@"C:\a.txt", " "));
    }

    [Fact]
    public void CreateFolder_CombinesPath() {
        var (ops, fs, _, _) = Setup();

        ops.CreateFolder(@"C:\base", "new");

        Assert.Contains(@"CreateDirectory:C:\base\new", fs.CallLog);
    }


    // --- Undo round-trips ---------------------------------------------

    [Fact]
    public void Rename_PushesUndo_ThatReversesTheRename() {
        var (ops, fs, _, undo) = Setup();
        fs.Files[@"C:\a.txt"] = new byte[0];

        ops.Rename(@"C:\a.txt", "b.txt");
        Assert.True(undo.CanUndo);

        undo.Undo();
        Assert.Contains(@"Rename:C:\b.txt->a.txt", fs.CallLog);
    }

    [Fact]
    public void Delete_PushesUndo_ThatRestoresViaRecycleBin() {
        var (ops, fs, bin, undo) = Setup();
        fs.Files[@"C:\a.txt"] = new byte[] { 1, 2, 3 };

        ops.Delete(@"C:\a.txt");
        Assert.True(undo.CanUndo);
        Assert.False(fs.FileExists(@"C:\a.txt"));

        undo.Undo();
        Assert.Contains(@"Restore:C:\a.txt", bin.CallLog);
        Assert.True(fs.FileExists(@"C:\a.txt"));
    }

    [Fact]
    public void CreateFolder_PushesUndo_ThatRecyclesTheNewFolder() {
        var (ops, fs, bin, undo) = Setup();

        ops.CreateFolder(@"C:\base", "new");
        Assert.Contains(@"C:\base\new", fs.Directories);

        undo.Undo();
        Assert.Contains(@"Recycle:C:\base\new", bin.CallLog);
        Assert.DoesNotContain(@"C:\base\new", fs.Directories);
    }
}
