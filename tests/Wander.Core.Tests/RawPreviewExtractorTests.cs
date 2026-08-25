using System.Buffers.Binary;
using System.Text;
using Wander.Core.Icons;

namespace Wander.Core.Tests;

public class RawPreviewExtractorTests {
    // --- Fixtures -------------------------------------------------------
    // Real RAW files are tens of megabytes and can't live in a repo, so the
    // containers are built here by hand: the parser's job is to walk box /
    // IFD structure and pick the right payload, and that is exactly what
    // these exercise.

    /// <summary>A JPEG the way a decoder wants it: baseline frame (SOF0).</summary>
    private static byte[] BaselineJpeg(int padding = 0) {
        return Jpeg(0xC0, padding);
    }

    /// <summary>A lossless JPEG (SOF3) — what a DNG's raw payload looks like.</summary>
    private static byte[] LosslessJpeg(int padding = 0) {
        return Jpeg(0xC3, padding);
    }

    private static byte[] Jpeg(byte frameMarker, int padding) {
        var bytes = new List<byte> {
            0xFF, 0xD8,                                     // SOI
            0xFF, 0xE0, 0x00, 0x06, 0x4A, 0x46, 0x00, 0x00, // APP0, 4 bytes of payload
            0xFF, frameMarker, 0x00, 0x0B,                  // frame header, 9 bytes of payload
            0x08, 0x00, 0x10, 0x00, 0x10, 0x01, 0x00, 0x11, 0x00,
            0xFF, 0xDA, 0x00, 0x02,                         // SOS
        };
        bytes.AddRange(Enumerable.Repeat((byte)0x42, padding));
        bytes.AddRange(new byte[] { 0xFF, 0xD9 });          // EOI

        return bytes.ToArray();
    }


    /// <summary>CR3-shaped file: ftyp, an unrelated box, then Canon's preview uuid box.</summary>
    private static byte[] Cr3(byte[] jpeg, byte[]? canonUuid = null) {
        byte[] uuid = canonUuid ?? new byte[] {
            0xea, 0xf4, 0x2b, 0x5e, 0x1c, 0x98, 0x4b, 0x88,
            0xb9, 0xfb, 0xb7, 0xdc, 0x40, 0x6e, 0x4d, 0x16,
        };

        var prvw = new List<byte>();
        prvw.AddRange(Be32(24 + jpeg.Length));
        prvw.AddRange(Encoding.ASCII.GetBytes("PRVW"));
        prvw.AddRange(new byte[] { 0, 0, 0, 0, 0, 1 });     // unknown fields
        prvw.AddRange(new byte[] { 0x06, 0x54, 0x04, 0x38 });  // 1620 x 1080
        prvw.AddRange(new byte[] { 0, 1 });                 // unknown
        prvw.AddRange(Be32(jpeg.Length));
        prvw.AddRange(jpeg);

        var uuidBox = new List<byte>();
        uuidBox.AddRange(Be32(8 + 16 + 8 + prvw.Count));
        uuidBox.AddRange(Encoding.ASCII.GetBytes("uuid"));
        uuidBox.AddRange(uuid);
        uuidBox.AddRange(new byte[] { 0, 0, 0, 0, 0, 0, 0, 1 });  // padding before PRVW
        uuidBox.AddRange(prvw);

        var file = new List<byte>();
        file.AddRange(Be32(24));
        file.AddRange(Encoding.ASCII.GetBytes("ftyp"));
        file.AddRange(Enumerable.Repeat((byte)0, 16));
        file.AddRange(Be32(16));                            // a box we must skip over
        file.AddRange(Encoding.ASCII.GetBytes("free"));
        file.AddRange(Enumerable.Repeat((byte)0, 8));
        file.AddRange(uuidBox);

        return file.ToArray();
    }


