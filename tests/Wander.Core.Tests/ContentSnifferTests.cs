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

    [Theory]
    [InlineData("Just a note, plain text, nothing to name.")]
    [InlineData("{not json at all}")]
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
}
