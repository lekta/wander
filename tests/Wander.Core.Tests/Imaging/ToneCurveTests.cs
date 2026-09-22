using Wander.Core.Imaging;

namespace Wander.Core.Tests.Imaging;

public class ToneCurveTests {
    [Fact]
    public void Shadows_LiftTheDarkEnd() {
        var lut = ToneCurve.Shadows();

        Assert.True(lut[16] > 40, $"deep shadow only reached {lut[16]}");
        Assert.True(lut[64] > 90, $"shadow only reached {lut[64]}");
    }


    [Fact]
    public void Highlights_PullTheBrightEnd() {
        var lut = ToneCurve.Highlights();

        Assert.True(lut[239] < 215, $"bright highlight only reached {lut[239]}");
        Assert.True(lut[191] < 165, $"highlight only reached {lut[191]}");
    }


    /// <summary>
    /// What a plain gamma got wrong: it moved the middle, and the frame
    /// read as a different exposure rather than as the same one opened up.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheMiddle_BarelyMoves(bool shadows) {
        var lut = shadows ? ToneCurve.Shadows() : ToneCurve.Highlights();

        Assert.True(Math.Abs(lut[128] - 128) <= 10, $"middle moved to {lut[128]}");
    }


    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Curve_IsMonotone_AndKeepsBlackAndWhite(bool shadows) {
        var lut = shadows ? ToneCurve.Shadows() : ToneCurve.Highlights();

        for (int i = 1; i < 256; i++) {
            Assert.True(lut[i] >= lut[i - 1], $"lut[{i}] < lut[{i - 1}]");
        }
        Assert.Equal(0, lut[0]);
        Assert.Equal(255, lut[255]);
    }


    /// <summary>Each curve works at its own end and leaves the other one where it was.</summary>
    [Fact]
    public void EachCurve_LeavesTheOtherEndAlone() {
        Assert.True(Math.Abs(ToneCurve.Shadows()[239] - 239) <= 4);
        Assert.True(Math.Abs(ToneCurve.Highlights()[16] - 16) <= 4);
    }


    [Fact]
    public void NoAmount_IsIdentity() {
        var lut = ToneCurve.Shadows(0);

        for (int i = 0; i < 256; i++) {
            Assert.Equal(i, lut[i]);
        }
    }


    [Fact]
    public void Apply_ChangesColours_KeepsAlpha() {
        var image = Synthetic.Square(4, 4, new RectI(0, 0, 2, 2), inside: 64);
        image.Pixels[3] = 77;

        var toned = ToneCurve.Apply(image, ToneCurve.Shadows());

        Assert.True(toned.Pixels[0] > 64);
        Assert.Equal(77, toned.Pixels[3]);
        Assert.Equal(64, image.Pixels[0]);
    }
}
