using Wander.Core.Imaging;

namespace Wander.Core.Tests.Imaging;

public class ToneCurveTests {
    [Fact]
    public void GammaOne_IsIdentity() {
        var lut = ToneCurve.Lut(1.0);

        for (int i = 0; i < 256; i++) {
            Assert.Equal(i, lut[i]);
        }
    }


    [Theory]
    [InlineData(ToneCurve.Shadows)]
    [InlineData(ToneCurve.Highlights)]
    public void Curve_IsMonotone_AndKeepsBlackAndWhite(double gamma) {
        var lut = ToneCurve.Lut(gamma);

        for (int i = 1; i < 256; i++) {
            Assert.True(lut[i] >= lut[i - 1], $"lut[{i}] < lut[{i - 1}]");
        }
        Assert.Equal(0, lut[0]);
        Assert.Equal(255, lut[255]);
    }


    [Fact]
    public void Shadows_Lift_Highlights_Lower_TheMiddle() {
        Assert.True(ToneCurve.Lut(ToneCurve.Shadows)[64] > 64);
        Assert.True(ToneCurve.Lut(ToneCurve.Highlights)[192] < 192);
    }


    [Fact]
    public void Apply_ChangesColours_KeepsAlpha() {
        var image = Synthetic.Square(4, 4, new RectI(0, 0, 2, 2), inside: 64);
        image.Pixels[3] = 77;

        var toned = ToneCurve.Apply(image, ToneCurve.Lut(ToneCurve.Shadows));

        Assert.True(toned.Pixels[0] > 64);
        Assert.Equal(77, toned.Pixels[3]);
        Assert.Equal(64, image.Pixels[0]);
    }
}
