using System.Buffers.Binary;

namespace Wander.Core.Imaging;

/// <summary>
/// Truevision TGA - the texture format of games and 3D tools, which Windows
/// has no codec for (PLAN B8). Uncompressed and run-length encoded;
/// colour-mapped, true-colour and grey; 8, 15, 16, 24 and 32 bits; the
/// origin corner from the descriptor. Out comes top-down BGRA, straight
/// (not premultiplied) alpha.
/// </summary>
public static class TgaDecoder {
    private const int HeaderSize = 18;

    /// <summary>Past this many pixels the file is not a texture but a trap: 16k x 16k.</summary>
    private const long MaxPixels = 16384L * 16384;

    private const int TopToBottom = 0x20;
    private const int RightToLeft = 0x10;


    /// <summary>The picture, or null when the bytes are not a TGA this reads.</summary>
    public static BgraImage? Decode(ReadOnlySpan<byte> file) {
        if (file.Length < HeaderSize) {
            return null;
        }

        int idLength = file[0];
        int mapType = file[1];
        int type = file[2];
        int mapFirst = BinaryPrimitives.ReadUInt16LittleEndian(file[3..]);
        int mapLength = BinaryPrimitives.ReadUInt16LittleEndian(file[5..]);
        int mapDepth = file[7];
        int width = BinaryPrimitives.ReadUInt16LittleEndian(file[12..]);
        int height = BinaryPrimitives.ReadUInt16LittleEndian(file[14..]);
        int depth = file[16];
        int descriptor = file[17];

        bool rle = type is 9 or 10 or 11;
        int kind = rle ? type - 8 : type;
        if (kind is not (1 or 2 or 3) || width == 0 || height == 0 || (long)width * height > MaxPixels) {
            return null;
        }
        if (kind == 1 && (mapType != 1 || depth is not (8 or 16))) {
            return null;
        }
        if (kind == 2 && depth is not (15 or 16 or 24 or 32)) {
            return null;
        }
        if (kind == 3 && depth != 8) {
            return null;
        }

        int at = HeaderSize + idLength;
        if (at > file.Length) {
            return null;
        }

        byte[]? palette = null;
        if (mapType == 1) {
            int entryBytes = (mapDepth + 7) / 8;
            int mapBytes = mapLength * entryBytes;
            // The depths Color reads. 9 to 14 bits round up to two bytes,
            // and Color would look for a third one.
            if (mapDepth is not (15 or 16 or 24 or 32) || at + mapBytes > file.Length) {
                return null;
            }
            // The map as BGRA, indexed from mapFirst.
            palette = new byte[(mapFirst + mapLength) * 4];
            for (int i = 0; i < mapLength; i++) {
                Color(file.Slice(at + (i * entryBytes), entryBytes), mapDepth, palette.AsSpan((mapFirst + i) * 4, 4));
            }
            at += mapBytes;
        }

        int pixelBytes = (depth + 7) / 8;
        int count = width * height;
        var source = file[at..];
        // Bytes enough for the pixels the header promises, before the buffer
        // for them is made: eighteen bytes of header must not cost a
        // gigabyte. A run-length packet holds at most 128 pixels, in no
        // fewer than one byte of its own and one pixel.
        long least = rle ? (count + 127L) / 128 * (1 + pixelBytes) : (long)count * pixelBytes;
        if (source.Length < least) {
            return null;
        }

        var pixels = new byte[count * 4];
        int written = 0;
        int read = 0;
        Span<byte> one = stackalloc byte[4];
        while (written < count) {
            if (!rle) {
                if (read + pixelBytes > source.Length) {
                    return null;
                }
                Pixel(source.Slice(read, pixelBytes), kind, depth, palette, one);
                one.CopyTo(pixels.AsSpan(written * 4, 4));
                read += pixelBytes;
                written++;

                continue;
            }

            if (read >= source.Length) {
                return null;
            }
            int packet = source[read++];
            int run = (packet & 0x7F) + 1;
            if (written + run > count) {
                return null;
            }
            if ((packet & 0x80) != 0) {
                if (read + pixelBytes > source.Length) {
                    return null;
                }
                Pixel(source.Slice(read, pixelBytes), kind, depth, palette, one);
                read += pixelBytes;
                for (int i = 0; i < run; i++) {
                    one.CopyTo(pixels.AsSpan((written + i) * 4, 4));
                }
            } else {
                if (read + (run * pixelBytes) > source.Length) {
                    return null;
                }
                for (int i = 0; i < run; i++) {
                    Pixel(source.Slice(read, pixelBytes), kind, depth, palette, one);
                    one.CopyTo(pixels.AsSpan((written + i) * 4, 4));
                    read += pixelBytes;
                }
            }
            written += run;
        }

        // Alpha only when the descriptor says there is some, and not when
        // every pixel says "transparent": exporters that write 32 bits with
        // an empty channel are common, and trusting them draws nothing.
        bool hasAlpha = (descriptor & 0x0F) != 0 && (depth == 32 || (depth == 16 && kind == 2) || palette is not null);
        if (!hasAlpha || AllTransparent(pixels)) {
            for (int i = 3; i < pixels.Length; i += 4) {
                pixels[i] = 255;
            }
        }

        Orient(pixels, width, height, (descriptor & TopToBottom) == 0, (descriptor & RightToLeft) != 0);

        return new BgraImage(pixels, width, height, width * 4);
    }


