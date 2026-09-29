using Wander.Core.Imaging;

namespace Wander.Core.Tests;

public class RankedGateTests {
    [Fact]
    public void FreeSlots_AreTakenAtOnce() {
        var gate = new RankedGate(2, _ => 0);

        Assert.True(gate.EnterAsync("a", CancellationToken.None).IsCompletedSuccessfully);
        Assert.True(gate.EnterAsync("b", CancellationToken.None).IsCompletedSuccessfully);
        Assert.False(gate.EnterAsync("c", CancellationToken.None).IsCompleted);
    }


    [Fact]
    public void Release_GoesToTheLowestRank() {
        var ranks = new Dictionary<string, int> { ["late"] = 2, ["selected"] = 1, ["shown"] = 0 };
        var gate = new RankedGate(1, key => ranks.GetValueOrDefault(key, 2));
        gate.EnterAsync("busy", CancellationToken.None);
        var late = gate.EnterAsync("late", CancellationToken.None);
        var selected = gate.EnterAsync("selected", CancellationToken.None);
        var shown = gate.EnterAsync("shown", CancellationToken.None);

        gate.Release();
        Assert.True(shown.IsCompleted);
        Assert.False(selected.IsCompleted);

        gate.Release();
        Assert.True(selected.IsCompleted);
        Assert.False(late.IsCompleted);

        gate.Release();
        Assert.True(late.IsCompleted);
    }


    [Fact]
    public void SameRank_KeepsArrivalOrder() {
        var gate = new RankedGate(1, _ => 2);
        gate.EnterAsync("busy", CancellationToken.None);
        var first = gate.EnterAsync("a", CancellationToken.None);
        var second = gate.EnterAsync("b", CancellationToken.None);

        gate.Release();

        Assert.True(first.IsCompleted);
        Assert.False(second.IsCompleted);
    }


    [Fact]
    public void Rank_IsAskedWhenTheSlotFrees() {
        // Selected while it waited: the file moves up the line.
        var selected = new HashSet<string>();
        var gate = new RankedGate(1, key => selected.Contains(key) ? 1 : 2);
        gate.EnterAsync("busy", CancellationToken.None);
        var a = gate.EnterAsync("a", CancellationToken.None);
        var b = gate.EnterAsync("b", CancellationToken.None);

        selected.Add("b");
        gate.Release();

        Assert.True(b.IsCompleted);
        Assert.False(a.IsCompleted);
    }


    [Fact]
    public void CancelledWait_LeavesTheLine() {
        var gate = new RankedGate(1, key => key == "gone" ? 0 : 2);
        gate.EnterAsync("busy", CancellationToken.None);
        using var cts = new CancellationTokenSource();
        var gone = gate.EnterAsync("gone", cts.Token);
        var stays = gate.EnterAsync("stays", CancellationToken.None);

        cts.Cancel();
        gate.Release();

        Assert.True(gone.IsCanceled);
        Assert.True(stays.IsCompletedSuccessfully);
    }


    [Fact]
    public void ReleaseWithNobodyWaiting_FreesTheSlot() {
        var gate = new RankedGate(1, _ => 0);
        gate.EnterAsync("a", CancellationToken.None);
        gate.Release();

        Assert.True(gate.EnterAsync("b", CancellationToken.None).IsCompletedSuccessfully);
    }


    [Fact]
    public void AlreadyCancelled_DoesNotTakeASlot() {
        var gate = new RankedGate(1, _ => 0);

        Assert.True(gate.EnterAsync("a", new CancellationToken(true)).IsCanceled);
        Assert.True(gate.EnterAsync("b", CancellationToken.None).IsCompletedSuccessfully);
    }
}
