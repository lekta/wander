using Wander.Core.Imaging;

namespace Wander.Core.Tests;

public class TgaDecoderTests {
    private static readonly byte[] _red = { 0, 0, 255 };
    private static readonly byte[] _green = { 0, 255, 0 };
    private static readonly byte[] _blue = { 255, 0, 0 };
    private static readonly byte[] _white = { 255, 255, 255 };


    [Fact]
    public void Uncompressed24_BottomUp_TurnedTopDown() {
        // Stored bottom row first: blue, white; then the top row: red, green.
        var file = Tga(type: 2, depth: 24, width: 2, height: 2, descriptor: 0, Concat(_blue, _white, _red, _green));

        var image = TgaDecoder.Decode(file)!;

        Assert.Equal(2, image.Width);
        Assert.Equal(Bgra(_red, _green, _blue, _white), image.Pixels);
    }

    [Fact]
    public void Rle32_TopDown_WithAlpha() {
        // One run of two half-transparent reds, one raw packet of two pixels.
        var data = Concat(
            new byte[] { 0x81, 0, 0, 255, 128 },
            new byte[] { 0x01, 0, 255, 0, 255, 255, 0, 0, 64 });
        var file = Tga(type: 10, depth: 32, width: 2, height: 2, descriptor: 0x28, data);

        var image = TgaDecoder.Decode(file)!;

        Assert.Equal(new byte[] { 0, 0, 255, 128, 0, 0, 255, 128, 0, 255, 0, 255, 255, 0, 0, 64 }, image.Pixels);
    }

    [Fact]
    public void Alpha32WithoutAlphaBits_Opaque() {
        var file = Tga(type: 2, depth: 32, width: 1, height: 1, descriptor: 0x20, new byte[] { 1, 2, 3, 0 });

        Assert.Equal(new byte[] { 1, 2, 3, 255 }, TgaDecoder.Decode(file)!.Pixels);
    }

    [Fact]
    public void Grey8() {
        var file = Tga(type: 3, depth: 8, width: 2, height: 1, descriptor: 0x20, new byte[] { 10, 200 });

        Assert.Equal(new byte[] { 10, 10, 10, 255, 200, 200, 200, 255 }, TgaDecoder.Decode(file)!.Pixels);
    }

    [Fact]
    public void Colour16_FiveBitsEach() {
        // 0x7C00: red at full, no alpha bit, descriptor without alpha: opaque red.
        var file = Tga(type: 2, depth: 16, width: 1, height: 1, descriptor: 0x20, new byte[] { 0x00, 0x7C });

        Assert.Equal(new byte[] { 0, 0, 255, 255 }, TgaDecoder.Decode(file)!.Pixels);
    }

    [Fact]
    public void ColourMapped() {
        var header = Header(type: 1, depth: 8, width: 2, height: 1, descriptor: 0x20);
        header[1] = 1;      // a colour map
        header[5] = 2;      // two entries
        header[7] = 24;     // of 24 bits
        var file = Concat(header, _green, _blue, new byte[] { 1, 0 });

        Assert.Equal(new byte[] { 255, 0, 0, 255, 0, 255, 0, 255 }, TgaDecoder.Decode(file)!.Pixels);
    }

    [Fact]
    public void RightToLeft_Mirrored() {
        var file = Tga(type: 2, depth: 24, width: 2, height: 1, descriptor: 0x30, Concat(_red, _green));

        Assert.Equal(Bgra(_green, _red), TgaDecoder.Decode(file)!.Pixels);
    }

    [Fact]
    public void Truncated_Null() {
        var file = Tga(type: 2, depth: 24, width: 2, height: 2, descriptor: 0, Concat(_red, _green));

        Assert.Null(TgaDecoder.Decode(file));
        Assert.Null(TgaDecoder.Decode(new byte[5]));
    }

    [Fact]
    public void UnknownType_Null() {
        Assert.Null(TgaDecoder.Decode(Tga(type: 32, depth: 24, width: 1, height: 1, descriptor: 0, _red)));
    }

    [Fact]
    public void RunPastTheEnd_Null() {
        var file = Tga(type: 10, depth: 24, width: 1, height: 1, descriptor: 0, new byte[] { 0x85, 0, 0, 255 });

        Assert.Null(TgaDecoder.Decode(file));
    }


    private static byte[] Tga(int type, int depth, int width, int height, int descriptor, byte[] data) {
        return Concat(Header(type, depth, width, height, descriptor), data);
    }

    private static byte[] Header(int type, int depth, int width, int height, int descriptor) {
        var header = new byte[18];
        header[2] = (byte)type;
        header[12] = (byte)width;
        header[14] = (byte)height;
        header[16] = (byte)depth;
        header[17] = (byte)descriptor;

        return header;
    }

    private static byte[] Concat(params byte[][] parts) {
        return parts.SelectMany(p => p).ToArray();
    }

    private static byte[] Bgra(params byte[][] bgr) {
        return bgr.SelectMany(p => p.Append((byte)255)).ToArray();
    }
}
