using Wander.Core.Preview;

namespace Wander.Core.Tests;

public class ZoomLinkTests {
    [Fact]
    public void Together_TheOtherShowsTheSamePlace() {
        var link = new ZoomLink();

        AssertAt((0.3, 0.4), link.Lead(fromFirst: true, 0.3, 0.4, alone: false));
        AssertAt((0.7, 0.1), link.Lead(fromFirst: false, 0.7, 0.1, alone: false));
    }

    /// <summary>The right button held: the other stands still, and what lies between the two is kept once it is let go.</summary>
    [Fact]
    public void RightButton_TheOtherStays_AndTheShiftIsKept() {
        var link = new ZoomLink();
        link.Lead(fromFirst: true, 0.5, 0.5, alone: false);

        Assert.Null(link.Lead(fromFirst: true, 0.6, 0.45, alone: true));
        Assert.Null(link.Lead(fromFirst: true, 0.62, 0.44, alone: true));

        // The second stayed at (0.5, 0.5): it now stands 0.12 left of and 0.06 below the first.
        AssertAt((0.58, 0.56), link.Lead(fromFirst: true, 0.7, 0.5, alone: false));
    }

    [Fact]
    public void TheSecondLeading_TheShiftWorksTheOtherWay() {
        var link = new ZoomLink();
        link.Lead(fromFirst: true, 0.5, 0.5, alone: false);
        link.Lead(fromFirst: true, 0.4, 0.5, alone: true);

        // The second is 0.1 right of the first; led from the second, the first follows 0.1 left of it.
        AssertAt((0.2, 0.3), link.Lead(fromFirst: false, 0.3, 0.3, alone: false));
    }

    [Fact]
    public void TheSecondAlone_MovesTheShiftToo() {
        var link = new ZoomLink();
        link.Lead(fromFirst: false, 0.5, 0.5, alone: false);

        Assert.Null(link.Lead(fromFirst: false, 0.55, 0.6, alone: true));

        AssertAt((0.35, 0.3), link.Lead(fromFirst: true, 0.3, 0.2, alone: false));
    }

    /// <summary>The first move of a zoom puts the other somewhere even with the right button already down.</summary>
    [Fact]
    public void RightButtonFromTheStart_TheOtherIsStillPut() {
        var link = new ZoomLink();

        AssertAt((0.2, 0.2), link.Lead(fromFirst: true, 0.2, 0.2, alone: true));
        Assert.Null(link.Lead(fromFirst: true, 0.3, 0.2, alone: true));
    }

    /// <summary>Lined up once, a pair stays lined up from one zoom to the next.</summary>
    [Fact]
    public void TheShiftOutlivesTheZoom_UntilReset() {
        var link = new ZoomLink();
        link.Lead(fromFirst: true, 0.5, 0.5, alone: false);
        link.Lead(fromFirst: true, 0.4, 0.4, alone: true);
        link.End();

        AssertAt((0.6, 0.6), link.Lead(fromFirst: true, 0.5, 0.5, alone: false));

        link.Reset();

        AssertAt((0.5, 0.5), link.Lead(fromFirst: true, 0.5, 0.5, alone: false));
    }

    /// <summary>A place past an edge is kept as it is: the pane clamps what it shows, the shift is not bent.</summary>
    [Fact]
    public void PastAnEdge_ThePlaceIsNotClamped() {
        var link = new ZoomLink();
        link.Lead(fromFirst: true, 0.9, 0.5, alone: false);
        link.Lead(fromFirst: true, 0.7, 0.5, alone: true);

        AssertAt((1.2, 0.5), link.Lead(fromFirst: true, 1.0, 0.5, alone: false));
    }


    private static void AssertAt((double X, double Y) expected, (double X, double Y)? actual) {
        Assert.NotNull(actual);
        Assert.Equal(expected.X, actual!.Value.X, 9);
        Assert.Equal(expected.Y, actual.Value.Y, 9);
    }
}
