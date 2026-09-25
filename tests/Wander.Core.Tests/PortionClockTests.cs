using Wander.Core.Listing;

namespace Wander.Core.Tests;

public class PortionClockTests {
    [Fact]
    public void Quick_NothingEarly() {
        var clock = new PortionClock();

        Assert.False(clock.Due(0));
        Assert.False(clock.Due(PortionClock.FirstMs - 1));
    }

    [Fact]
    public void Slow_FirstPortion_ThenOnePerInterval() {
        var clock = new PortionClock();

        Assert.True(clock.Due(PortionClock.FirstMs));
        Assert.False(clock.Due(PortionClock.FirstMs + 10));
        Assert.False(clock.Due(PortionClock.FirstMs + PortionClock.EveryMs - 1));
        Assert.True(clock.Due(PortionClock.FirstMs + PortionClock.EveryMs));
    }

    /// <summary>A row that took long itself: the next portion counts from when this one was given.</summary>
    [Fact]
    public void LateAsk_TheIntervalRunsFromIt() {
        var clock = new PortionClock();

        Assert.True(clock.Due(5_000));
        Assert.False(clock.Due(5_000 + PortionClock.EveryMs - 1));
        Assert.True(clock.Due(5_000 + PortionClock.EveryMs));
    }
}
