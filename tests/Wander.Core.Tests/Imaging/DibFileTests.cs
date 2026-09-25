using System.Buffers.Binary;
using Wander.Core.Imaging;

namespace Wander.Core.Tests.Imaging;

public class DibFileTests {
    /// <summary>A packed DIB: a header of <paramref name="headerSize"/> bytes, then masks, palette and pixels as given.</summary>
    private static byte[] Dib(int headerSize, ushort bits, uint compression, uint colorsUsed, int after) {
        var dib = new byte[headerSize + after];
        BinaryPrimitives.WriteUInt32LittleEndian(dib, (uint)headerSize);
        BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(4), 2);
        BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(8), 2);
        BinaryPrimitives.WriteUInt16LittleEndian(dib.AsSpan(12), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(dib.AsSpan(14), bits);
        BinaryPrimitives.WriteUInt32LittleEndian(dib.AsSpan(16), compression);
        BinaryPrimitives.WriteUInt32LittleEndian(dib.AsSpan(32), colorsUsed);

        return dib;
    }

    private static uint PixelsAt(byte[] bmp) {
        return BinaryPrimitives.ReadUInt32LittleEndian(bmp.AsSpan(10));
    }


    [Fact]
    public void ToBmp_PutsTheFileHeaderInFront() {
        var dib = Dib(40, 32, 0, 0, after: 16);

        var bmp = DibFile.ToBmp(dib)!;

        Assert.Equal((byte)'B', bmp[0]);
        Assert.Equal((byte)'M', bmp[1]);
        Assert.Equal((uint)bmp.Length, BinaryPrimitives.ReadUInt32LittleEndian(bmp.AsSpan(2)));
        Assert.Equal(14u + 40, PixelsAt(bmp));
        Assert.Equal(dib, bmp[14..]);
    }

    /// <summary>Bit fields after a 40-byte header: the three masks sit between it and the pixels.</summary>
    [Fact]
    public void ToBmp_MasksAfterTheShortHeader() {
        Assert.Equal(14u + 40 + 12, PixelsAt(DibFile.ToBmp(Dib(40, 32, 3, 0, after: 12 + 16))!));
    }

    /// <summary>A V5 header holds its masks itself.</summary>
    [Fact]
    public void ToBmp_MasksInsideTheV5Header() {
        Assert.Equal(14u + 124, PixelsAt(DibFile.ToBmp(Dib(124, 32, 3, 0, after: 16))!));
    }

    [Fact]
    public void ToBmp_PaletteOfEightBits_WholeUnlessCounted() {
        Assert.Equal(14u + 40 + (256 * 4), PixelsAt(DibFile.ToBmp(Dib(40, 8, 0, 0, after: (256 * 4) + 8))!));
        Assert.Equal(14u + 40 + (16 * 4), PixelsAt(DibFile.ToBmp(Dib(40, 8, 0, 16, after: (16 * 4) + 8))!));
    }

    [Fact]
    public void ToBmp_NotABitmap_Null() {
        Assert.Null(DibFile.ToBmp(new byte[10]));
        Assert.Null(DibFile.ToBmp(Dib(12, 24, 0, 0, after: 40)));
        // The palette runs past the end: no pixels left.
        Assert.Null(DibFile.ToBmp(Dib(40, 8, 0, 0, after: 100)));
    }
}
