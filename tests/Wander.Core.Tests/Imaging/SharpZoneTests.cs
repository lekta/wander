using Wander.Core.Imaging;

namespace Wander.Core.Tests.Imaging;

public class SharpZoneTests {
    private const int W = 480;
    private const int H = 320;


    [Fact]
    public void Centre_NothingCrisp_IsNone() {
        Assert.Null(SharpZone.Centre(new byte[W * H], W, H));
    }


    [Fact]
    public void Centre_TooFewMarks_IsNone() {
        var marks = new byte[W * H];
        Mark(marks, 100, 100, 5, 5);

        Assert.Null(SharpZone.Centre(marks, W, H, minMarks: 50));
    }


    /// <summary>A sharp patch in the top right among scattered specks: the zone is the patch.</summary>
    [Fact]
    public void Centre_ThickPatch_WinsOverScatteredSpecks() {
        var marks = new byte[W * H];
        for (int y = 5; y < H; y += 40) {
            for (int x = 5; x < W; x += 40) {
                Mark(marks, x, y, 2, 2);
            }
        }
        Mark(marks, 360, 40, 40, 30);

        var (x0, y0) = SharpZone.Centre(marks, W, H)!.Value;

        Assert.InRange(x0 * W, 360, 400);
        Assert.InRange(y0 * H, 40, 70);
    }


    /// <summary>Two sharp places: the one with more crisp edges in it is the zone.</summary>
    [Fact]
    public void Centre_TwoPatches_TheThickerWins() {
        var marks = new byte[W * H];
        Mark(marks, 40, 200, 30, 30);
        Mark(marks, 300, 100, 50, 50);

        var (x0, y0) = SharpZone.Centre(marks, W, H)!.Value;

        Assert.InRange(x0 * W, 300, 350);
        Assert.InRange(y0 * H, 100, 150);
    }


    private static void Mark(byte[] marks, int x, int y, int w, int h) {
        for (int j = y; j < y + h; j++) {
            for (int i = x; i < x + w; i++) {
                marks[j * W + i] = 255;
            }
        }
    }
}
