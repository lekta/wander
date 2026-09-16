using Wander.Core.Icons;

namespace Wander.Core.Tests;

public class GeoPositionTests {
    [Fact]
    public void Dms_SplitsACoordinate() {
        var (d, m, s) = GeoPosition.Dms(55.753429);

        Assert.Equal(55, d);
        Assert.Equal(45, m);
        Assert.Equal(12.3444, s, 3);
    }

    [Fact]
    public void Dms_IsUnsigned_TheSignIsTheReference() {
        var (d, m, s) = GeoPosition.Dms(-37.634842);

        Assert.Equal(37, d);
        Assert.Equal(38, m);
        Assert.Equal(5.4312, s, 3);
        Assert.Equal("W", GeoPosition.ReferenceOf(-37.634842, isLatitude: false));
        Assert.Equal("S", GeoPosition.ReferenceOf(-1, isLatitude: true));
        Assert.Equal("N", GeoPosition.ReferenceOf(0, isLatitude: true));
        Assert.Equal("E", GeoPosition.ReferenceOf(37.6, isLatitude: false));
    }

    [Fact]
    public void Dms_CarriesARoundedSixtyUp() {
        // 10 + 59/60 + 59.99999/3600: the seconds round to 60 and must carry.
        var (d, m, s) = GeoPosition.Dms(10 + 59.0 / 60 + 59.99999 / 3600);

        Assert.Equal(11, d);
        Assert.Equal(0, m);
        Assert.Equal(0, s);
    }

    [Fact]
    public void Dms_WholeDegrees() {
        Assert.Equal((12, 0, 0.0), GeoPosition.Dms(12));
    }
}
