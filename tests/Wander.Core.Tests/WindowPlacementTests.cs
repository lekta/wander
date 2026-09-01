using Wander.Core.Layout;

namespace Wander.Core.Tests;

public class WindowPlacementTests {
    /// <summary>A single 1920x1080 monitor with its top-left at the origin.</summary>
    private static readonly ScreenRect _screen = new(0, 0, 1920, 1080);


    // --- Is the saved size worth restoring -------------------------------

    [Fact]
    public void AnOrdinarySize_IsRestored() {
        Assert.True(WindowPlacement.IsUsableSize(1200, 800));
    }

    /// <summary>
    /// A window this small has no titlebar left to grab, so a saved size
    /// under the floor is a truncation rather than a choice.
    /// </summary>
    [Fact]
    public void ASizeBelowTheFloor_IsIgnored() {
        Assert.False(WindowPlacement.IsUsableSize(200, 800));
        Assert.False(WindowPlacement.IsUsableSize(1200, 100));
        Assert.False(WindowPlacement.IsUsableSize(0, 0));
    }

    [Fact]
    public void TheFloorItself_IsRestored() {
        Assert.True(WindowPlacement.IsUsableSize(WindowPlacement.MinWidth, WindowPlacement.MinHeight));
    }


    // --- Where the window comes back -------------------------------------

    [Fact]
    public void APositionOnScreen_IsLeftAlone() {
        var (left, top) = WindowPlacement.Clamp(new ScreenRect(300, 200, 1000, 700), _screen);

        Assert.Equal(300, left);
        Assert.Equal(200, top);
    }

    /// <summary>
    /// The window may hang off the left edge - what has to stay reachable
    /// is a strip of titlebar, not the whole frame.
    /// </summary>
    [Fact]
    public void OffTheLeftEdge_KeepsAStripVisible() {
        var (left, _) = WindowPlacement.Clamp(new ScreenRect(-5000, 100, 1000, 700), _screen);

        Assert.Equal(-900, left);
    }

    [Fact]
    public void OffTheRightEdge_KeepsAStripVisible() {
        var (left, _) = WindowPlacement.Clamp(new ScreenRect(5000, 100, 1000, 700), _screen);

        Assert.Equal(1820, left);
    }

    /// <summary>
    /// Above the desktop the titlebar itself is gone, so the top is pinned
    /// rather than merely nudged.
    /// </summary>
    [Fact]
    public void AboveTheDesktop_IsPinnedToTheTop() {
        var (_, top) = WindowPlacement.Clamp(new ScreenRect(100, -900, 1000, 700), _screen);

        Assert.Equal(0, top);
    }

    [Fact]
    public void BelowTheDesktop_KeepsAStripVisible() {
        var (_, top) = WindowPlacement.Clamp(new ScreenRect(100, 5000, 1000, 700), _screen);

        Assert.Equal(1020, top);
    }

    /// <summary>
    /// The case this exists for: a second monitor to the left of the main
    /// one, unplugged since the session was saved. The virtual desktop no
    /// longer covers those coordinates and the window has to come back onto
    /// the one screen that is left.
    /// </summary>
    [Fact]
    public void APositionOnAMonitorThatIsGone_ComesBackOntoTheDesktop() {
        var (left, top) = WindowPlacement.Clamp(new ScreenRect(-1800, 300, 1000, 700), _screen);

        Assert.Equal(-900, left);
        Assert.Equal(300, top);
    }

    /// <summary>
    /// A second monitor to the left is part of the virtual desktop, and its
    /// coordinates are negative: "off screen" is not the same as "negative".
    /// </summary>
    [Fact]
    public void NegativeCoordinatesOnASecondMonitor_AreLeftAlone() {
        var wide = new ScreenRect(-1920, 0, 3840, 1080);
        var (left, top) = WindowPlacement.Clamp(new ScreenRect(-1800, 300, 1000, 700), wide);

        Assert.Equal(-1800, left);
        Assert.Equal(300, top);
    }
}
