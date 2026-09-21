using Wander.Core.Operations;

namespace Wander.Core.Tests;

public class BusyWaitTests {
    private static readonly TimeSpan _step = TimeSpan.FromMilliseconds(200);


    [Fact]
    public void FreePath_CostsNothing() {
        var pauses = new List<TimeSpan>();
        var wait = new BusyWait(pauses.Add);

        var result = wait.UntilFree(() => false);

        Assert.Equal(new BusyWaitResult(Free: true, WasBusy: false, TimeSpan.Zero), result);
        Assert.Empty(pauses);
        Assert.Equal(BusyWait.DefaultBudget, wait.Remaining);
    }

    [Fact]
    public void LetGoAfterAWhile_GoesAheadAtOnce_AndSaysItWasBusy() {
        var pauses = new List<TimeSpan>();
        var wait = new BusyWait(pauses.Add);
        int looks = 0;

        var result = wait.UntilFree(() => ++looks <= 3);

        Assert.True(result.Free);
        Assert.True(result.WasBusy);
        Assert.Equal(3 * _step, result.Waited);
        Assert.Equal(new[] { _step, _step, _step }, pauses);
    }

    [Fact]
    public void NeverLetGo_GivesUpWhenTheBudgetIsSpent() {
        var pauses = new List<TimeSpan>();
        var wait = new BusyWait(pauses.Add);

        var result = wait.UntilFree(() => true);

        Assert.False(result.Free);
        Assert.Equal(BusyWait.DefaultBudget, result.Waited);
        Assert.Equal(10, pauses.Count);
        Assert.Equal(TimeSpan.Zero, wait.Remaining);
    }

    [Fact]
    public void TheBudgetIsTheOperations_NotTheFiles() {
        var pauses = new List<TimeSpan>();
        var wait = new BusyWait(pauses.Add);
        wait.UntilFree(() => true);
        pauses.Clear();

        var second = wait.UntilFree(() => true);

        Assert.False(second.Free);
        Assert.Equal(TimeSpan.Zero, second.Waited);
        Assert.Empty(pauses);
    }

    [Fact]
    public void ABudgetThatIsNotAWholeNumberOfSteps_IsSpentToTheEnd() {
        var pauses = new List<TimeSpan>();
        var wait = new BusyWait(pauses.Add, budget: TimeSpan.FromMilliseconds(500));

        wait.UntilFree(() => true);

        Assert.Equal(new[] { _step, _step, TimeSpan.FromMilliseconds(100) }, pauses);
    }

    [Fact]
    public void Cancelled_StopsWaiting() {
        using var cts = new CancellationTokenSource();
        var wait = new BusyWait(_ => cts.Cancel());

        Assert.Throws<OperationCanceledException>(() => wait.UntilFree(() => true, cts.Token));
    }
}
