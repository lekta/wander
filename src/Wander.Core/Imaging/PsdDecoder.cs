using System.Buffers.Binary;

namespace Wander.Core.Imaging;

/// <summary>
/// What a Photoshop file shows without its layers: the flattened picture
/// it keeps at its end, and the small JPEG preview among its resources.
/// </summary>
/// <param name="Composite">The flattened picture; null when the file has none worth showing, or it is past the size limit.</param>
/// <param name="Thumbnail">The JPEG preview (resource 1036, up to 160 px), as stored; null when absent.</param>
public sealed record PsdPicture(BgraImage? Composite, byte[]? Thumbnail);


/// <summary>
/// Photoshop's PSD and its large sibling PSB, which Windows has no codec
/// for. Not the layers: the composite Photoshop writes after them
/// ("Maximize compatibility", on by default) - planar channels, raw or
/// PackBits; RGB, grey, duotone, indexed, CMYK and bitmap; 8, 16 and 32
/// bits; the merged transparency as alpha when the layer count says it is
/// there (negative). Lab and the zip-compressed composites are not read.
///
/// <para>
/// The layers are stepped over by their length, so a file of 500 MB costs
/// what its composite costs. A file saved without the composite
/// (<c>hasRealMergedData</c> = 0 in resource 1057), or one past
/// <see cref="MaxPixels"/>, comes back with its preview alone.
/// </para>
/// </summary>
public static class PsdDecoder {
    /// <summary>A composite past this is not decoded: planes plus the picture are seven bytes a pixel.</summary>
    public const long MaxPixels = 36L * 1000 * 1000;

    private const int HeaderSize = 26;
    private const int ThumbnailResource = 1036;
    private const int VersionResource = 1057;

    /// <summary>The image data section past this is not read; a composite that size is not a preview.</summary>
    private const long MaxDataBytes = 512L * 1024 * 1024;


    /// <summary>The picture, or null when the stream is not a PSD this reads.</summary>
    /// <param name="composite">False reads the preview alone - what a small tile needs, at no decoding cost.</param>
    public static PsdPicture? Decode(Stream stream, bool composite = true) {
        try {
            return Read(stream, composite);
        } catch (Exception ex) when (
            ex is EndOfStreamException or InvalidDataException or ArgumentException or OverflowException or IOException) {
            return null;
        }
    }


    private static PsdPicture? Read(Stream stream, bool wantComposite) {
        stream.Position = 0;
        var header = new byte[HeaderSize];
        if (stream.Read(header, 0, HeaderSize) < HeaderSize
            || header[0] != '8' || header[1] != 'B' || header[2] != 'P' || header[3] != 'S') {
            return null;
        }

        int version = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(4));
        if (version is not (1 or 2)) {
            return null;
        }

