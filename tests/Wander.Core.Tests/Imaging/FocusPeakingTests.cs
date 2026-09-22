using Wander.Core.Imaging;

namespace Wander.Core.Tests.Imaging;

public class FocusPeakingTests {
    private const int W = 64;
    private const int H = 48;


    [Fact]
    public void FlatField_MarksNothing() {
        var mask = FocusPeaking.Mask(Sharpness.Crispness(Synthetic.Flat(W, H, 128), W, H));

        Assert.All(mask, m => Assert.Equal(0, m));
    }


    [Fact]
    public void SharpStep_MarksTheStepColumnsOnly() {
        const int at = 30;
        var mask = FocusPeaking.Mask(Sharpness.Crispness(Synthetic.Step(W, H, at), W, H));

        for (int y = 1; y < H - 1; y++) {
            for (int x = 0; x < W; x++) {
                bool onStep = x == at - 1 || x == at;
                Assert.Equal(onStep ? 255 : 0, mask[y * W + x]);
            }
        }
    }


    /// <summary>What the first version got wrong: a soft edge of a contrasty scene is still soft.</summary>
    [Fact]
    public void BlurredStep_MarksNothing() {
        var soft = Synthetic.Soft(Synthetic.Step(W, H, 30), W, H);

        var mask = FocusPeaking.Mask(Sharpness.Crispness(soft, W, H));

        Assert.All(mask, m => Assert.Equal(0, m));
    }


    [Fact]
    public void Border_IsNeverMarked() {
        var mask = FocusPeaking.Mask(Sharpness.Crispness(Synthetic.Step(W, H, 30), W, H));

        for (int x = 0; x < W; x++) {
            Assert.Equal(0, mask[x]);
            Assert.Equal(0, mask[(H - 1) * W + x]);
        }
    }


    [Fact]
    public void Shrink_KeepsASinglePixelMark() {
        var mask = new byte[W * H];
        mask[21 * W + 37] = 255;

        var small = FocusPeaking.Shrink(mask, W, H, W / 8, H / 8);

        Assert.Equal(W / 8 * (H / 8), small.Length);
        Assert.Equal(255, small[21 / 8 * (W / 8) + 37 / 8]);
        Assert.Equal(1, small.Count(m => m != 0));
    }


    [Fact]
    public void Shrink_EmptyStaysEmpty() {
        Assert.All(FocusPeaking.Shrink(new byte[W * H], W, H, 20, 15), m => Assert.Equal(0, m));
    }
}
