using Wander.Core.Imaging;

namespace Wander.Core.Tests.Imaging;

public class ClippingTests {
    private const int W = 40;
    private const int H = 30;
    private static readonly RectI _square = new(10, 5, 12, 8);


    [Fact]
    public void WhiteSquare_ClipsAllThreeChannels_InsideOnly() {
        var flags = Clipping.Mask(Synthetic.Square(W, H, _square, inside: 255));

        AssertInsideOnly(flags, Clipping.Red | Clipping.Green | Clipping.Blue);
    }


    [Fact]
    public void BlackSquare_IsDark_InsideOnly() {
        var flags = Clipping.Mask(Synthetic.Square(W, H, _square, inside: 0));

        AssertInsideOnly(flags, Clipping.Dark);
    }


    [Fact]
    public void OneChannelHigh_FlagsThatChannelAlone() {
        var image = Synthetic.Square(W, H, _square, inside: 128);
        image.Pixels[0 + 2] = 252;

        var flags = Clipping.Mask(image);

        Assert.Equal(Clipping.Red, flags[0]);
    }


    private static void AssertInsideOnly(byte[] flags, int expected) {
        for (int y = 0; y < H; y++) {
            for (int x = 0; x < W; x++) {
                bool inside = x >= _square.X && x < _square.X + _square.Width && y >= _square.Y && y < _square.Y + _square.Height;
                Assert.Equal(inside ? expected : 0, flags[y * W + x]);
            }
        }
    }
}
