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

    // --- RenameMany (a file plus its companion sidecars) ---------------

    private const string MainAsset = @"C:\assets\Sprite.png";
    private const string MetaAsset = @"C:\assets\Sprite.png.meta";


    [Fact]
    public void RenameMany_RenamesEveryMemberOfTheGroup() {
        var (ops, fs, _, _) = Setup();
        fs.Files[MainAsset] = new byte[0];
        fs.Files[MetaAsset] = new byte[0];

        ops.RenameMany(new[] { (MainAsset, "Ship.png"), (MetaAsset, "Ship.png.meta") });

        Assert.True(fs.Files.ContainsKey(@"C:\assets\Ship.png"));
        Assert.True(fs.Files.ContainsKey(@"C:\assets\Ship.png.meta"));
    }

    [Fact]
    public void RenameMany_LandsAsOneUndoStep() {
        var (ops, fs, _, undo) = Setup();
        fs.Files[MainAsset] = new byte[0];
        fs.Files[MetaAsset] = new byte[0];

        ops.RenameMany(new[] { (MainAsset, "Ship.png"), (MetaAsset, "Ship.png.meta") });
        Assert.Equal(1, undo.Depth);

        undo.Undo();
        Assert.True(fs.Files.ContainsKey(MainAsset));
        Assert.True(fs.Files.ContainsKey(MetaAsset));
    }

    [Fact]
    public void RenameMany_RollsBack_WhenAMemberFails() {
        // Half a renamed group is the exact breakage this feature exists to
        // prevent — a failure must leave the folder as it was.
        var (ops, fs, _, undo) = Setup();
        fs.Files[MainAsset] = new byte[0];
        fs.Files[MetaAsset] = new byte[0];
        fs.RenameFailures.Add(MetaAsset);

        Assert.Throws<IOException>(() => ops.RenameMany(new[] { (MainAsset, "Ship.png"), (MetaAsset, "Ship.png.meta") }));

        Assert.True(fs.Files.ContainsKey(MainAsset));
        Assert.False(fs.Files.ContainsKey(@"C:\assets\Ship.png"));
        Assert.Equal(0, undo.Depth);
    }

    [Fact]
    public void RenameMany_WithOneItem_BehavesLikeRename() {
        var (ops, fs, _, undo) = Setup();
        fs.Files[FileA] = new byte[0];

        ops.RenameMany(new[] { (FileA, RenamedNewName) });

        Assert.Contains($"Rename:{FileA}->{RenamedNewName}", fs.CallLog);
        Assert.Equal(1, undo.Depth);
    }

    [Fact]
    public void RenameMany_EmptyName_Throws_BeforeTouchingAnything() {
        var (ops, fs, _, _) = Setup();
        fs.Files[MainAsset] = new byte[0];

        Assert.Throws<ArgumentException>(() => ops.RenameMany(new[] { (MainAsset, "Ship.png"), (MetaAsset, " ") }));
        Assert.Empty(fs.CallLog);
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
