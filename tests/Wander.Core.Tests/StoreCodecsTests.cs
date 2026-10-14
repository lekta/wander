using Wander.Core.Preview;
using Wander.Core.Tests.Fakes;

namespace Wander.Core.Tests;

public class StoreCodecsTests {
    [Theory]
    [InlineData(@"C:\p\a.heic", new[] { MissingCodec.Heif, MissingCodec.Hevc })]
    [InlineData(@"C:\p\a.HIF", new[] { MissingCodec.Heif, MissingCodec.Hevc })]
    [InlineData(@"C:\p\a.avif", new[] { MissingCodec.Heif, MissingCodec.Av1 })]
    [InlineData(@"C:\p\a.jxl", new[] { MissingCodec.JpegXl })]
    [InlineData(@"C:\p\a.webp", new[] { MissingCodec.Webp })]
    [InlineData(@"C:\p\a.cr3", new[] { MissingCodec.Raw })]
    [InlineData(@"C:\p\a.ogg", new[] { MissingCodec.WebMedia })]
    [InlineData(@"C:\p\a.opus", new[] { MissingCodec.WebMedia })]
    [InlineData(@"C:\p\a.ogv", new[] { MissingCodec.WebMedia })]
    public void ForFile_NamesTheExtensionsContainerFirst(string path, MissingCodec[] expected) {
        Assert.Equal(expected, StoreCodecs.ForFile(path));
    }

    [Theory]
    [InlineData(@"C:\p\a.jpg")]
    [InlineData(@"C:\p\a.png")]
    [InlineData(@"C:\p\a.dng")]
    [InlineData(@"C:\p\a.mp4")]
    [InlineData(@"C:\p\a.txt")]
    [InlineData(@"C:\p\noext")]
    public void ForFile_IsEmptyForWhatWindowsReadsItself(string path) {
        Assert.Empty(StoreCodecs.ForFile(path));
    }

    [Theory]
    [InlineData("H.265", MissingCodec.Hevc)]
    [InlineData("AV1", MissingCodec.Av1)]
    [InlineData("VP9", MissingCodec.Vp9)]
    [InlineData("MPEG-1", MissingCodec.Mpeg2)]
    [InlineData("MPEG-2", MissingCodec.Mpeg2)]
    [InlineData("Theora", MissingCodec.WebMedia)]
    [InlineData("Vorbis", MissingCodec.WebMedia)]
    [InlineData("H.264", MissingCodec.None)]
    [InlineData("Opus", MissingCodec.None)]
    [InlineData("AAC", MissingCodec.None)]
    [InlineData(null, MissingCodec.None)]
    public void ForStream_NamesTheExtensionOfTheCodec(string? codec, MissingCodec expected) {
        Assert.Equal(expected, StoreCodecs.ForStream(codec));
    }

    [Fact]
    public void FirstMissing_IsTheContainerWhenBothAreMissing() {
        var probe = new FakeCodecProbe { Lacks = { MissingCodec.Heif, MissingCodec.Av1 } };

        Assert.Equal(MissingCodec.Heif, StoreCodecs.FirstMissing(StoreCodecs.ForFile("a.avif"), probe));
    }

    [Fact]
    public void FirstMissing_IsTheCodecWhenOnlyItIsMissing() {
        var probe = new FakeCodecProbe { Lacks = { MissingCodec.Av1 } };

        Assert.Equal(MissingCodec.Av1, StoreCodecs.FirstMissing(StoreCodecs.ForFile("a.avif"), probe));
    }

    [Fact]
    public void FirstMissing_IsNoneWhenEveryOneIsThere() {
        var probe = new FakeCodecProbe();

        Assert.Equal(MissingCodec.None, StoreCodecs.FirstMissing(StoreCodecs.ForFile("a.heic"), probe));
    }

    [Fact]
    public void FirstMissingForMedia_NamesTheMissingStreamByItsCodec() {
        var probe = new FakeCodecProbe { Lacks = { MissingCodec.Vp9 } };

        var (name, missing) = StoreCodecs.FirstMissingForMedia(@"C:\v\a.webm", new[] { "VP9", "Opus" }, probe);

        Assert.Equal("VP9", name);
        Assert.Equal(MissingCodec.Vp9, missing);
    }

    [Fact]
    public void FirstMissingForMedia_FindsAMissingAudioPastAPresentVideo() {
        var probe = new FakeCodecProbe { Lacks = { MissingCodec.WebMedia } };

        var (name, missing) = StoreCodecs.FirstMissingForMedia(@"C:\v\a.mkv", new[] { "H.265", "Vorbis" }, probe);

        Assert.Equal("Vorbis", name);
        Assert.Equal(MissingCodec.WebMedia, missing);
    }

    [Fact]
    public void FirstMissingForMedia_NamesTheOggContainerByTheFilesExtension() {
        // MediaProbe does not read Ogg: no codecs to go by.
        var probe = new FakeCodecProbe { Lacks = { MissingCodec.WebMedia } };

        var (name, missing) = StoreCodecs.FirstMissingForMedia(@"C:\m\track.ogg", Array.Empty<string?>(), probe);

        Assert.Equal("OGG", name);
        Assert.Equal(MissingCodec.WebMedia, missing);
    }

    [Fact]
    public void FirstMissingForMedia_AsksNothingForCodecsWindowsDecodesItself() {
        var probe = new FakeCodecProbe { Lacks = { MissingCodec.Hevc } };

        var (name, missing) = StoreCodecs.FirstMissingForMedia(@"C:\v\a.mp4", new[] { "H.264", "AAC", null }, probe);

        Assert.Null(name);
        Assert.Equal(MissingCodec.None, missing);
        Assert.Empty(probe.Asked);
    }
}
