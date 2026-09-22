using Wander.Core.Imaging;

namespace Wander.Core.Tests.Imaging;

public class SharpnessTests {
    private const int W = 128;
    private const int H = 128;
    private const int At = 64;


    [Fact]
    public void Crispness_FlatField_HasNoEdges() {
        Assert.All(Sharpness.Crispness(Synthetic.Flat(W, H, 90), W, H), c => Assert.Equal(0, c));
    }


    /// <summary>The point of the measure: a sharp edge is crisp however faint, as long as it is an edge at all.</summary>
    [Theory]
    [InlineData(40, 200)]
    [InlineData(100, 140)]
    public void Crispness_SharpStep_IsCrisp_WhateverTheContrast(byte dark, byte light) {
        var crisp = Sharpness.Crispness(Synthetic.Step(W, H, At, dark, light), W, H);

        Assert.True(crisp[H / 2 * W + At] >= 3.9 * Sharpness.Unit);
        Assert.True(crisp[H / 2 * W + At - 1] >= 3.9 * Sharpness.Unit);
    }


    [Fact]
    public void Crispness_StepUnderTheContrastFloor_IsNoEdge() {
        var crisp = Sharpness.Crispness(Synthetic.Step(W, H, At, 100, 115), W, H);

        Assert.All(crisp, c => Assert.Equal(0, c));
    }


    [Fact]
    public void Crispness_BlurredStep_IsSoft() {
        var soft = Synthetic.Soft(Synthetic.Step(W, H, At), W, H);

        var crisp = Sharpness.Crispness(soft, W, H);

        Assert.All(crisp, c => Assert.True(c < 2 * Sharpness.Unit));
        Assert.Contains(crisp, c => c > 0);
    }


    [Fact]
    public void Score_SharpStep_IsFull_BlurredLower_FlatNone() {
        var sharp = Synthetic.Step(W, H, At);
        var soft = Synthetic.Soft(sharp, W, H);

        double? crisp = Sharpness.Score(Sharpness.Crispness(sharp, W, H), W, H);
        double? blurred = Sharpness.Score(Sharpness.Crispness(soft, W, H), W, H);

        Assert.Equal(100, crisp);
        Assert.NotNull(blurred);
        Assert.True(blurred < crisp, $"blurred {blurred} >= sharp {crisp}");
        Assert.Null(Sharpness.Score(Sharpness.Crispness(Synthetic.Flat(W, H, 90), W, H), W, H));
    }


    /// <summary>Only the area counts: an edge outside it is not the area's sharpness.</summary>
    [Fact]
    public void Score_LooksOnlyInsideTheArea() {
        var crisp = Sharpness.Crispness(Synthetic.Step(W, H, 8), W, H);

        Assert.Null(Sharpness.Score(crisp, W, H, new RectI(W / 4, H / 4, W / 2, H / 2)));
        Assert.Equal(100, Sharpness.Score(crisp, W, H, new RectI(0, 0, W / 2, H)));
    }


    [Fact]
    public void Area_WithoutAf_IsTheMiddleHalf() {
        Assert.Equal(new RectI(100, 50, 200, 100), Sharpness.Area(400, 200, null));
        Assert.Equal(new RectI(100, 50, 200, 100), Sharpness.Area(400, 200, Array.Empty<AfPoint>()));
    }


    [Fact]
    public void Area_IsTwiceTheFocusedAfArea_ClippedAtTheFrame() {
        var af = new[] {
            new AfPoint(0.1, 0.1, 0.1, 0.1, InFocus: false),
            new AfPoint(0.5, 0.25, 0.1, 0.2, InFocus: true),
        };

        Assert.Equal(new RectI(160, 10, 80, 80), Sharpness.Area(400, 200, af));
        Assert.Equal(new RectI(0, 0, 80, 40), Sharpness.Area(400, 200, new[] { af[0] }));
    }
}
