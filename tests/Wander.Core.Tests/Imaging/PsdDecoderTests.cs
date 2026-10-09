using System.Buffers.Binary;
using System.Text;
using Wander.Core.Imaging;

namespace Wander.Core.Tests.Imaging;

/// <summary>
/// Files built here section by section: header, colour mode data,
/// resources, layers (only their count matters), then the planar
/// composite - what is tested is that the layers are stepped over by
/// length, the channels land in BGRA, and the run-length rows unpack.
/// </summary>
public class PsdDecoderTests {
    [Fact]
    public void Rgb_PackBits_LandsInBgra() {
        // 2 x 2: red, green / blue, white.
        var file = Psd(mode: 3, channels: 3, width: 2, height: 2, depth: 8, layerCount: 1,
            planes: new[] { new byte[] { 255, 0, 0, 255 }, new byte[] { 0, 255, 0, 255 }, new byte[] { 0, 0, 255, 255 } });

        var image = PsdDecoder.Decode(new MemoryStream(file))!.Composite!;

        Assert.Equal((2, 2), (image.Width, image.Height));
        Assert.Equal(new byte[] { 0, 0, 255, 255, 0, 255, 0, 255, 255, 0, 0, 255, 255, 255, 255, 255 }, image.Pixels);
    }

    [Fact]
    public void NegativeLayerCount_MakesTheFourthChannelAlpha() {
        var planes = new[] { new byte[] { 10, 10 }, new byte[] { 20, 20 }, new byte[] { 30, 30 }, new byte[] { 0, 128 } };

        var withAlpha = PsdDecoder.Decode(new MemoryStream(Psd(3, 4, 2, 1, 8, layerCount: -1, planes)))!.Composite!;
        var withoutAlpha = PsdDecoder.Decode(new MemoryStream(Psd(3, 4, 2, 1, 8, layerCount: 1, planes)))!.Composite!;

        Assert.Equal(new byte[] { 0, 128 }, new[] { withAlpha.Pixels[3], withAlpha.Pixels[7] });
        Assert.Equal(new byte[] { 255, 255 }, new[] { withoutAlpha.Pixels[3], withoutAlpha.Pixels[7] });
    }

    [Fact]
    public void SixteenBitGrey_TakesTheHighByte() {
        var plane = new byte[] { 0x80, 0x01, 0xFF, 0xFF };

        var image = PsdDecoder.Decode(new MemoryStream(Psd(1, 1, 2, 1, 16, layerCount: 0, new[] { plane })))!.Composite!;

        Assert.Equal(new byte[] { 0x80, 0x80, 0x80, 255, 0xFF, 0xFF, 0xFF, 255 }, image.Pixels);
    }

    [Fact]
    public void Cmyk_IsStoredAsPaperLeft() {
        // C = 0 ink (255), M = full ink (0), Y = 0 ink (255), K = 0 ink (255): magenta.
        var planes = new[] { new byte[] { 255 }, new byte[] { 0 }, new byte[] { 255 }, new byte[] { 255 } };

        var image = PsdDecoder.Decode(new MemoryStream(Psd(4, 4, 1, 1, 8, layerCount: 0, planes)))!.Composite!;

        Assert.Equal(new byte[] { 255, 0, 255, 255 }, image.Pixels);
    }

    [Fact]
    public void WithoutRealMergedData_OnlyThePreviewComesBack() {
        byte[] jpeg = { 0xFF, 0xD8, 0xFF, 0xD9 };
        var file = Psd(3, 3, 1, 1, 8, layerCount: 1, new[] { new byte[] { 1 }, new byte[] { 2 }, new byte[] { 3 } },
            thumbnail: jpeg, merged: false);

        var picture = PsdDecoder.Decode(new MemoryStream(file))!;

        Assert.Null(picture.Composite);
        Assert.Equal(jpeg, picture.Thumbnail);
    }

    [Fact]
    public void NotAPsd_IsNull_AndCutShortNeverThrows() {
        Assert.Null(PsdDecoder.Decode(new MemoryStream(Encoding.ASCII.GetBytes("GIF89a and more bytes than a header"))));

        var whole = Psd(3, 3, 4, 4, 8, layerCount: 1, Enumerable.Range(0, 3).Select(_ => new byte[16]).ToArray());
        for (int cut = 0; cut < whole.Length; cut++) {
            var stream = new MemoryStream(whole, 0, cut);

            Assert.Null(Record.Exception(() => PsdDecoder.Decode(stream)));
        }
    }


    /// <summary>A PSD with one layer record's worth of layer section and an RLE composite of the given planes.</summary>
    private static byte[] Psd(int mode, int channels, int width, int height, int depth, int layerCount, byte[][] planes,
        byte[]? thumbnail = null, bool merged = true) {
        var output = new List<byte>();
        output.AddRange(Encoding.ASCII.GetBytes("8BPS"));
        output.AddRange(Be16(1));
        output.AddRange(new byte[6]);
        output.AddRange(Be16(channels));
        output.AddRange(Be32(height));
        output.AddRange(Be32(width));
        output.AddRange(Be16(depth));
        output.AddRange(Be16(mode));
        output.AddRange(Be32(0));

        var resources = new List<byte>();
        resources.AddRange(Resource(1057, new byte[] { 0, 0, 0, 1, (byte)(merged ? 1 : 0) }));
        if (thumbnail is not null) {
            resources.AddRange(Resource(1036, new byte[28].Concat(thumbnail).ToArray()));
        }
        output.AddRange(Be32(resources.Count));
        output.AddRange(resources);

        // Layer and mask info: layer info with its count, and junk the
        // reader must step over by the section's length.
        var layers = new List<byte>();
        layers.AddRange(Be32(2 + 16));
        layers.AddRange(Be16(layerCount));
        layers.AddRange(Enumerable.Repeat((byte)0xAB, 16));
        output.AddRange(Be32(layers.Count));
        output.AddRange(layers);

        int rowBytes = width * depth / 8;
        output.AddRange(Be16(1));
        var rows = new List<byte[]>();
        foreach (var plane in planes) {
            for (int y = 0; y < height; y++) {
                // Literal runs of one byte each: PackBits at its plainest.
                var packed = new List<byte>();
                for (int x = 0; x < rowBytes; x++) {
                    packed.Add(0);
                    packed.Add(plane[y * rowBytes + x]);
                }
                rows.Add(packed.ToArray());
            }
        }
        foreach (var row in rows) {
            output.AddRange(Be16(row.Length));
        }
        foreach (var row in rows) {
            output.AddRange(row);
        }

        return output.ToArray();
    }

    private static byte[] Resource(int id, byte[] data) {
        var block = new List<byte>();
        block.AddRange(Encoding.ASCII.GetBytes("8BIM"));
        block.AddRange(Be16(id));
        block.AddRange(new byte[2]);
        block.AddRange(Be32(data.Length));
        block.AddRange(data);
        if (data.Length % 2 == 1) {
            block.Add(0);
        }

        return block.ToArray();
    }

    private static byte[] Be16(int value) {
        var bytes = new byte[2];
        BinaryPrimitives.WriteInt16BigEndian(bytes, (short)value);

        return bytes;
    }

    private static byte[] Be32(int value) {
        var bytes = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(bytes, value);

        return bytes;
    }
}
