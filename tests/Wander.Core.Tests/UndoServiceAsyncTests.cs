using Wander.Core.FileSystem;
using Wander.Core.Operations;
using Wander.Core.Undo;

namespace Wander.Core.Tests;

public class UndoServiceAsyncTests {
    private sealed class Step : IUndoableAction {
        private readonly List<string> _log;


        public Step(string name, List<string> log) {
            Description = name;
            _log = log;
        }


        public string Description { get; }

        public Action? OnUndo { get; set; }

        public IReadOnlyList<string> PathsAfterUndo => new[] { @"C:\back\" + Description };


        public void Undo() {
            OnUndo?.Invoke();
            _log.Add(Description);
        }
    }


    private static (UndoService Undo, OperationTracker Tracker, List<string> Log) Setup() {
        return (new UndoService(), new OperationTracker(TimeSpan.Zero), new List<string>());
    }


    [Fact]
    public async Task EmptyStack_ReturnsNull() {
        var (undo, tracker, _) = Setup();

        Assert.Null(await undo.UndoAsync(tracker));
    }

    [Fact]
    public async Task SingleAction_IsUndone_AndReportedWhole() {
        var (undo, tracker, log) = Setup();
        var action = new Step("a", log);
        undo.Push(action);

        var outcome = await undo.UndoAsync(tracker);

        Assert.Same(action, outcome!.Undone);
        Assert.Null(outcome.Remaining);
        Assert.Empty(outcome.Failures);
        Assert.False(outcome.Cancelled);
        Assert.Equal(0, undo.Depth);
    }

    [Fact]
    public async Task Bundle_UnwindsLastStepFirst_AndShowsUpInTheTracker() {
        var (undo, tracker, log) = Setup();
        OperationSnapshot? seen = null;
        var steps = new[] { new Step("a", log), new Step("b", log), new Step("c", log) };
        steps[1].OnUndo = () => seen = tracker.Snapshot().Single();
        undo.Push(new CompositeAction("delete of 3 items", steps));

        var outcome = await undo.UndoAsync(tracker);

        Assert.Equal(new[] { "c", "b", "a" }, log);
        Assert.Equal(OperationVerbs.Undo, seen!.Verb);
        Assert.Equal(3, seen.Total);
        Assert.Equal(1, seen.Completed);
        Assert.Empty(tracker.Snapshot());
        Assert.Equal(3, outcome!.Undone!.PathsAfterUndo.Count);
    }

    [Fact]
    public async Task WhileItRuns_TheStackIsBusy() {
        var (undo, tracker, log) = Setup();
        bool? canUndoInside = null;
        var action = new Step("a", log) { OnUndo = () => canUndoInside = undo.CanUndo };
        undo.Push(new Step("older", log));
        undo.Push(action);

        await undo.UndoAsync(tracker);

        Assert.False(canUndoInside);
        Assert.True(undo.CanUndo);
    }

    [Fact]
    public async Task Cancelled_TheStepsNotReached_GoBackOnTheStack() {
        var (undo, tracker, log) = Setup();
        using var cts = new CancellationTokenSource();
        var steps = new[] { new Step("a", log), new Step("b", log), new Step("c", log) };
        steps[2].OnUndo = cts.Cancel;
        undo.Push(new CompositeAction("move of 3 items", steps));

        var outcome = await undo.UndoAsync(tracker, cts.Token);

        Assert.True(outcome!.Cancelled);
        Assert.Equal(new[] { "c" }, log);
        Assert.Equal(new[] { @"C:\back\c" }, outcome.Undone!.PathsAfterUndo);
        Assert.Equal(1, undo.Depth);
        Assert.Equal("move of 3 items", undo.NextDescription);

        // The next Ctrl+Z goes on from where this one stopped.
        var rest = await undo.UndoAsync(tracker);

        Assert.Equal(new[] { "c", "b", "a" }, log);
        Assert.False(rest!.Cancelled);
        Assert.Equal(0, undo.Depth);
    }

    [Fact]
    public async Task FailedStep_IsReportedAndLeftBehind_TheRestStillComesBack() {
        var (undo, tracker, log) = Setup();
        var steps = new[] { new Step("a", log), new Step("b", log), new Step("c", log) };
        steps[1].OnUndo = () => throw new IOException("not in the bin any more");
        undo.Push(new CompositeAction("delete of 3 items", steps));

        var outcome = await undo.UndoAsync(tracker);

        Assert.Equal(new[] { "c", "a" }, log);
        Assert.Same(steps[1], Assert.Single(outcome!.Failures).Step);
        Assert.Equal(new[] { @"C:\back\a", @"C:\back\c" }, outcome.Undone!.PathsAfterUndo);
        Assert.Null(outcome.Remaining);
        Assert.Equal(0, undo.Depth);
    }

    [Fact]
    public async Task FailedSingleAction_UndoesNothing_AndDoesNotWedgeTheStack() {
        var (undo, tracker, log) = Setup();
        undo.Push(new Step("older", log));
        undo.Push(new Step("gone", log) { OnUndo = () => throw new IOException("gone") });

        var outcome = await undo.UndoAsync(tracker);

        Assert.Null(outcome!.Undone);
        Assert.Single(outcome.Failures);
        Assert.Equal("older", undo.NextDescription);
    }
}
