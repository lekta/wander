using Wander.Core.Imaging;

namespace Wander.Core.Tests.Imaging;

public class SharpnessTests {
    private const int W = 128;
    private const int H = 128;
    private const int At = 64;


    [Fact]
    public void Measure_FlatField_HasNoEdges() {
        Assert.All(Sharpness.Measure(Synthetic.Flat(W, H, 90), W, H).Values, c => Assert.Equal(0, c));
    }


    /// <summary>The point of the measure: a sharp edge is crisp however faint, as long as it is an edge at all.</summary>
    [Theory]
    [InlineData(40, 200)]
    [InlineData(100, 140)]
    public void Measure_SharpStep_IsCrisp_WhateverTheContrast(byte dark, byte light) {
        var map = Sharpness.Measure(Synthetic.Step(W, H, At, dark, light), W, H);

        Assert.True(map.Values[H / 2 * W + At] >= 3.9 * Sharpness.Unit);
        Assert.True(map.Values[H / 2 * W + At - 1] >= 3.9 * Sharpness.Unit);
    }


    [Fact]
    public void Measure_StepUnderTheContrastFloor_IsNoEdge() {
        var map = Sharpness.Measure(Synthetic.Step(W, H, At, 100, 115), W, H);

        Assert.All(map.Values, c => Assert.Equal(0, c));
    }


    [Fact]
    public void Measure_BlurredStep_IsSoft() {
        var soft = Synthetic.Soft(Synthetic.Step(W, H, At), W, H);

        var map = Sharpness.Measure(soft, W, H);

        Assert.All(map.Values, c => Assert.True(c < 2 * Sharpness.Unit));
        Assert.Contains(map.Values, c => c > 0);
    }


    /// <summary>
    /// A coarser map is the same measurement asked about fewer points: the
    /// step's own pixels answer the same way.
    /// </summary>
    [Fact]
    public void Measure_WithAStep_IsSmaller_AndAgrees() {
        var luma = Synthetic.Step(W, H, At);

        var fine = Sharpness.Measure(luma, W, H);
        var coarse = Sharpness.Measure(luma, W, H, step: 2);

        Assert.Equal(W / 2, coarse.Width);
        Assert.Equal(H / 2, coarse.Height);
        for (int my = 1; my < coarse.Height - 1; my++) {
            for (int mx = 1; mx < coarse.Width - 1; mx++) {
                Assert.Equal(fine.Values[my * 2 * W + mx * 2], coarse.Values[my * coarse.Width + mx]);
            }
        }
    }


    [Fact]
    public void Score_SharpStep_IsFull_BlurredLower_FlatNone() {
        var sharp = Synthetic.Step(W, H, At);
        var soft = Synthetic.Soft(sharp, W, H);

        double? crisp = Score(sharp);
        double? blurred = Score(soft);

        Assert.Equal(100, crisp);
        Assert.NotNull(blurred);
        Assert.True(blurred < crisp, $"blurred {blurred} >= sharp {crisp}");
        Assert.Null(Score(Synthetic.Flat(W, H, 90)));
    }


    /// <summary>
    /// A frame of crisp specks - stars, falling snow, the noise of a sensor
    /// decode - is not a frame in focus, and scores below one with edges.
    /// </summary>
    [Fact]
    public void Score_Specks_CountForLessThanEdges() {
        double? dots = Score(Synthetic.Dots(W, H, spacing: 6));
        double? step = Score(Synthetic.Step(W, H, At));

        Assert.NotNull(dots);
        Assert.True(dots < step, $"dots {dots} >= step {step}");
    }


    /// <summary>Only the area counts: an edge outside it is not the area's sharpness.</summary>
    [Fact]
    public void Score_LooksOnlyInsideTheArea() {
        var luma = Synthetic.Step(W, H, 8);
        var map = Sharpness.Measure(luma, W, H);
        var crisp = FocusPeaking.Continuous(FocusPeaking.Mask(map), map.Width, map.Height);

        Assert.Null(Sharpness.Score(map, crisp, new RectI(W / 4, H / 4, W / 2, H / 2)));
        Assert.Equal(100, Sharpness.Score(map, crisp, new RectI(0, 0, W / 2, H)));
    }


    [Fact]
    public void Area_IsTheMiddleHalf() {
        Assert.Equal(new RectI(100, 50, 200, 100), Sharpness.Area(400, 200));
    }


    private static double? Score(byte[] luma) {
        var map = Sharpness.Measure(luma, W, H);

        return Sharpness.Score(map, FocusPeaking.Continuous(FocusPeaking.Mask(map), map.Width, map.Height));
    }
}
