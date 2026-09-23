using Wander.Core.Layout;

namespace Wander.Core.Tests;

/// <summary>Scrolling at the edge (U2, decision B20).</summary>
public class EdgeScrollTests {
    [Fact]
    public void OutsideTheZone_NothingScrolls() {
        Assert.Equal(0, EdgeScroll.Velocity(EdgeScroll.Zone));
        Assert.Equal(0, EdgeScroll.Along(300, 600));
    }

    [Fact]
    public void AtTheEdgeAndPastIt_TopSpeed() {
        Assert.Equal(EdgeScroll.MaxSpeed, EdgeScroll.Velocity(0));
        Assert.Equal(EdgeScroll.MaxSpeed, EdgeScroll.Velocity(-40));
    }

    /// <summary>The speed grows with the square of the depth: half way into the zone, a quarter of the top speed.</summary>
    [Fact]
    public void HalfWayIn_AQuarterOfTheTopSpeed() {
        Assert.Equal(EdgeScroll.MaxSpeed / 4, EdgeScroll.Velocity(EdgeScroll.Zone / 2), precision: 6);
    }

    [Fact]
    public void Along_ScrollsBackNearTheStart_AndOnNearTheEnd() {
        Assert.True(EdgeScroll.Along(5, 600) < 0);
        Assert.True(EdgeScroll.Along(595, 600) > 0);
        Assert.Equal(-EdgeScroll.MaxSpeed, EdgeScroll.Along(-10, 600));
        Assert.Equal(EdgeScroll.MaxSpeed, EdgeScroll.Along(640, 600));
    }

    [Fact]
    public void Step_IsSpeedTimesTime() {
        Assert.Equal(75, EdgeScroll.Step(1500, 50), precision: 6);
        Assert.Equal(0, EdgeScroll.Step(1500, -5));
    }
}
