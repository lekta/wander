using Wander.Core.Preview;

namespace Wander.Core.Tests;

public class SoundTileTests {
    [Fact]
    public void Button_SitsInsideTheBandAtItsLeft() {
        const double page = 200;
        var (top, height) = SoundTile.Band(page);
        var (x, y, r) = SoundTile.Button(page);

        Assert.Equal(page, top + height, 6);
        Assert.InRange(y - r, top, top + height);
        Assert.InRange(y + r, top, top + height);
        Assert.Equal(x - r, top + height - (y + r), 6);
        Assert.True(SoundTile.LengthLeft(page) > x + r);
    }

    [Fact]
    public void ButtonOnTile_IsThePageButtonMovedByTheEdge() {
        const double side = 240;
        double edge = side / 12;
        var (px, py, pr) = SoundTile.Button(side - edge * 2);
        var (x, y, r) = SoundTile.ButtonOnTile(side);

        Assert.Equal(px + edge, x, 6);
        Assert.Equal(py + edge, y, 6);
        Assert.Equal(pr, r, 6);
    }

    [Fact]
    public void ButtonOnTile_SmallIconIsCovered() {
        var (x, y, r) = SoundTile.ButtonOnTile(16);

        Assert.Equal(8, x);
        Assert.Equal(8, y);
        Assert.True(r >= 5.5);
    }
}