    /// <summary>
    /// TIFF-shaped file whose IFD0 points at each of <paramref name="payloads"/>
    /// through a sub-IFD chain — one JPEG per IFD, which is how CR2 / NEF /
    /// ARW lay theirs out.
    /// </summary>
    private static byte[] Tiff(bool little, params byte[][] payloads) {
        // Layout: header, IFD0, N sub-IFDs, then the payloads.
        const int HeaderSize = 8;
        int ifd0Size = 2 + 12 + 4;                          // one entry: SubIFDs
        int subIfdSize = 2 + 24 + 4;                        // two entries: offset + length
        int subIfdArray = payloads.Length > 1 ? 4 * payloads.Length : 0;

        int ifd0 = HeaderSize;
        int arrayAt = ifd0 + ifd0Size;
        int firstSub = arrayAt + subIfdArray;
        int firstPayload = firstSub + subIfdSize * payloads.Length;

        var file = new List<byte>();
        file.AddRange(little ? Encoding.ASCII.GetBytes("II") : Encoding.ASCII.GetBytes("MM"));
        file.AddRange(U16(little, 42));
        file.AddRange(U32(little, (uint)ifd0));

        file.AddRange(U16(little, 1));
        file.AddRange(Entry(little, 0x014A, 4, (uint)payloads.Length,
            payloads.Length > 1 ? (uint)arrayAt : (uint)firstSub));
        file.AddRange(U32(little, 0));

        if (payloads.Length > 1) {
            for (int i = 0; i < payloads.Length; i++) {
                file.AddRange(U32(little, (uint)(firstSub + subIfdSize * i)));
            }
        }

        int offset = firstPayload;
        foreach (byte[] payload in payloads) {
            file.AddRange(U16(little, 2));
            file.AddRange(Entry(little, 0x0201, 4, 1, (uint)offset));
            file.AddRange(Entry(little, 0x0202, 4, 1, (uint)payload.Length));
            file.AddRange(U32(little, 0));
            offset += payload.Length;
        }

        foreach (byte[] payload in payloads) {
            file.AddRange(payload);
        }

        return file.ToArray();
    }

    private static byte[] Entry(bool little, ushort tag, ushort type, uint count, uint value) {
        var e = new List<byte>();
        e.AddRange(U16(little, tag));
        e.AddRange(U16(little, type));
        e.AddRange(U32(little, count));
        e.AddRange(U32(little, value));

        return e.ToArray();
    }

    private static byte[] U16(bool little, ushort v) {
        var b = new byte[2];
        if (little) {
            BinaryPrimitives.WriteUInt16LittleEndian(b, v);
        } else {
            BinaryPrimitives.WriteUInt16BigEndian(b, v);
        }

        return b;
    }

    private static byte[] U32(bool little, uint v) {
        var b = new byte[4];
        if (little) {
            BinaryPrimitives.WriteUInt32LittleEndian(b, v);
        } else {
            BinaryPrimitives.WriteUInt32BigEndian(b, v);
        }

        return b;
    }

    private static byte[] Be32(int v) {
        var b = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(b, (uint)v);

        return b;
    }

    private static byte[]? Extract(byte[] file) {
        return RawPreviewExtractor.Extract(new MemoryStream(file));
    }


    // --- CR3 ------------------------------------------------------------

    [Fact]
    public void Extract_FindsThePreview_InACr3() {
        byte[] jpeg = BaselineJpeg(padding: 500);

        Assert.Equal(jpeg, Extract(Cr3(jpeg)));
    }

    [Fact]
    public void Extract_IgnoresAUuidBox_ThatIsNotCanonsPreview() {
        byte[] other = Enumerable.Repeat((byte)0xAB, 16).ToArray();

        Assert.Null(Extract(Cr3(BaselineJpeg(), canonUuid: other)));
    }

    [Fact]
    public void Extract_RejectsACr3PreviewThatIsNotADisplayableJpeg() {
        Assert.Null(Extract(Cr3(LosslessJpeg(padding: 500))));
    }


