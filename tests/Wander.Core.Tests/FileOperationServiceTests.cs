using Wander.Core.FileSystem;
using Wander.Core.Logging;
using Wander.Core.Operations;
using Wander.Core.Tests.Fakes;
using Wander.Core.Undo;

namespace Wander.Core.Tests;

public class FileOperationServiceTests {
    // --- Paths reused across cases ------------------------------------
    private const string FileA = @"C:\a.txt";
    private const string FileB = @"C:\b.txt";
    private const string FileMissing = @"C:\missing";
    private const string FileRenamed = @"C:\renamed.txt";
    private const string DirSrc = @"C:\src";
    private const string DirDst = @"C:\dst";
    private const string DirX = @"C:\dir";
    private const string XGenericTarget = @"C:\x";

    private const string FileBRenamed = @"C:\b.txt";  // alias for the rename test
    private const string RenamedNewName = "b.txt";
    private const string RenamedRevertName = "a.txt";

    private const string BaseFolder = @"C:\base";
    private const string NewFolderName = "new";
    private const string NewFolderPath = @"C:\base\new";


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
        fs.Files[FileA] = new byte[] { 1, 2, 3 };

        ops.Copy(FileA, FileB);

        Assert.Contains($"CopyFile:{FileA}->{FileB}:False", fs.CallLog);
    }

    [Fact]
    public void Copy_Directory_CallsCopyDirectory() {
        var (ops, fs, _, _) = Setup();
        fs.Directories.Add(DirSrc);

        ops.Copy(DirSrc, DirDst);

        Assert.Contains($"CopyDirectory:{DirSrc}->{DirDst}:False", fs.CallLog);
    }

    [Fact]
    public void Copy_MissingSource_Throws() {
        var (ops, _, _, _) = Setup();

        Assert.Throws<FileNotFoundException>(() => ops.Copy(FileMissing, XGenericTarget));
    }

    [Fact]
    public void Delete_File_RoutesToRecycleBin() {
        var (ops, fs, bin, _) = Setup();
        fs.Files[FileA] = new byte[0];

        ops.Delete(FileA);

        Assert.Contains($"Recycle:{FileA}", bin.CallLog);
        Assert.DoesNotContain($"DeleteFile:{FileA}", fs.CallLog);
    }

    [Fact]
    public void Delete_Directory_RoutesToRecycleBin() {
        var (ops, fs, bin, _) = Setup();
        fs.Directories.Add(DirX);

        ops.Delete(DirX);

        Assert.Contains($"Recycle:{DirX}", bin.CallLog);
        Assert.DoesNotContain($"DeleteDirectory:{DirX}:True", fs.CallLog);
    }

    [Fact]
    public void PermanentDelete_File_BypassesRecycleBin() {
        var (ops, fs, bin, _) = Setup();
        fs.Files[FileA] = new byte[0];

        ops.PermanentDelete(FileA);

        Assert.Contains($"DeleteFile:{FileA}", fs.CallLog);
        Assert.Empty(bin.CallLog);
    }

    [Fact]
    public void PermanentDelete_Directory_BypassesRecycleBin() {
        var (ops, fs, bin, _) = Setup();
        fs.Directories.Add(DirX);

        ops.PermanentDelete(DirX);

        Assert.Contains($"DeleteDirectory:{DirX}:True", fs.CallLog);
        Assert.Empty(bin.CallLog);
    }

    [Fact]
    public void PermanentDelete_ClearsUndoStack() {
        var (ops, fs, _, undo) = Setup();
        fs.Files[FileA] = new byte[0];
        fs.Files[FileB] = new byte[0];

        ops.Rename(FileA, "renamed.txt");
        Assert.Equal(1, undo.Depth);

        ops.PermanentDelete(FileB);
        Assert.Equal(0, undo.Depth);
        Assert.False(undo.CanUndo);
    }

    [Fact]
    public void Move_CallsMoveEntry() {
        var (ops, fs, _, _) = Setup();
        fs.Files[FileA] = new byte[0];

        ops.Move(FileA, FileB);

        Assert.Contains($"MoveEntry:{FileA}->{FileB}", fs.CallLog);
    }

    [Fact]
    public void Rename_EmptyName_Throws() {
        var (ops, _, _, _) = Setup();

        Assert.Throws<ArgumentException>(() => ops.Rename(FileA, " "));
    }

    [Fact]
    public void CreateFolder_CombinesPath() {
        var (ops, fs, _, _) = Setup();

        ops.CreateFolder(BaseFolder, NewFolderName);

        Assert.Contains($"CreateDirectory:{NewFolderPath}", fs.CallLog);
    }


    // --- Undo round-trips ---------------------------------------------

    [Fact]
    public void Rename_PushesUndo_ThatReversesTheRename() {
        var (ops, fs, _, undo) = Setup();
        fs.Files[FileA] = new byte[0];

        ops.Rename(FileA, RenamedNewName);
        Assert.True(undo.CanUndo);

        undo.Undo();
        Assert.Contains($"Rename:{FileBRenamed}->{RenamedRevertName}", fs.CallLog);
    }

    [Fact]
    public void Delete_PushesUndo_ThatRestoresViaRecycleBin() {
        var (ops, fs, bin, undo) = Setup();
        fs.Files[FileA] = new byte[] { 1, 2, 3 };

        ops.Delete(FileA);
        Assert.True(undo.CanUndo);
        Assert.False(fs.FileExists(FileA));

        undo.Undo();
        Assert.Contains($"Restore:{FileA}", bin.CallLog);
        Assert.True(fs.FileExists(FileA));
    }

    [Fact]
    public void CreateFolder_PushesUndo_ThatRecyclesTheNewFolder() {
        var (ops, fs, bin, undo) = Setup();

        ops.CreateFolder(BaseFolder, NewFolderName);
        Assert.Contains(NewFolderPath, fs.Directories);

        undo.Undo();
        Assert.Contains($"Recycle:{NewFolderPath}", bin.CallLog);
        Assert.DoesNotContain(NewFolderPath, fs.Directories);
    }
}
