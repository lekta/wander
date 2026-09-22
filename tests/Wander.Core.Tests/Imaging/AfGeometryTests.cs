using Wander.Core.Imaging;

namespace Wander.Core.Tests.Imaging;

public class AfGeometryTests {
    /// <summary>
    /// One area near the top left of the stored frame, tall and narrow, and
    /// where each EXIF orientation puts it on the upright picture.
    /// </summary>
    [Theory]
    [InlineData(null, 0.2, 0.1, 0.1, 0.3)]
    [InlineData(1, 0.2, 0.1, 0.1, 0.3)]
    [InlineData(2, 0.8, 0.1, 0.1, 0.3)]
    [InlineData(3, 0.8, 0.9, 0.1, 0.3)]
    [InlineData(4, 0.2, 0.9, 0.1, 0.3)]
    [InlineData(5, 0.1, 0.2, 0.3, 0.1)]
    [InlineData(6, 0.9, 0.2, 0.3, 0.1)]
    [InlineData(7, 0.9, 0.8, 0.3, 0.1)]
    [InlineData(8, 0.1, 0.8, 0.3, 0.1)]
    public void Orient_PutsTheAreaWhereThePictureTurns(int? orientation, double x, double y, double w, double h) {
        var stored = new AfPoint(0.2, 0.1, 0.1, 0.3, InFocus: true);

        var upright = Assert.Single(AfGeometry.Orient(new[] { stored }, orientation));

        Assert.Equal(x, upright.X, 9);
        Assert.Equal(y, upright.Y, 9);
        Assert.Equal(w, upright.W, 9);
        Assert.Equal(h, upright.H, 9);
        Assert.True(upright.InFocus);
    }


    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    public void Restore_UndoesOrient(int orientation) {
        var stored = new AfPoint(0.2, 0.1, 0.1, 0.3, InFocus: false);

        var back = Assert.Single(AfGeometry.Restore(AfGeometry.Orient(new[] { stored }, orientation), orientation));

        Assert.Equal(stored.X, back.X, 9);
        Assert.Equal(stored.Y, back.Y, 9);
        Assert.Equal(stored.W, back.W, 9);
        Assert.Equal(stored.H, back.H, 9);
    }
}
