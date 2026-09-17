using Wander.Core.Icons;

namespace Wander.Core.Tests;

public class ShotSummaryTests {
    [Fact]
    public void AValueEverybodyShares_IsListedOnce() {
        var summary = ShotSummary.Aggregate(new[] { Shot(iso: "100"), Shot(iso: "100"), Shot(iso: "100") });

        Assert.Equal(3, summary.Shots);
        Assert.Equal(new[] { "100" }, summary.Iso);
        Assert.Equal(new[] { "50 mm" }, summary.FocalLengths);
    }

    [Fact]
    public void TwoOrThreeValues_AreListedInTheOrderTheyWereMet() {
        var summary = ShotSummary.Aggregate(new[] {
            Shot(shutter: "1/500 sec"), Shot(shutter: "1/2000 sec"), Shot(shutter: "1/500 sec"), Shot(shutter: "1/125 sec"),
        });

        Assert.Equal(new[] { "1/500 sec", "1/2000 sec", "1/125 sec" }, summary.Shutters);
    }

    [Fact]
    public void MoreValuesThanTheLimit_LeaveTheFieldOut() {
        var summary = ShotSummary.Aggregate(new[] {
            Shot(iso: "100"), Shot(iso: "200"), Shot(iso: "400"), Shot(iso: "800"),
        });

        Assert.Empty(summary.Iso);
        // The other fields are untouched by one field varying.
        Assert.Equal(new[] { "f/2.8" }, summary.Apertures);
    }

    [Fact]
    public void APictureWithoutTheField_DoesNotVote() {
        var summary = ShotSummary.Aggregate(new[] { Shot(iso: "100"), Shot(iso: null) });

        Assert.Equal(new[] { "100" }, summary.Iso);
    }

    [Fact]
    public void NobodyHasTheField_NothingIsListed() {
        var summary = ShotSummary.Aggregate(new[] { Shot(iso: null), Shot(iso: null) });

        Assert.Empty(summary.Iso);
    }

    [Fact]
    public void Camera_IsMakeAndModelTogether() {
        var summary = ShotSummary.Aggregate(new[] { Shot(), Shot(make: "Nikon", model: "Z6") });

        Assert.Equal(new[] { "Canon EOS R5", "Nikon Z6" }, summary.Cameras);
    }

    [Theory]
    [InlineData("Canon", "Canon EOS R8", "Canon EOS R8")]
    [InlineData("NIKON CORPORATION", "NIKON Z 6", "NIKON Z 6")]
    [InlineData("SONY", "ILCE-7M3", "SONY ILCE-7M3")]
    [InlineData("Canon", "Canonet", "Canon Canonet")]
    [InlineData(" Canon ", null, "Canon")]
    [InlineData(null, "EOS R8", "EOS R8")]
    [InlineData(null, " ", null)]
    public void CameraName_DoesNotSayTheMakeTwice(string? make, string? model, string? expected) {
        Assert.Equal(expected, ShotSummary.CameraName(Shot(make: make, model: model)));
    }

    [Fact]
    public void PixelSize_CountsAsOneValue() {
        var summary = ShotSummary.Aggregate(new[] { Shot(), Shot(width: 4000, height: 6000), Shot() });

        Assert.Equal(new[] { new PixelSize(6000, 4000), new PixelSize(4000, 6000) }, summary.PixelSizes);
    }

    [Fact]
    public void EverythingVaries_IsEmpty() {
        var shots = Enumerable.Range(0, 5)
            .Select(i => Shot(make: "M" + i, model: null, iso: i.ToString(), aperture: "f/" + i, shutter: i + " s",
                focal: i + " mm", width: i, height: i))
            .ToArray();

        Assert.True(ShotSummary.Aggregate(shots).IsEmpty);
        Assert.Equal(5, ShotSummary.Aggregate(shots).Shots);
    }


    private static ImageMetadata Shot(
        string? make = "Canon", string? model = "EOS R5", string? iso = "100", string? aperture = "f/2.8",
        string? shutter = "1/500 sec", string? focal = "50 mm", int? width = 6000, int? height = 4000) {
        return new ImageMetadata(make, model, iso, aperture, shutter, focal, null, width, height, null);
    }
}
