using System.Text;
using Wander.Core.Preview;

namespace Wander.Core.Tests;

/// <summary>Formats named by their first bytes, whatever the file is called.</summary>
public class ContentSnifferTests {
    public static IEnumerable<object[]> Signatures() {
        yield return new object[] { new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0 }, ".png" };
        yield return new object[] { new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0, 0 }, ".jpg" };
        yield return new object[] { Ascii("GIF89a\0\0"), ".gif" };
        yield return new object[] { Ascii("8BPS\0\u0001"), ".psd" };
        yield return new object[] { Ascii("RIFF\0\0\0\0WEBPVP8 "), ".webp" };
        yield return new object[] { Ascii("RIFF\0\0\0\0AVI LIST"), ".avi" };
        yield return new object[] { Ascii("\0\0\0\u0018ftypheic\0\0\0\0"), ".heic" };
        yield return new object[] { Ascii("\0\0\0\u0018ftypisom\0\0\0\0"), ".mp4" };
        yield return new object[] { Ascii("\0\0\0\u0014ftypqt  \0\0\0\0"), ".mov" };
        yield return new object[] { new byte[] { 0x1A, 0x45, 0xDF, 0xA3, 0x9F }, ".mkv" };
        yield return new object[] { Ascii("OggS\0\u0002\0\0\0\0\0\0\0\0\u0080theora"), ".ogv" };
        yield return new object[] { Ascii("OggS\0\u0002\0\0\0\0\0\0\0\0OpusHead"), ".opus" };
        yield return new object[] { Ascii("fLaC\0\0\0\u0001"), ".flac" };
        yield return new object[] { Ascii("ID3\u0004\0\0"), ".mp3" };
        yield return new object[] { Ascii("%PDF-1.7\n"), ".pdf" };
        yield return new object[] { Ascii("PK\u0003\u0004" + new string('\0', 26) + "mimetypeapplication/epub+zip"), ".epub" };
        yield return new object[] { Ascii("PK\u0003\u0004" + new string('\0', 26) + "[Content_Types].xml ... xl/workbook.xml"), ".xlsx" };
        yield return new object[] { Ascii("PK\u0003\u0004" + new string('\0', 26) + "readme.txt"), ".zip" };
        yield return new object[] { Ascii("glTF\u0002\0\0\0"), ".glb" };
        yield return new object[] { Ascii("Kaydara FBX Binary  \0"), ".fbx" };
        yield return new object[] { Ascii("ply\nformat ascii 1.0\n"), ".ply" };
        yield return new object[] { Ascii("OTTO\0\u000A"), ".otf" };
        yield return new object[] { Ascii("{\\rtf1\\ansi"), ".rtf" };
        yield return new object[] { Ascii("  <?xml version=\"1.0\"?>\n<svg xmlns=\"...\">"), ".svg" };
        yield return new object[] { Ascii("<?xml version=\"1.0\"?>\n<project/>"), ".xml" };
        yield return new object[] { Ascii("<!DOCTYPE html>\n<html>"), ".html" };
        yield return new object[] { Ascii("{\n  \"name\": \"wander\"\n}"), ".json" };
        yield return new object[] { Ascii("[\n  {\"id\": 1}\n]"), ".json" };
    }

    [Theory]
    [MemberData(nameof(Signatures))]
    public void KnownSignatures_AreNamed(byte[] head, string expected) {
        Assert.Equal(expected, ContentSniffer.Extension(head));
    }

    [Fact]
    public void Executable_NeedsItsPeHeader() {
        var exe = new byte[128];
        exe[0] = (byte)'M';
        exe[1] = (byte)'Z';
        exe[60] = 64;
        Encoding.ASCII.GetBytes("PE\0\0").CopyTo(exe, 64);

        Assert.Equal(".exe", ContentSniffer.Extension(exe));
        Assert.Null(ContentSniffer.Extension(Ascii("MZ is how this note starts, and nothing more")));
    }

    [Fact]
    public void Webp_IsAnimated_ByTheFlagOfItsExtendedHeader() {
        var animated = Ascii("RIFF\0\0\0\0WEBPVP8X\u000A\0\0\0\u0012\0\0\0");
        var stillWithAlpha = Ascii("RIFF\0\0\0\0WEBPVP8X\u000A\0\0\0\u0010\0\0\0");

        Assert.True(ContentSniffer.IsAnimatedWebp(animated));
        Assert.False(ContentSniffer.IsAnimatedWebp(stillWithAlpha));
        Assert.False(ContentSniffer.IsAnimatedWebp(Ascii("RIFF\0\0\0\0WEBPVP8 \0\0\0\0\u0002\0\0\0")));
        Assert.False(ContentSniffer.IsAnimatedWebp(Ascii("RIFF\0\0\0\0WEBPVP8X")));
    }

    [Fact]
    public void Gif_IsAnimated_ByItsSecondPicture() {
        Assert.False(ContentSniffer.IsAnimatedGif(new MemoryStream(Gif(frames: 1))));
        Assert.True(ContentSniffer.IsAnimatedGif(new MemoryStream(Gif(frames: 2))));
        Assert.False(ContentSniffer.IsAnimatedGif(new MemoryStream(Gif(frames: 2)[..60])));
        Assert.False(ContentSniffer.IsAnimatedGif(new MemoryStream(Ascii("not a gif at all"))));
    }

    [Theory]
    [InlineData("Just a note, plain text, nothing to name.")]
    [InlineData("{not json at all}")]
    [InlineData("[section]\nname=\"value\"")]
    public void PlainText_IsNotNamed(string text) {
        Assert.Null(ContentSniffer.Extension(Encoding.UTF8.GetBytes(text)));
    }

    [Fact]
    public void Noise_IsNotNamed() {
        var noise = new byte[256];
        new Random(7).NextBytes(noise);
        noise[0] = 0x13;

        Assert.Null(ContentSniffer.Extension(noise));
    }


    private static byte[] Ascii(string text) {
        return text.Select(c => (byte)c).ToArray();
    }

    /// <summary>
    /// A 1x1 GIF89a of <paramref name="frames"/> pictures: a global colour
    /// table, the NETSCAPE loop extension, and per frame a graphic control
    /// extension, a descriptor and one sub-block of picture data.
    /// </summary>
    private static byte[] Gif(int frames) {
        var bytes = new List<byte>();
        bytes.AddRange(Ascii("GIF89a"));
        bytes.AddRange(new byte[] { 1, 0, 1, 0, 0x80, 0, 0 });              // 1x1, a table of two colours
        bytes.AddRange(new byte[6]);
        bytes.AddRange(new byte[] { 0x21, 0xFF, 0x0B });
        bytes.AddRange(Ascii("NETSCAPE2.0"));
        bytes.AddRange(new byte[] { 0x03, 0x01, 0x00, 0x00, 0x00 });
        for (int i = 0; i < frames; i++) {
            bytes.AddRange(new byte[] { 0x21, 0xF9, 0x04, 0x00, 0x0A, 0x00, 0x00, 0x00 });
            bytes.AddRange(new byte[] { 0x2C, 0, 0, 0, 0, 1, 0, 1, 0, 0x00 });
            bytes.AddRange(new byte[] { 0x02, 0x02, 0x4C, 0x01, 0x00 });
        }
        bytes.Add(0x3B);

        return bytes.ToArray();
    }
}
