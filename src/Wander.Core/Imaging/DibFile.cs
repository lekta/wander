using System.Buffers.Binary;

namespace Wander.Core.Imaging;

/// <summary>
/// A device-independent bitmap off the clipboard (<c>CF_DIB</c>) made into
/// a <c>.bmp</c> file a decoder can read (PLAN X): the clipboard holds the
/// header and the pixels, and a file puts a 14-byte header in front whose
/// one fact worth working out is where the pixels begin - past the info
/// header, the colour masks a 40-byte header keeps after itself, and the
/// palette.
/// </summary>
public static class DibFile {
    private const int FileHeaderSize = 14;
    private const int InfoHeaderSize = 40;
    private const uint BiBitFields = 3;
    private const uint BiAlphaBitFields = 6;


    /// <summary>
    /// The bitmap as the bytes of a <c>.bmp</c> file, or null when it is not
    /// one with a header of 40 bytes or more (the old 12-byte core header
    /// does not come off a clipboard) or its sizes do not add up.
    /// </summary>
    public static byte[]? ToBmp(ReadOnlySpan<byte> dib) {
        if (dib.Length < InfoHeaderSize) {
            return null;
        }

        uint headerSize = BinaryPrimitives.ReadUInt32LittleEndian(dib);
        ushort bitCount = BinaryPrimitives.ReadUInt16LittleEndian(dib[14..]);
        uint compression = BinaryPrimitives.ReadUInt32LittleEndian(dib[16..]);
        uint colorsUsed = BinaryPrimitives.ReadUInt32LittleEndian(dib[32..]);
        if (headerSize < InfoHeaderSize || headerSize > dib.Length || bitCount is 0 or > 32 || colorsUsed > 256) {
            return null;
        }

        // A 40-byte header keeps the masks of a bit-field bitmap after it;
        // the bigger headers (V4, V5) hold them inside.
        long masks = headerSize == InfoHeaderSize
            ? compression switch {
                BiBitFields => 12,
                BiAlphaBitFields => 16,
                _ => 0,
            }
            : 0;
        long palette = (colorsUsed > 0 ? colorsUsed : bitCount <= 8 ? 1u << bitCount : 0) * 4L;
        long pixels = headerSize + masks + palette;
        if (pixels >= dib.Length) {
            return null;
        }

        var file = new byte[FileHeaderSize + dib.Length];
        file[0] = (byte)'B';
        file[1] = (byte)'M';
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(2), (uint)file.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(10), (uint)(FileHeaderSize + pixels));
        dib.CopyTo(file.AsSpan(FileHeaderSize));

        return file;
    }
}
