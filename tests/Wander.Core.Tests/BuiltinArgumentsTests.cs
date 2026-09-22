using Wander.Core.Actions;

namespace Wander.Core.Tests;

public class BuiltinArgumentsTests {
    [Fact]
    public void Parse_ReadsPairs_CaseInsensitively_WithSpacesDropped() {
        var args = BuiltinArguments.Parse(" Format = jpeg ; QUALITY=90;maxside=1920 ");

        Assert.Equal("jpeg", args.Get("format"));
        Assert.Equal("90", args.Get("quality"));
        Assert.Equal("1920", args.Get("MaxSide"));
        Assert.Null(args.Get("other"));
    }

    [Fact]
    public void Parse_IgnoresStrayFragments_AndALaterKeyWins() {
        var args = BuiltinArguments.Parse(";;format;=png;quality=50;quality=70;empty=");

        Assert.Null(args.Get("format"));
        Assert.Equal("70", args.Get("quality"));
        Assert.Equal(string.Empty, args.Get("empty"));
    }

    [Theory]
    [InlineData("q=50", 50)]
    [InlineData("q=0", 1)]
    [InlineData("q=250", 100)]
    [InlineData("q=high", 90)]
    [InlineData("", 90)]
    public void GetInt_ClampsANumber_AndFallsBackOtherwise(string text, int expected) {
        Assert.Equal(expected, BuiltinArguments.Parse(text).GetInt("q", 90, 1, 100));
    }


    // --- The picture encoder's options ------------------------------------

    [Theory]
    [InlineData("format=jpeg", ImageTarget.Jpeg)]
    [InlineData("format=JPG", ImageTarget.Jpeg)]
    [InlineData("format=png", ImageTarget.Png)]
    [InlineData("format=bmp", ImageTarget.Bmp)]
    [InlineData("format=tif", ImageTarget.Tiff)]
    [InlineData("format=tiff", ImageTarget.Tiff)]
    [InlineData("format=gif", ImageTarget.Gif)]
    public void Options_KnowTheFiveFormats(string arguments, ImageTarget expected) {
        Assert.Equal(expected, ImageConvertOptions.Parse(arguments).Format);
    }

    [Theory]
    [InlineData("")]
    [InlineData("quality=90")]
    [InlineData("format=webp")]
    public void Options_WithoutAFormatTheEncoderWrites_Fail(string arguments) {
        Assert.Throws<FormatException>(() => ImageConvertOptions.Parse(arguments));
    }

    [Fact]
    public void Options_DefaultToQuality90_AndNoResize() {
        var options = ImageConvertOptions.Parse("format=jpeg");

        Assert.Equal(90, options.Quality);
        Assert.Equal(0, options.MaxSide);
        Assert.Equal((6000, 4000), options.Fit(6000, 4000));
    }

    [Fact]
    public void Presets_ParseToWhatTheyPromise() {
        var shrink = ActionPresets.All.Single(p => p.Id == "preset:image-shrink");

        Assert.Equal(
            new ImageConvertOptions(ImageTarget.Jpeg, 85, 1920),
            ImageConvertOptions.Parse(shrink.Arguments));
        Assert.All(
            ActionPresets.All.Where(p => p.Program == ActionPresets.ImageConvert),
            p => ImageConvertOptions.Parse(p.Arguments));
    }

    [Theory]
    [InlineData("format=jpeg", false, false, false)]
    [InlineData("format=jpeg;source=file", false, false, false)]
    [InlineData("format=jpeg;source=Preview", true, false, true)]
    [InlineData("format=jpeg;source=preview-full", true, true, true)]
    [InlineData("format=jpeg;source=preview;maxside=1920", true, false, false)]
    [InlineData("format=png;source=preview-full", true, true, false)]
    public void Options_FromPreview_KeepTheJpegAsIs_OnlyWhenNothingChangesIt(
        string arguments, bool fromPreview, bool fullSize, bool asIs) {

        var options = ImageConvertOptions.Parse(arguments);

        Assert.Equal(fromPreview, options.FromPreview);
        Assert.Equal(fullSize, options.FullSizePreview);
        Assert.Equal(asIs, options.KeepsPreviewAsIs);
    }

    [Fact]
    public void Options_UnknownSource_Fails() {
        Assert.Throws<FormatException>(() => ImageConvertOptions.Parse("format=jpeg;source=sensor"));
    }

    [Theory]
    [InlineData(6000, 4000, 1920, 1280)]
    [InlineData(4000, 6000, 1280, 1920)]
    [InlineData(1920, 1080, 1920, 1080)]
    [InlineData(800, 600, 800, 600)]
    [InlineData(5000, 3, 1920, 1)]
    public void Fit_BringsTheLongSideDown_KeepsProportions_NeverEnlarges(int w, int h, int expectedW, int expectedH) {
        var options = new ImageConvertOptions(ImageTarget.Jpeg, 85, 1920);

        Assert.Equal((expectedW, expectedH), options.Fit(w, h));
    }

    [Theory]
    [InlineData(ImageTarget.Jpeg, ".JPG", true)]
    [InlineData(ImageTarget.Jpeg, "jpeg", true)]
    [InlineData(ImageTarget.Jpeg, ".tif", false)]
    [InlineData(ImageTarget.Tiff, ".tiff", true)]
    [InlineData(ImageTarget.Png, ".png", true)]
    [InlineData(ImageTarget.Jpeg, ".cr3", false)]
    public void SameContainer_ByExtension(ImageTarget format, string extension, bool expected) {
        Assert.Equal(expected, new ImageConvertOptions(format, 90, 0).SameContainer(extension));
    }

    [Fact]
    public void OnlyJpegAndTiff_CarryMetadata() {
        Assert.True(new ImageConvertOptions(ImageTarget.Jpeg, 90, 0).CarriesMetadata);
        Assert.True(new ImageConvertOptions(ImageTarget.Tiff, 90, 0).CarriesMetadata);
        Assert.False(new ImageConvertOptions(ImageTarget.Png, 90, 0).CarriesMetadata);
        Assert.False(new ImageConvertOptions(ImageTarget.Bmp, 90, 0).CarriesMetadata);
        Assert.False(new ImageConvertOptions(ImageTarget.Gif, 90, 0).CarriesMetadata);
    }
}
