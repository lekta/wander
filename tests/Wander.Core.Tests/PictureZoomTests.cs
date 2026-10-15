using Wander.Core.Preview;

namespace Wander.Core.Tests;

/// <summary>Ctrl+wheel's levels past the fitted picture, and Ctrl+arrow's move of the view.</summary>
public class PictureZoomTests {
    [Theory]
    // A 45-megapixel frame in a pane: fitted at a fifth.
    [InlineData(null, 0.2, 1, 0.5)]
    [InlineData(0.5, 0.2, 1, 1.0)]
    [InlineData(1.0, 0.2, 2, 4.0)]
    [InlineData(4.0, 0.2, 1, 4.0)]
    [InlineData(0.5, 0.2, -1, null)]
    [InlineData(null, 0.2, -1, null)]
    // Fitted at 0.6: 50 % is smaller than what is on show - no step.
    [InlineData(null, 0.6, 1, 1.0)]
    [InlineData(1.0, 0.6, -1, null)]
    // A small picture, not shrunk: up to 200 % and 400 %.
    [InlineData(null, 1.0, 1, 2.0)]
    [InlineData(2.0, 1.0, -1, null)]
    // Held at 1:1 by the button, the wheel goes on from there.
    [InlineData(1.0, 0.3, 1, 2.0)]
    public void Step_GoesAlongTheLevelsPastTheFit(double? current, double fit, int notches, double? expected) {
        Assert.Equal(expected, PictureZoom.Step(current, fit, notches));
    }

    [Fact]
    public void Nudge_MovesAThirdOfTheView_AndStopsAtTheEdges() {
        // A 3000-wide picture in a 1000-wide view: 2000 to pan, a third of the view is 1/6 of it.
        Assert.Equal(0.5 + 1.0 / 6, PictureZoom.Nudge(0.5, 1000, 3000, +1), 9);
        Assert.Equal(0.5 - 1.0 / 6, PictureZoom.Nudge(0.5, 1000, 3000, -1), 9);
        Assert.Equal(1.0, PictureZoom.Nudge(0.95, 1000, 3000, +1));
        Assert.Equal(0.0, PictureZoom.Nudge(0.05, 1000, 3000, -1));
        // An axis the picture fits along does not pan.
        Assert.Equal(0.5, PictureZoom.Nudge(0.5, 1000, 800, +1));
    }
}
