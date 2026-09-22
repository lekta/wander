using Wander.Core.Imaging;

namespace Wander.Core.Tests.Imaging;

public class HistogramTests {
    private const int W = 40;
    private const int H = 30;


    [Fact]
    public void EveryChannel_SumsToThePixelCount() {
        var data = Histogram.Compute(Synthetic.Square(W, H, new RectI(5, 5, 10, 10), inside: 255, outside: 30));

        Assert.Equal(W * H, data.Pixels);
        Assert.Equal(W * H, data.Red.Sum());
        Assert.Equal(W * H, data.Green.Sum());
        Assert.Equal(W * H, data.Blue.Sum());
        Assert.Equal(W * H, data.Luma.Sum());
    }


    [Fact]
    public void Shares_CountWhiteAndBlack() {
        var white = Histogram.Compute(Synthetic.Square(W, H, new RectI(0, 0, 10, 12), inside: 255));
        var black = Histogram.Compute(Synthetic.Square(W, H, new RectI(0, 0, 20, 12), inside: 0));

        Assert.Equal(120.0 / (W * H), white.Over, 6);
        Assert.Equal(0, white.Under);
        Assert.Equal(240.0 / (W * H), black.Under, 6);
        Assert.Equal(0, black.Over);
    }
}
