using Wander.Core.Imaging;

namespace Wander.Core.Tests.Imaging;

public class ExifOrientationTests {
    /// <summary>
    /// The turn moves a point of the stored frame where the autofocus areas
    /// go for the same value - two independent readings of the EXIF table.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    public void Upright_TurnsThePictureAsTheAreasTurn(int orientation) {
        var stored = new AfPoint(0.2, 0.1, 0.1, 0.1, InFocus: true);
        var expected = Assert.Single(AfGeometry.Orient(new[] { stored }, orientation));

        var (mirror, clockwise) = ExifOrientation.Upright(orientation);
        double x = mirror ? 1 - stored.X : stored.X;
        double y = stored.Y;
        (x, y) = clockwise switch {
            90 => (1 - y, x),
            180 => (1 - x, 1 - y),
            270 => (y, 1 - x),
            _ => (x, y),
        };

        Assert.Equal(expected.X, x, 9);
        Assert.Equal(expected.Y, y, 9);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(9)]
    public void Upright_LeavesUnknownValuesAlone(int? orientation) {
        Assert.Equal((false, 0), ExifOrientation.Upright(orientation));
    }
}
