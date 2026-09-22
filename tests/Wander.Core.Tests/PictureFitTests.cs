using Wander.Core.Imaging;

namespace Wander.Core.Tests;

public class PictureFitTests {
    [Fact]
    public void LandscapeInANarrowPane_DecodedToThePaneWidth() {
        Assert.Equal(1000, PictureFit.DecodeWidth(6000, 4000, 1, 1000, 1000));
    }

    [Fact]
    public void TallPane_HeightDecides() {
        // 6000x4000 into 3000x1000: height allows a sixth of the frame.
        Assert.Equal(1500, PictureFit.DecodeWidth(6000, 4000, null, 3000, 1000));
    }

    [Fact]
    public void TurnedFrame_MeasuredAsShown() {
        // Stored 6000x4000, shown 4000x6000; a 1000x1000 box allows 1/6 of it.
        Assert.Equal(1000, PictureFit.DecodeWidth(6000, 4000, 6, 1000, 1000));
    }

    [Fact]
    public void FitsAlready_Whole() {
        Assert.Null(PictureFit.DecodeWidth(800, 600, 1, 1000, 1000));
        Assert.Null(PictureFit.DecodeWidth(1000, 800, 1, 980, 1000));
    }

    [Fact]
    public void NoBoxYet_Whole() {
        Assert.Null(PictureFit.DecodeWidth(6000, 4000, 1, 0, 0));
        Assert.Null(PictureFit.DecodeWidth(0, 0, 1, 1000, 1000));
    }

    [Fact]
    public void BoxGrewPastTheCopy_TooSmall() {
        Assert.True(PictureFit.TooSmall(1000, 667, 6000, 4000, 1600, 1600));
        Assert.False(PictureFit.TooSmall(1000, 667, 6000, 4000, 1020, 1020));
    }

    [Fact]
    public void WholeFrame_NeverTooSmall() {
        Assert.False(PictureFit.TooSmall(800, 600, 800, 600, 3000, 3000));
    }
}