    private static void Pixel(ReadOnlySpan<byte> bytes, int kind, int depth, byte[]? palette, Span<byte> bgra) {
        switch (kind) {
            case 1:
                int index = depth == 8 ? bytes[0] : BinaryPrimitives.ReadUInt16LittleEndian(bytes);
                if (palette is not null && (index * 4) + 4 <= palette.Length) {
                    palette.AsSpan(index * 4, 4).CopyTo(bgra);
                } else {
                    bgra.Clear();
                }
                break;

            case 3:
                bgra[0] = bgra[1] = bgra[2] = bytes[0];
                bgra[3] = 255;
                break;

            default:
                Color(bytes, depth, bgra);
                break;
        }
    }

    /// <summary>One colour of 15, 16, 24 or 32 bits, as BGRA.</summary>
    private static void Color(ReadOnlySpan<byte> bytes, int depth, Span<byte> bgra) {
        if (depth is 15 or 16) {
            int v = BinaryPrimitives.ReadUInt16LittleEndian(bytes);
            bgra[0] = Five(v & 0x1F);
            bgra[1] = Five((v >> 5) & 0x1F);
            bgra[2] = Five((v >> 10) & 0x1F);
            bgra[3] = depth == 16 && (v & 0x8000) == 0 ? (byte)0 : (byte)255;

            return;
        }

        bgra[0] = bytes[0];
        bgra[1] = bytes[1];
        bgra[2] = bytes[2];
        bgra[3] = depth == 32 ? bytes[3] : (byte)255;
    }

    private static byte Five(int value) {
        return (byte)((value << 3) | (value >> 2));
    }

    private static bool AllTransparent(byte[] pixels) {
        for (int i = 3; i < pixels.Length; i += 4) {
            if (pixels[i] != 0) {
                return false;
            }
        }

        return true;
    }

    /// <summary>Turns rows stored bottom-up, or pixels stored right to left, into top-down, left to right.</summary>
    private static void Orient(byte[] pixels, int width, int height, bool flipRows, bool flipColumns) {
        int stride = width * 4;
        if (flipRows) {
            var row = new byte[stride];
            for (int y = 0; y < height / 2; y++) {
                var top = pixels.AsSpan(y * stride, stride);
                var bottom = pixels.AsSpan((height - 1 - y) * stride, stride);
                top.CopyTo(row);
                bottom.CopyTo(top);
                row.CopyTo(bottom);
            }
        }
        if (flipColumns) {
            for (int y = 0; y < height; y++) {
                for (int x = 0; x < width / 2; x++) {
                    int a = (y * stride) + (x * 4);
                    int b = (y * stride) + ((width - 1 - x) * 4);
                    for (int c = 0; c < 4; c++) {
                        (pixels[a + c], pixels[b + c]) = (pixels[b + c], pixels[a + c]);
                    }
                }
            }
        }
    }
}
