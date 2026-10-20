using Wander.Core.FileSystem;
using Wander.Core.Operations;
using Wander.Core.Tests.Fakes;
using Wander.Core.Undo;

namespace Wander.Core.Tests;

public class UndoDestinationsTests {
    private static readonly HashSet<string> _folders = new(StringComparer.OrdinalIgnoreCase) { @"C:\", @"D:\", @"D:\new", @"C:\b" };


    [Fact]
    public void Missing_TheFolderTheItemsCameFromIsGone_ListsIt_WithItsItems_InTheOrderTheUndoGoes() {
        var fs = new FakeFileSystem();
        var moved = new CompositeAction("move of 2 items", new IUndoableAction[] {
            new MoveAction(fs, @"C:\a\1.txt", @"D:\new\1.txt"),
            new MoveAction(fs, @"C:\a\2.txt", @"D:\new\2.txt"),
        });

        var gone = UndoDestinations.Missing(moved, _folders.Contains);

        var only = Assert.Single(gone);
        Assert.Equal(@"C:\a", only.Folder);
        Assert.Equal(new[] { @"C:\a\2.txt", @"C:\a\1.txt" }, only.Items);
    }

    [Fact]
    public void Missing_TheFolderIsThere_Nothing() {
        var fs = new FakeFileSystem();

        Assert.Empty(UndoDestinations.Missing(new MoveAction(fs, @"C:\b\1.txt", @"D:\new\1.txt"), _folders.Contains));
    }

    [Fact]
    public void Missing_TheDriveIsNotThere_Nothing() {
        // The drive comes back with the folder on it: not a question.
        var fs = new FakeFileSystem();

        Assert.Empty(UndoDestinations.Missing(new MoveAction(fs, @"E:\a\1.txt", @"D:\new\1.txt"), _folders.Contains));
    }

    [Fact]
    public void Missing_ARename_StaysInItsFolder_Nothing() {
        var fs = new FakeFileSystem();

        Assert.Empty(UndoDestinations.Missing(new RenameAction(fs, @"C:\a\new.txt", "old.txt"), _folders.Contains));
    }

    [Fact]
    public void Missing_TheUndoBringsTheFolderBackFirst_Nothing() {
        // A merge moved everything out of C:\a and binned it emptied: the
        // undo restores C:\a before the files go back into it.
        var fs = new FakeFileSystem();
        var bin = new FakeRecycleBin(fs);
        var merged = new CompositeAction("move of 1 items", new IUndoableAction[] {
            new MoveAction(fs, @"C:\a\1.txt", @"D:\new\1.txt"),
            new DeleteAction(bin, new RecycleHandle(@"C:\a", DateTime.UtcNow, "id")),
        });

        Assert.Empty(UndoDestinations.Missing(merged, _folders.Contains));
    }

    [Fact]
    public void Redirected_NothingGoesIntoThoseFolders_IsItself() {
        var fs = new FakeFileSystem();
        var moved = new CompositeAction("move of 1 items", new IUndoableAction[] { new MoveAction(fs, @"C:\b\1.txt", @"D:\new\1.txt") });

        Assert.Same(moved, moved.Redirected(Redirect(@"C:\a", @"C:\b")));
    }

    [Fact]
    public async Task UndoAsync_Redirected_PutsTheItemsWhereTheUserSaid_KeepsTheRest() {
        var fs = new FakeFileSystem();
        fs.Files[@"D:\new\1.txt"] = new byte[] { 1 };
        fs.Files[@"D:\new\2.txt"] = new byte[] { 2 };
        var undo = new UndoService();
        undo.Push(new CompositeAction("move of 2 items", new IUndoableAction[] {
            new MoveAction(fs, @"C:\a\1.txt", @"D:\new\1.txt"),
            new MoveAction(fs, @"C:\b\2.txt", @"D:\new\2.txt"),
        }, OperationVerbs.Move));

        var outcome = await undo.UndoAsync(new OperationTracker(TimeSpan.Zero), redirect: Redirect(@"C:\a", @"C:\picked"));

        Assert.Empty(outcome!.Failures);
        Assert.True(fs.FileExists(@"C:\picked\1.txt"));
        Assert.True(fs.FileExists(@"C:\b\2.txt"));
        Assert.Equal(new[] { @"C:\picked\1.txt", @"C:\b\2.txt" }, outcome.Undone!.PathsAfterUndo);
    }


    private static Dictionary<string, string> Redirect(string gone, string instead) {
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [gone] = instead };
    }
}