        bool large = version == 2;
        int channels = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(12));
        int height = (int)BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(14));
        int width = (int)BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(18));
        int depth = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(22));
        int mode = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(24));
        if (channels is < 1 or > 56 || width <= 0 || height <= 0 || depth is not (1 or 8 or 16 or 32)) {
            return null;
        }

        byte[] palette = ReadBlock(stream, wide: false, limit: 1 << 20);
        byte[] resources = ReadBlock(stream, wide: false, limit: 64 << 20);
        var (thumbnail, merged) = Resources(resources);

        // Layers: only whether the first extra channel is the merged
        // transparency - a negative layer count says so.
        long layersLength = ReadLength(stream, large);
        long layersEnd = stream.Position + layersLength;
        bool alpha = false;
        if (layersLength >= (large ? 10 : 6)) {
            ReadLength(stream, large);
            var count = new byte[2];
            stream.ReadExactly(count);
            alpha = BinaryPrimitives.ReadInt16BigEndian(count) < 0;
        }
        stream.Position = layersEnd;

        BgraImage? composite = null;
        if (wantComposite && merged && (long)width * height <= MaxPixels && stream.Length - stream.Position <= MaxDataBytes) {
            var data = new byte[stream.Length - stream.Position];
            stream.ReadExactly(data);
            composite = Composite(data, large, channels, width, height, depth, mode, palette, alpha);
        }

        return composite is null && thumbnail is null ? null : new PsdPicture(composite, thumbnail);
    }


    private static BgraImage? Composite(byte[] data, bool large, int channels, int width, int height, int depth, int mode, byte[] palette, bool alpha) {
        if (data.Length < 2) {
            return null;
        }

        int compression = BinaryPrimitives.ReadUInt16BigEndian(data);
        int rowBytes = (width * depth + 7) / 8;
        int used = mode switch {
            3 => Math.Min(channels, alpha ? 4 : 3),
            4 => Math.Min(channels, alpha ? 5 : 4),
            0 or 2 => 1,
            1 or 7 or 8 => Math.Min(channels, alpha ? 2 : 1),
            _ => 0,
        };
        if (used == 0 || (mode == 3 && used < 3) || (mode == 4 && used < 4) || (mode == 2 && palette.Length < 768)) {
            return null;
        }

        // Each channel as 8-bit samples, row after row.
        var planes = new byte[used][];
        var row = new byte[rowBytes];
        int at = 2;
        int countAt = 2;
        if (compression == 1) {
            at += channels * height * (large ? 4 : 2);
        } else if (compression != 0) {
            return null;
        }

        for (int c = 0; c < used; c++) {
            var plane = new byte[width * height];
            for (int y = 0; y < height; y++) {
                if (compression == 1) {
                    int length = large
                        ? (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(countAt))
                        : BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(countAt));
                    countAt += large ? 4 : 2;
                    if (at + length > data.Length || !UnpackBits(data.AsSpan(at, length), row)) {
                        return null;
                    }
                    at += length;
                } else {
                    if (at + rowBytes > data.Length) {
                        return null;
                    }
                    data.AsSpan(at, rowBytes).CopyTo(row);
                    at += rowBytes;
                }
                Samples(row, plane.AsSpan(y * width, width), depth);
            }
            planes[c] = plane;
        }

        var pixels = new byte[width * height * 4];
        for (int i = 0; i < width * height; i++) {
            int o = i * 4;
            byte a = 255;
            switch (mode) {
                case 3:
                    pixels[o] = planes[2][i];
                    pixels[o + 1] = planes[1][i];
                    pixels[o + 2] = planes[0][i];
                    if (used > 3) {
                        a = planes[3][i];
                    }
                    break;

                case 4: {
                        // Stored as the paper left, not the ink: 255 is none.
                        int k = planes[3][i];
                        pixels[o] = (byte)(planes[2][i] * k / 255);
                        pixels[o + 1] = (byte)(planes[1][i] * k / 255);
                        pixels[o + 2] = (byte)(planes[0][i] * k / 255);
                        if (used > 4) {
                            a = planes[4][i];
                        }
                        break;
                    }

                case 2: {
                        int index = planes[0][i];
                        pixels[o] = palette[512 + index];
                        pixels[o + 1] = palette[256 + index];
                        pixels[o + 2] = palette[index];
                        break;
                    }

                default:
                    pixels[o] = pixels[o + 1] = pixels[o + 2] = planes[0][i];
                    if (used > 1) {
                        a = planes[1][i];
                    }
                    break;
            }
            pixels[o + 3] = a;
        }

        return new BgraImage(pixels, width, height, width * 4);
    }


    /// <summary>A row of samples at any depth as 8-bit values: the high byte, a float gamma-encoded, a bit as black or white.</summary>
    private static void Samples(byte[] row, Span<byte> into, int depth) {
        switch (depth) {
            case 8:
                row.AsSpan(0, into.Length).CopyTo(into);
                break;

            case 16:
                for (int x = 0; x < into.Length; x++) {
                    into[x] = row[x * 2];
                }
                break;

            case 32:
                for (int x = 0; x < into.Length; x++) {
                    float linear = BinaryPrimitives.ReadSingleBigEndian(row.AsSpan(x * 4));
                    into[x] = (byte)Math.Round(Math.Pow(Math.Clamp(linear, 0f, 1f), 1 / 2.2) * 255);
                }
                break;

            default:
                // Bitmap mode: a set bit is black ink.
                for (int x = 0; x < into.Length; x++) {
                    into[x] = (row[x >> 3] & (0x80 >> (x & 7))) != 0 ? (byte)0 : (byte)255;
                }
                break;
        }
    }


    /// <summary>PackBits into a row of known length; false when the run does not fill it exactly.</summary>
    private static bool UnpackBits(ReadOnlySpan<byte> packed, byte[] row) {
        int i = 0, o = 0;
        while (i < packed.Length && o < row.Length) {
            int n = (sbyte)packed[i++];
            if (n >= 0) {
                int count = n + 1;
                if (i + count > packed.Length || o + count > row.Length) {
                    return false;
                }
                packed.Slice(i, count).CopyTo(row.AsSpan(o));
                i += count;
                o += count;
            } else if (n != -128) {
                int count = 1 - n;
                if (i >= packed.Length || o + count > row.Length) {
                    return false;
                }
                row.AsSpan(o, count).Fill(packed[i++]);
                o += count;
            }
        }

        return o == row.Length;
    }


    /// <summary>The preview JPEG, and whether the composite is real (resource 1057; true when not stated).</summary>
    private static (byte[]? Thumbnail, bool Merged) Resources(byte[] block) {
        byte[]? thumbnail = null;
        bool merged = true;
        int p = 0;
        while (p + 12 <= block.Length && block[p] == '8' && block[p + 1] == 'B') {
            int id = BinaryPrimitives.ReadUInt16BigEndian(block.AsSpan(p + 4));
            int nameLength = block[p + 6];
            int q = p + 6 + ((nameLength + 2) & ~1);
            if (q + 4 > block.Length) {
                break;
            }
            int size = (int)BinaryPrimitives.ReadUInt32BigEndian(block.AsSpan(q));
            int body = q + 4;
            if (size < 0 || body + size > block.Length) {
                break;
            }

            if (id == ThumbnailResource && size > 28) {
                thumbnail = block.AsSpan(body + 28, size - 28).ToArray();
            } else if (id == VersionResource && size >= 5) {
                merged = block[body + 4] != 0;
            }
            p = body + ((size + 1) & ~1);
        }

        return (thumbnail, merged);
    }


    private static byte[] ReadBlock(Stream stream, bool wide, int limit) {
        long length = ReadLength(stream, wide);
        if (length < 0 || length > limit) {
            throw new InvalidDataException("PSD section out of bounds");
        }

        var block = new byte[length];
        stream.ReadExactly(block);

        return block;
    }


    private static long ReadLength(Stream stream, bool wide) {
        var bytes = new byte[wide ? 8 : 4];
        stream.ReadExactly(bytes);

        return wide ? (long)BinaryPrimitives.ReadUInt64BigEndian(bytes) : BinaryPrimitives.ReadUInt32BigEndian(bytes);
    }
}
