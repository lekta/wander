using Wander.Core.Imaging;

namespace Wander.Core.Tests.Imaging;

public class LumaTests {
    private const int W = 32;
    private const int H = 32;


    [Fact]
    public void Of_IsRec601_AndWhiteStaysWhite() {
        Assert.Equal(255, Luma.Of(255, 255, 255));
        Assert.Equal(0, Luma.Of(0, 0, 0));
        Assert.True(Luma.Of(0, 255, 0) > Luma.Of(255, 0, 0));
    }


    /// <summary>A grain of sensor noise is a crisp little edge; the median takes it out.</summary>
    [Fact]
    public void Denoise_TakesOutASinglePixel() {
        var luma = Synthetic.Flat(W, H, 80);
        luma[10 * W + 10] = 250;

        var quiet = Luma.Denoise(luma, W, H);

        Assert.Equal(80, quiet[10 * W + 10]);
    }


    /// <summary>And leaves the step where it was: that is what a median does and a blur does not.</summary>
    [Fact]
    public void Denoise_KeepsAStep() {
        var step = Synthetic.Step(W, H, 16);

        var quiet = Luma.Denoise(step, W, H);

        for (int x = 1; x < W - 1; x++) {
            Assert.Equal(step[16 * W + x], quiet[16 * W + x]);
        }
    }
}
