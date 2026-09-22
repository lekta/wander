using Wander.Core.Imaging;

namespace Wander.Core.Tests.Imaging;

public class FocusPeakingTests {
    private const int W = 64;
    private const int H = 48;


    [Fact]
    public void FlatField_MarksNothing() {
        var mask = FocusPeaking.Mask(Sharpness.Measure(Synthetic.Flat(W, H, 128), W, H));

        Assert.All(mask, m => Assert.Equal(0, m));
    }


    [Fact]
    public void SharpStep_MarksTheStepColumnsOnly() {
        const int at = 30;
        var mask = FocusPeaking.Mask(Sharpness.Measure(Synthetic.Step(W, H, at), W, H));

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

        var mask = FocusPeaking.Mask(Sharpness.Measure(soft, W, H));

        Assert.All(mask, m => Assert.Equal(0, m));
    }


    [Fact]
    public void Border_IsNeverMarked() {
        var mask = FocusPeaking.Mask(Sharpness.Measure(Synthetic.Step(W, H, 30), W, H));

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

    // --- Specks and lines ----------------------------------------------

    /// <summary>A mark on its own is dust, noise or a point of light - not an edge.</summary>
    [Fact]
    public void Continuous_DropsWhatIsTooSmall() {
        var mask = new byte[W * H];
        mask[10 * W + 10] = 255;
        mask[20 * W + 20] = 255;
        mask[20 * W + 21] = 255;

        Assert.All(FocusPeaking.Continuous(mask, W, H), m => Assert.Equal(0, m));
    }


    [Fact]
    public void Continuous_KeepsALine_AtFullWeight() {
        var mask = new byte[W * H];
        for (int x = 10; x < 30; x++) {
            mask[15 * W + x] = 255;
        }

        var kept = FocusPeaking.Continuous(mask, W, H);

        Assert.Equal(255, kept[15 * W + 20]);
        Assert.Equal(20, kept.Count(m => m != 0));
    }


    /// <summary>A round patch - a snowflake, a blown-out point of light - counts for less.</summary>
    [Fact]
    public void Continuous_WeighsARoundPatchDown() {
        var mask = new byte[W * H];
        for (int y = 20; y < 24; y++) {
            for (int x = 20; x < 24; x++) {
                mask[y * W + x] = 255;
            }
        }

        var kept = FocusPeaking.Continuous(mask, W, H);

        Assert.Equal(64, kept[21 * W + 21]);
    }


    /// <summary>A big patch is a textured surface, not a speck, whatever its shape.</summary>
    [Fact]
    public void Continuous_KeepsABigPatch_AtFullWeight() {
        var mask = new byte[W * H];
        for (int y = 5; y < 25; y++) {
            for (int x = 5; x < 25; x++) {
                mask[y * W + x] = 255;
            }
        }

        Assert.Equal(255, FocusPeaking.Continuous(mask, W, H)[15 * W + 15]);
    }
}
