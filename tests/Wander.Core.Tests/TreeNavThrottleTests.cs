using Wander.Core.Panels;

namespace Wander.Core.Tests;

/// <summary>When a cursor move opens its row, with "arrows open folders" (P-4).</summary>
public class TreeNavThrottleTests {
    [Fact]
    public void Off_Never() {
        Assert.Equal(ThrottleDecision.Never, TreeNavThrottle.Decide(arrowsOpen: false, 1000, null));
    }

    [Fact]
    public void ALonePress_Now() {
        Assert.Equal(ThrottleDecision.Now, TreeNavThrottle.Decide(true, 1000, null));
        Assert.Equal(ThrottleDecision.Now, TreeNavThrottle.Decide(true, 1000, 1000 - TreeNavThrottle.BurstMs));
    }

    [Fact]
    public void APressInABurst_WaitsForTheCursorToRest() {
        var decision = TreeNavThrottle.Decide(true, 1000, 1000 - TreeNavThrottle.BurstMs + 1);

        Assert.Equal(ThrottleOutcome.At, decision.Outcome);
        Assert.Equal(1000 + TreeNavThrottle.SettleMs, decision.AtMs);
    }
}
