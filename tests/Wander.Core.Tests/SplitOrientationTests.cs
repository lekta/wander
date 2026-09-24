using Wander.Core.Preview;

namespace Wander.Core.Tests;

public class SplitOrientationTests {
    private static readonly PictureShape _landscape = new(6000, 4000);
    private static readonly PictureShape _portrait = new(4000, 6000);
    private static readonly PictureShape _wide = new(1920, 1080);


    /// <summary>Shapes not known: the old rule - one above the other while the place is taller than it is wide.</summary>
    [Theory]
    [InlineData(500, 1000, true)]
    [InlineData(1000, 1000, true)]
    [InlineData(1000, 500, false)]
    public void UnknownShapes_FollowThePlace(double width, double height, bool stacked) {
        Assert.Equal(stacked, SplitOrientation.Stacked(width, height, null, null, null));
    }

    [Fact]
    public void LandscapePair_InAPlaceWiderThanTall_IsStacked_WhenThatShowsMore() {
        Assert.True(SplitOrientation.Stacked(1400, 1000, _landscape, _landscape, null));
        Assert.True(SplitOrientation.Stacked(1400, 1000, _wide, _wide, null));
    }

    [Fact]
    public void PortraitPair_InAPlaceTallerThanWide_IsSideBySide_WhenThatShowsMore() {
        Assert.False(SplitOrientation.Stacked(900, 1000, _portrait, _portrait, null));
    }

    /// <summary>In a narrow strip even portraits are bigger one above the other; in a wide window even landscapes side by side.</summary>
    [Fact]
    public void ThePlaceStillCounts() {
        Assert.True(SplitOrientation.Stacked(500, 1000, _portrait, _portrait, null));
        Assert.False(SplitOrientation.Stacked(2000, 800, _landscape, _landscape, null));
    }

    [Fact]
    public void AMixedPair_WeighsBoth() {
        Assert.True(SplitOrientation.Stacked(900, 1000, _landscape, _portrait, null));
        Assert.False(SplitOrientation.Stacked(1100, 1000, _landscape, _portrait, null));
    }

    /// <summary>Near the point where both ways are even, a split on screen stays the way it is.</summary>
    [Fact]
    public void NearEven_ASplitOnScreenStays() {
        // 3:2 pictures are even at a place of 3:2; a little either side of it
        // the better way wins by less than TurnAbove.
        Assert.True(SplitOrientation.Stacked(1550, 1000, _landscape, _landscape, stacked: true));
        Assert.False(SplitOrientation.Stacked(1450, 1000, _landscape, _landscape, stacked: false));

        // Clearly better the other way: it turns.
        Assert.False(SplitOrientation.Stacked(2400, 1000, _landscape, _landscape, stacked: true));
        Assert.True(SplitOrientation.Stacked(900, 1000, _landscape, _landscape, stacked: false));
    }

    [Fact]
    public void NoPlaceYet_KeepsWhatIsThere() {
        Assert.False(SplitOrientation.Stacked(0, 0, _landscape, _landscape, stacked: false));
        Assert.True(SplitOrientation.Stacked(0, 0, _landscape, _landscape, stacked: null));
    }
}