    // --- TIFF -----------------------------------------------------------

    [Fact]
    public void Extract_FindsThePreview_InALittleEndianTiff() {
        byte[] jpeg = BaselineJpeg(padding: 300);

        Assert.Equal(jpeg, Extract(Tiff(little: true, jpeg)));
    }

    [Fact]
    public void Extract_FindsThePreview_InABigEndianTiff() {
        byte[] jpeg = BaselineJpeg(padding: 300);

        Assert.Equal(jpeg, Extract(Tiff(little: false, jpeg)));
    }

    [Fact]
    public void Extract_PrefersTheLargestPreview() {
        byte[] small = BaselineJpeg(padding: 100);
        byte[] large = BaselineJpeg(padding: 900);

        Assert.Equal(large, Extract(Tiff(little: true, small, large)));
    }

    [Fact]
    public void Extract_SkipsTheRawPayload_AndTakesTheSmallerRealPreview() {
        // The biggest JPEG stream in a DNG or NEF is the sensor data itself,
        // stored as lossless JPEG. Picking it would leave the user staring
        // at "no preview available" for a file that has a perfectly good one.
        byte[] preview = BaselineJpeg(padding: 200);
        byte[] rawPayload = LosslessJpeg(padding: 5000);

        Assert.Equal(preview, Extract(Tiff(little: true, preview, rawPayload)));
    }


    // --- Files that are not what they claim -----------------------------

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(15)]
    public void Extract_ReturnsNull_ForAFileTooShortToClassify(int length) {
        Assert.Null(Extract(Enumerable.Repeat((byte)0, length).ToArray()));
    }

    [Fact]
    public void Extract_ReturnsNull_ForSomethingThatIsNotARaw() {
        Assert.Null(Extract(BaselineJpeg(padding: 2000)));
    }

    [Fact]
    public void Extract_ReturnsNull_WhenTheBoxTreeIsTruncated() {
        byte[] file = Cr3(BaselineJpeg(padding: 400));

        Assert.Null(Extract(file[..(file.Length / 2)]));
    }

    [Fact]
    public void Extract_TerminatesOnABoxThatDoesNotAdvance() {
        // size = 0 inside a box means "to end of file"; size < 8 is nonsense.
        // Either one used to be a way to spin the walk forever.
        var file = new List<byte>();
        file.AddRange(Be32(24));
        file.AddRange(Encoding.ASCII.GetBytes("ftyp"));
        file.AddRange(Enumerable.Repeat((byte)0, 16));
        file.AddRange(Be32(0));
        file.AddRange(Encoding.ASCII.GetBytes("uuid"));
        file.AddRange(Enumerable.Repeat((byte)0, 32));

        Assert.Null(Extract(file.ToArray()));
    }

    [Fact]
    public void Extract_ReturnsNull_WhenAnIfdClaimsMoreEntriesThanTheFileHolds() {
        // The classic fuzzed-TIFF shape: an entry count that would have us
        // allocate and read far past the end of the file.
        var file = new List<byte>();
        file.AddRange(Encoding.ASCII.GetBytes("II"));
        file.AddRange(U16(little: true, 42));
        file.AddRange(U32(little: true, 8));
        file.AddRange(U16(little: true, 60000));
        file.AddRange(Enumerable.Repeat((byte)0, 32));

        Assert.Null(Extract(file.ToArray()));
    }

    [Fact]
    public void Extract_ReturnsNull_WhenAPointerLeavesTheFile() {
        byte[] jpeg = BaselineJpeg(padding: 100);
        byte[] file = Tiff(little: true, jpeg);
        // Repoint the JPEG offset entry (IFD0 → sub-IFD → first entry) far
        // past the end. The sub-IFD starts right after IFD0.
        int subIfd = 8 + 2 + 12 + 4;
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(subIfd + 2 + 8), 0xFFFF00);

        Assert.Null(Extract(file));
    }
}
