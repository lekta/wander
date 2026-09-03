using Wander.Core.Layout;

namespace Wander.Core.Tests;

public class PaneSizesTests {
    private const double Min = 120;
    private const double Reserve = 240;


    [Fact]
    public void TheSameWindow_GivesThePaneBackExactly() {
        // Seventy per cent of the window is a choice, not an accident.
        Assert.Equal(1348, PaneSizes.Restore(1348, 1925, 1925, Min, Reserve));
    }

    [Fact]
    public void AWindowOnePixelOff_StillCountsAsTheSame() {
        Assert.Equal(748, PaneSizes.Restore(748, 1925, 1924, Min, Reserve));
    }

    [Fact]
    public void AMonitorToALaptop_KeepsTheProportion() {
        // The case this exists for: 748 of 1925 came back as 748 of 1086.
        Assert.Equal(422, PaneSizes.Restore(748, 1925, 1086, Min, Reserve), 0);
    }

    [Fact]
    public void WithoutASavedWindow_TheOldBoundsApply() {
        Assert.Equal(748, PaneSizes.Restore(748, 0, 1086, Min, Reserve));
        Assert.Equal(PaneSizes.LegacyMax, PaneSizes.Restore(4000, 0, 1086, Min, Reserve));
        Assert.Equal(Min, PaneSizes.Restore(10, 0, 1086, Min, Reserve));
    }

    [Fact]
    public void TheShareStopsAtTheReserve() {
        // 900 of 1000 is 450 of 500, and 500 has to keep 240 for the list.
        Assert.Equal(260, PaneSizes.Restore(900, 1000, 500, Min, Reserve));
    }

    [Fact]
    public void TheShareStopsAtTheMinimum() {
        Assert.Equal(Min, PaneSizes.Restore(130, 1925, 800, Min, Reserve));
    }

    [Fact]
    public void AWindowTooNarrowForBoth_LeavesThePaneAtItsMinimum() {
        // Reserve wins over min nowhere: the pane cannot go below min, and
        // what is left over is the list's problem, not the rule's.
        Assert.Equal(Min, PaneSizes.Restore(300, 1925, 200, Min, Reserve));
    }

    [Fact]
    public void AGrowingWindow_GivesThePaneItsShare() {
        Assert.Equal(400, PaneSizes.Restore(200, 1000, 2000, Min, Reserve));
    }
}
