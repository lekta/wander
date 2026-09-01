using Wander.Core.FileSystem;
using Wander.Core.Tests.Fakes;
using Wander.Core.Undo;

namespace Wander.Core.Tests;

public class UndoableActionsTests {
    // --- Paths reused across cases ------------------------------------
    private const string NewPath = @"C:\new.txt";
    private const string OldName = "old.txt";
    private const string MoveNew = @"C:\new\a.txt";
    private const string MoveOld = @"C:\old\a.txt";
    private const string GonePath = @"C:\gone.txt";


    private sealed class TrackingAction : IUndoableAction {
        public TrackingAction(string desc, List<string> log) {
            Description = desc;
            _log = log;
        }
        private readonly List<string> _log;
        public string Description { get; }
        public Action? OnUndo { get; set; }
        public void Undo() {
            _log.Add(Description);
            OnUndo?.Invoke();
        }
    }


    // --- CompositeAction -----------------------------------------------

    [Fact]
    public void Composite_UndoesChildren_InReverseOrder() {
        // Order matters when later steps depend on earlier ones (e.g. a move
        // then a delete: undoing the delete first restores the file at its
        // *new* location, then the move-undo puts it back where it belonged).
        var log = new List<string>();
        var composite = new CompositeAction("3 ops", new IUndoableAction[] {
            new TrackingAction("first", log),
            new TrackingAction("second", log),
            new TrackingAction("third", log),
        });

        composite.Undo();

        Assert.Equal(new[] { "third", "second", "first" }, log);
    }

    [Fact]
    public void Composite_Empty_UndoIsNoOp() {
        var composite = new CompositeAction("nothing", Array.Empty<IUndoableAction>());

        var ex = Record.Exception(() => composite.Undo());

        Assert.Null(ex);
    }

    [Fact]
    public void Composite_Description_IsExposedVerbatim() {
        var composite = new CompositeAction("move of 5 items", Array.Empty<IUndoableAction>());

        Assert.Equal("move of 5 items", composite.Description);
    }

    [Fact]
    public void Composite_PropagatesExceptionFromChild_AndStops() {
        var log = new List<string>();
        var composite = new CompositeAction("oops", new IUndoableAction[] {
            new TrackingAction("first", log),
            new TrackingAction("second", log) { OnUndo = () => throw new InvalidOperationException("boom") },
            new TrackingAction("third", log),
        });

        Assert.Throws<InvalidOperationException>(() => composite.Undo());
        // Reverse order: third first, then second throws — first should not run.
        Assert.Equal(new[] { "third", "second" }, log);
    }


    // --- Single-action helpers (round-trip with FakeFileSystem) -------

    [Fact]
    public void RenameAction_Undo_RestoresOriginalName() {
        var fs = new FakeFileSystem();
        fs.Files[NewPath] = new byte[0];
        var action = new RenameAction(fs, NewPath, OldName);

        action.Undo();

        Assert.Contains($"Rename:{NewPath}->{OldName}", fs.CallLog);
    }

    [Fact]
    public void MoveAction_Undo_MovesBack() {
        var fs = new FakeFileSystem();
        fs.Files[MoveNew] = new byte[0];
        var action = new MoveAction(fs, MoveOld, MoveNew);

        action.Undo();

        Assert.Contains($"MoveEntry:{MoveNew}->{MoveOld}", fs.CallLog);
    }

    [Fact]
    public void CreateAction_Undo_SendsCreatedItemToRecycleBin() {
        var fs = new FakeFileSystem();
        fs.Files[NewPath] = new byte[0];
        var bin = new FakeRecycleBin(fs);
        var action = new CreateAction(bin, NewPath);

        action.Undo();

        Assert.Contains($"Recycle:{NewPath}", bin.CallLog);
        Assert.False(fs.FileExists(NewPath));
    }

    [Fact]
    public void DeleteAction_Undo_RestoresViaHandle() {
        var fs = new FakeFileSystem();
        fs.Files[GonePath] = new byte[] { 7 };
        var bin = new FakeRecycleBin(fs);
        var handle = bin.Send(GonePath);
        var action = new DeleteAction(bin, handle);

        action.Undo();

        Assert.Contains($"Restore:{GonePath}", bin.CallLog);
        Assert.True(fs.FileExists(GonePath));
    }


    // --- PathsAfterUndo ------------------------------------------------
    // What the UI re-selects after Ctrl+Z. Wrong answers here are not a
    // crash, they are the selection quietly landing on the wrong file.

    [Fact]
    public void RenameAction_PathsAfterUndo_IsTheOldNameInTheSameFolder() {
        var action = new RenameAction(new FakeFileSystem(), NewPath, OldName);

        Assert.Equal(new[] { @"C:\old.txt" }, action.PathsAfterUndo);
    }

    [Fact]
    public void MoveAction_PathsAfterUndo_IsWhereItCameFrom() {
        var action = new MoveAction(new FakeFileSystem(), MoveOld, MoveNew);

        Assert.Equal(new[] { MoveOld }, action.PathsAfterUndo);
    }

    [Fact]
    public void DeleteAction_PathsAfterUndo_IsTheOriginalLocation() {
        var fs = new FakeFileSystem();
        fs.Files[GonePath] = new byte[] { 7 };
        var bin = new FakeRecycleBin(fs);
        var action = new DeleteAction(bin, bin.Send(GonePath));

        Assert.Equal(new[] { GonePath }, action.PathsAfterUndo);
    }

    [Fact]
    public void CreateAction_PathsAfterUndo_IsEmpty_BecauseUndoRemovesIt() {
        var action = new CreateAction(new FakeRecycleBin(new FakeFileSystem()), NewPath);

        Assert.Empty(action.PathsAfterUndo);
    }

    [Fact]
    public void Composite_PathsAfterUndo_CollectsEveryMember() {
        var fs = new FakeFileSystem();
        var composite = new CompositeAction("2 ops", new IUndoableAction[] {
            new MoveAction(fs, MoveOld, MoveNew),
            new RenameAction(fs, NewPath, OldName),
        });

        Assert.Equal(new[] { MoveOld, @"C:\old.txt" }, composite.PathsAfterUndo);
    }

    [Fact]
    public void Composite_PathsAfterUndo_SkipsMembersThatHaveNone() {
        var fs = new FakeFileSystem();
        var composite = new CompositeAction("2 ops", new IUndoableAction[] {
            new CreateAction(new FakeRecycleBin(fs), NewPath),
            new MoveAction(fs, MoveOld, MoveNew),
        });

        Assert.Equal(new[] { MoveOld }, composite.PathsAfterUndo);
    }
}
