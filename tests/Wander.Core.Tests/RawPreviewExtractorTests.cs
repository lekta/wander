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


    /// <summary>
    /// CR3-shaped file: ftyp, an unrelated box, then Canon's preview uuid
    /// box. With <paramref name="track"/>, also the full-size JPEG in an
    /// mdat and a movie box whose first track points at it.
    /// </summary>
    private static byte[] Cr3(byte[] jpeg, byte[]? canonUuid = null, byte[]? track = null, bool wideOffsets = true) {
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
        if (track is not null) {
            const int TrackAt = 24 + 16 + 8;                // after ftyp, free and the mdat header
            file.AddRange(Box("mdat", track));
            file.AddRange(Moov(TrackAt, track.Length, wideOffsets));
        }
        file.AddRange(uuidBox);

        return file.ToArray();
    }

    /// <summary>
    /// A CR3 movie box cut down to the path the parser walks - moov / trak /
    /// mdia / minf / stbl with the first sample's size and place. Real files
    /// have tkhd, mdhd and the rest around these.
    /// </summary>
    private static byte[] Moov(int sampleAt, int sampleLength, bool wide) {
        byte[] stsz = Box("stsz", Be32(0), Be32(0), Be32(1), Be32(sampleLength));
        byte[] chunks = wide
            ? Box("co64", Be32(0), Be32(1), Be32(0), Be32(sampleAt))
            : Box("stco", Be32(0), Be32(1), Be32(sampleAt));
        byte[] stbl = Box("stbl", Box("stsd", Be32(0), Be32(0)), stsz, chunks);

        return Box("moov", Box("mvhd", new byte[8]), Box("trak", Box("mdia", Box("minf", stbl))));
    }

    private static byte[] Box(string type, params byte[][] body) {
        var box = new List<byte>();
        box.AddRange(Be32(8 + body.Sum(part => part.Length)));
        box.AddRange(Encoding.ASCII.GetBytes(type));
        foreach (byte[] part in body) {
            box.AddRange(part);
        }

        return box.ToArray();
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

    /// <summary>
    /// DNG-shaped IFD0 whose picture is one JPEG strip, its compression a
    /// SHORT in the first two bytes of the value slot - how an iPhone lays
    /// out its ProRAW preview.
    /// </summary>
    private static byte[] StripTiff(bool little, byte[] jpeg) {
        const int Ifd0 = 8;
        const int Payload = Ifd0 + 2 + (3 * 12) + 4;

        var file = new List<byte>();
        file.AddRange(Encoding.ASCII.GetBytes(little ? "II" : "MM"));
        file.AddRange(U16(little, 42));
        file.AddRange(U32(little, Ifd0));
        file.AddRange(U16(little, 3));
        file.AddRange(U16(little, 0x0103));
        file.AddRange(U16(little, 3));
        file.AddRange(U32(little, 1));
        file.AddRange(U16(little, 7));
        file.AddRange(U16(little, 0));
        file.AddRange(Entry(little, 0x0111, 4, 1, Payload));
        file.AddRange(Entry(little, 0x0117, 4, 1, (uint)jpeg.Length));
        file.AddRange(U32(little, 0));
        file.AddRange(jpeg);

        return file.ToArray();
    }

    /// <summary>RW2-shaped: its own TIFF magic, and the JPEG as the one tag of IFD0 - offset as the value, length as the count.</summary>
    private static byte[] Rw2(byte[] jpeg) {
        const int Ifd0 = 8;
        const int Payload = Ifd0 + 2 + 12 + 4;

        var file = new List<byte>();
        file.AddRange(Encoding.ASCII.GetBytes("II"));
        file.AddRange(U16(little: true, 0x55));
        file.AddRange(U32(little: true, Ifd0));
        file.AddRange(U16(little: true, 1));
        file.AddRange(Entry(little: true, 0x002E, 7, (uint)jpeg.Length, Payload));
        file.AddRange(U32(little: true, 0));
        file.AddRange(jpeg);

        return file.ToArray();
    }

    /// <summary>
    /// ORF-shaped: IFD0, the Exif IFD, the maker note; the note's own IFD
    /// points at the camera settings, whose two tags point at the JPEG -
    /// every offset in the note counted from its first byte.
    /// </summary>
    private static byte[] Orf(byte[] jpeg, string noteHeader) {
        const int IfdSize = 2 + 12 + 4;
        const int Ifd0 = 8;
        const int Exif = Ifd0 + IfdSize;
        const int Note = Exif + IfdSize;
        byte[] header = Encoding.Latin1.GetBytes(noteHeader);
        int settings = header.Length + IfdSize;
        int jpegAt = settings + 2 + 24 + 4;

        var file = new List<byte>();
        file.AddRange(Encoding.ASCII.GetBytes("IIRO"));
        file.AddRange(U32(little: true, Ifd0));
        file.AddRange(U16(little: true, 1));
        file.AddRange(Entry(little: true, 0x8769, 4, 1, Exif));
        file.AddRange(U32(little: true, 0));
        file.AddRange(U16(little: true, 1));
        file.AddRange(Entry(little: true, 0x927C, 7, (uint)(jpegAt + jpeg.Length), Note));
        file.AddRange(U32(little: true, 0));
        file.AddRange(header);
        file.AddRange(U16(little: true, 1));
        file.AddRange(Entry(little: true, 0x2020, 13, 1, (uint)settings));
        file.AddRange(U32(little: true, 0));
        file.AddRange(U16(little: true, 2));
        file.AddRange(Entry(little: true, 0x0101, 4, 1, (uint)jpegAt));
        file.AddRange(Entry(little: true, 0x0102, 4, 1, (uint)jpeg.Length));
        file.AddRange(U32(little: true, 0));
        file.AddRange(jpeg);

        return file.ToArray();
    }

    /// <summary>RAF-shaped: the header, the JPEG's offset and length at 84, big-endian, then the JPEG.</summary>
    private static byte[] Raf(byte[] jpeg, int? offset = null) {
        const int JpegAt = 148;

        var file = new List<byte>();
        file.AddRange(Encoding.ASCII.GetBytes("FUJIFILMCCD-RAW 0201FF159505"));
        file.AddRange(new byte[84 - file.Count]);
        file.AddRange(Be32(offset ?? JpegAt));
        file.AddRange(Be32(jpeg.Length));
        file.AddRange(new byte[JpegAt - file.Count]);
        file.AddRange(jpeg);

        return file.ToArray();
    }

    /// <summary>
    /// CRW-shaped: the header, then the root heap - the JPEG, a table of one
    /// record pointing at it, and the table's offset in the last four bytes.
    /// </summary>
    private static byte[] Crw(byte[] jpeg, uint? tableAt = null) {
        const int HeaderSize = 26;

        var file = new List<byte>();
        file.AddRange(Encoding.ASCII.GetBytes("II"));
        file.AddRange(U32(little: true, HeaderSize));
        file.AddRange(Encoding.ASCII.GetBytes("HEAPCCDR"));
        file.AddRange(new byte[HeaderSize - file.Count]);
        file.AddRange(jpeg);
        file.AddRange(U16(little: true, 1));
        file.AddRange(U16(little: true, 0x2007));
        file.AddRange(U32(little: true, (uint)jpeg.Length));
        file.AddRange(U32(little: true, 0));
        file.AddRange(U32(little: true, tableAt ?? (uint)jpeg.Length));

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

    private static byte[]? Extract(byte[] file, bool fullSize = false) {
        return RawPreviewExtractor.Extract(new MemoryStream(file), fullSize);
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

    /// <summary>
    /// The preview pane's zoom: the 1620-px PRVW at 1:1 is a third of a
    /// 6000-px frame blown up, which is what FastStone showed side by side
    /// with it (2026-09-21).
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Extract_FullSize_TakesTheFirstTracksJpeg_InACr3(bool wideOffsets) {
        byte[] preview = BaselineJpeg(padding: 100);
        byte[] full = BaselineJpeg(padding: 900);

        Assert.Equal(full, Extract(Cr3(preview, track: full, wideOffsets: wideOffsets), fullSize: true));
    }

    /// <summary>Thumbnails keep the small one: a fraction of the bytes to read, and more than a tile shows.</summary>
    [Fact]
    public void Extract_ByDefault_KeepsTheSmallCr3Preview() {
        byte[] preview = BaselineJpeg(padding: 100);

        Assert.Equal(preview, Extract(Cr3(preview, track: BaselineJpeg(padding: 900))));
    }

    [Fact]
    public void Extract_FullSize_FallsBackToThePreview_WhenTheFirstTrackIsNotADisplayableJpeg() {
        byte[] preview = BaselineJpeg(padding: 100);

        Assert.Equal(preview, Extract(Cr3(preview, track: LosslessJpeg(padding: 900)), fullSize: true));
    }

    [Fact]
    public void Extract_FullSize_FallsBackToThePreview_WithoutAMovieBox() {
        byte[] preview = BaselineJpeg(padding: 100);

        Assert.Equal(preview, Extract(Cr3(preview), fullSize: true));
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

    /// <summary>An iPhone's DNG is big-endian (2026-09-30): its compression, a SHORT, read as 0x70000 lost the preview.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Extract_ReadsAShortValue_InEitherByteOrder(bool little) {
        byte[] jpeg = BaselineJpeg(padding: 300);

        Assert.Equal(jpeg, Extract(StripTiff(little, jpeg)));
    }

    [Fact]
    public void Extract_FindsThePreview_InAnRw2() {
        byte[] jpeg = BaselineJpeg(padding: 300);

        Assert.Equal(jpeg, Extract(Rw2(jpeg)));
    }

    [Theory]
    [InlineData("OLYMPUS\0II\u0003\0")]
    [InlineData("OM SYSTEM\0\0\0II\u0004\0")]
    public void Extract_FindsThePreview_InAnOrfMakerNote(string noteHeader) {
        byte[] jpeg = BaselineJpeg(padding: 300);

        Assert.Equal(jpeg, Extract(Orf(jpeg, noteHeader)));
    }

    [Fact]
    public void Extract_LeavesAlone_AMakerNoteOfAnotherMaker() {
        Assert.Null(Extract(Orf(BaselineJpeg(padding: 300), "Nikon\0\u0002\u0010\0\0II*\0")));
    }


    // --- RAF ------------------------------------------------------------

    [Fact]
    public void Extract_FindsThePreview_InARaf() {
        byte[] jpeg = BaselineJpeg(padding: 300);

        Assert.Equal(jpeg, Extract(Raf(jpeg)));
        Assert.Equal(jpeg, Extract(Raf(jpeg), fullSize: true));
    }

    [Fact]
    public void Extract_ReturnsNull_WhenARafPointsPastItsEnd() {
        Assert.Null(Extract(Raf(BaselineJpeg(padding: 300), offset: 0xFFFF00)));
    }


    // --- CRW ------------------------------------------------------------

    [Fact]
    public void Extract_FindsThePreview_InACrw() {
        byte[] jpeg = BaselineJpeg(padding: 300);

        Assert.Equal(jpeg, Extract(Crw(jpeg)));
    }

    [Fact]
    public void Extract_ReturnsNull_WhenACrwTablePointsPastItsEnd() {
        Assert.Null(Extract(Crw(BaselineJpeg(padding: 300), tableAt: 0xFFFF00)));
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
