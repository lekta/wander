using System.Buffers.Binary;
using System.Text;
using Wander.Core.Companions;
using Wander.Core.FileSystem;

namespace Wander.Core.Tests;

public class EmbeddedRatingTests {
    private static readonly byte[] _xmpUuid = {
        0xbe, 0x7a, 0xcf, 0xcb, 0x97, 0xa9, 0x42, 0xe8,
        0x9c, 0x71, 0x99, 0x94, 0x91, 0xe3, 0xaf, 0xac,
    };

    private static readonly byte[] _canonMetaUuid = {
        0x85, 0xc0, 0xb6, 0x87, 0x82, 0x0f, 0x11, 0xe0,
        0x81, 0x11, 0xf4, 0xce, 0x46, 0x2b, 0x6a, 0x48,
    };


    /// <summary>The packet a Canon R8 writes, element form, as found in a real CR3 (2026-10-07).</summary>
    internal static string Packet(int rating, string? label = null) {
        string labelElement = label is null ? "" : $"<xmp:Label>{label}</xmp:Label>";

        return "<x:xmpmeta xmlns:x=\"adobe:ns:meta/\"><rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\">"
            + "<rdf:Description rdf:about=\"\" xmlns:xmp=\"http://ns.adobe.com/xap/1.0/\">"
            + $"<xmp:Rating>{rating}</xmp:Rating>{labelElement}</rdf:Description></rdf:RDF></x:xmpmeta>";
    }

    /// <summary>A CR3's head: ftyp, moov with Canon's metadata (CMT1 = IFD0), the XMP uuid box, an mdat.</summary>
    internal static byte[] Cr3(string? packet, int? exifRating = null) {
        var moov = Box("moov", Uuid(_canonMetaUuid, Box("CMT1", Tiff(little: true, exifRating, xmp: null))));
        var xmp = packet is null ? Array.Empty<byte>() : Uuid(_xmpUuid, Concat(Encoding.UTF8.GetBytes(packet), new byte[200]));

        return Concat(Box("ftyp", Encoding.ASCII.GetBytes("crx \0\0\0\u0001crx isom")), moov, xmp, Box("mdat", new byte[4096]));
    }

    /// <summary>A JPEG's head: SOI, APP1 EXIF and / or APP1 XMP, SOS.</summary>
    internal static byte[] Jpeg(string? packet, int? exifRating = null) {
        var parts = new List<byte[]> { new byte[] { 0xFF, 0xD8 } };
        if (exifRating is not null) {
            parts.Add(Segment(0xE1, Concat(Encoding.ASCII.GetBytes("Exif\0\0"), Tiff(little: false, exifRating, xmp: null))));
        }
        if (packet is not null) {
            parts.Add(Segment(0xE1, Concat(Encoding.ASCII.GetBytes("http://ns.adobe.com/xap/1.0/\0"), Encoding.UTF8.GetBytes(packet))));
        }
        parts.Add(new byte[] { 0xFF, 0xDA, 0x00, 0x02, 0x11, 0x22 });

        return Concat(parts.ToArray());
    }

    /// <summary>IFD0 of a TIFF with the EXIF rating (SHORT) and / or the XMP packet (tag 0x02BC) - a CR2, NEF, ARW or DNG head.</summary>
    internal static byte[] Tiff(bool little, int? rating, string? xmp) {
        var entries = new List<(ushort Tag, ushort Type, uint Count, uint Value)>();
        byte[] packet = xmp is null ? Array.Empty<byte>() : Encoding.UTF8.GetBytes(xmp);
        entries.Add((0x0112, 3, 1, 1));
        if (xmp is not null) {
            entries.Add((0x02BC, 1, (uint)packet.Length, 0));
        }
        if (rating is not null) {
            entries.Add((0x4746, 3, 1, (uint)rating.Value));
        }

        int ifdSize = 2 + entries.Count * 12 + 4;
        uint packetAt = (uint)(8 + ifdSize);
        var tiff = new byte[8 + ifdSize + packet.Length];
        tiff[0] = tiff[1] = (byte)(little ? 'I' : 'M');
        U16(tiff, 2, 42, little);
        U32(tiff, 4, 8, little);
        U16(tiff, 8, (ushort)entries.Count, little);
        for (int i = 0; i < entries.Count; i++) {
            int at = 10 + i * 12;
            var (tag, type, count, value) = entries[i];
            U16(tiff, at, tag, little);
            U16(tiff, at + 2, type, little);
            U32(tiff, at + 4, count, little);
            if (tag == 0x02BC) {
                U32(tiff, at + 8, packetAt, little);
            } else {
                U16(tiff, at + 8, (ushort)value, little);
            }
        }
        packet.CopyTo(tiff, (int)packetAt);

        return tiff;
    }

    private static SidecarRating? Read(byte[] bytes) {
        return EmbeddedRating.Read(new MemoryStream(bytes));
    }


    // --- Where it lives ---------------------------------------------------

    [Fact]
    public void Cr3_TheCamerasStars_InTheXmpBox() {
        var rating = Read(Cr3(Packet(5)));

        Assert.Equal(5, rating?.Rank);
        Assert.Null(rating!.ColorLabel);
        Assert.True(rating.InPhoto);
    }

    [Fact]
    public void Cr3_EveryUnratedFrame_SaysNothing() {
        // The camera writes <xmp:Rating>0</xmp:Rating> into every frame: a
        // folder of them is not a folder of rated photos.
        Assert.Null(Read(Cr3(Packet(0))));
    }

    [Fact]
    public void Cr3_ExifRatingInCmt1_WhenThereIsNoXmp() {
        Assert.Equal(3, Read(Cr3(packet: null, exifRating: 3))?.Rank);
    }

    [Fact]
    public void Xmp_WinsOverExif() {
        Assert.Equal(4, Read(Cr3(Packet(4), exifRating: 2))?.Rank);
        Assert.Equal(4, Read(Jpeg(Packet(4), exifRating: 2))?.Rank);
    }

    [Fact]
    public void Jpeg_XmpSegment_AndExifSegment() {
        Assert.Equal(2, Read(Jpeg(Packet(2)))?.Rank);
        Assert.Equal(3, Read(Jpeg(packet: null, exifRating: 3))?.Rank);
    }

    [Fact]
    public void Jpeg_LabelComesAlong() {
        var rating = Read(Jpeg(Packet(0, "Green")));

        Assert.Null(rating?.Rank);
        Assert.Equal(3, rating?.ColorLabel);
    }

    [Fact]
    public void TiffShapedRaw_Ifd0_BothByteOrders() {
        Assert.Equal(4, Read(Tiff(little: true, 4, xmp: null))?.Rank);
        Assert.Equal(4, Read(Tiff(little: false, 4, xmp: null))?.Rank);
        Assert.Equal(5, Read(Tiff(little: true, rating: null, Packet(5)))?.Rank);
    }

    [Fact]
    public void Raf_TheWrappedJpeg() {
        var jpeg = Jpeg(Packet(2));
        var raf = new byte[100 + jpeg.Length];
        Encoding.ASCII.GetBytes("FUJIFILMCCD-RAW 0201").CopyTo(raf, 0);
        BinaryPrimitives.WriteUInt32BigEndian(raf.AsSpan(84), 100);
        BinaryPrimitives.WriteUInt32BigEndian(raf.AsSpan(88), (uint)jpeg.Length);
        jpeg.CopyTo(raf, 100);

        Assert.Equal(2, Read(raf)?.Rank);
    }

    [Fact]
    public void NotAPhoto_OrCutShort_SaysNothing() {
        Assert.Null(Read(Encoding.ASCII.GetBytes("just some text, long enough to sniff")));
        Assert.Null(Read(Cr3(Packet(5))[..40]));
        Assert.Null(Read(Array.Empty<byte>()));
    }

    [Fact]
    public void Reads_RawAndWhatACameraWrites_NotEveryPicture() {
        Assert.True(EmbeddedRating.Reads("IMG.CR3"));
        Assert.True(EmbeddedRating.Reads("IMG.jpg"));
        Assert.True(EmbeddedRating.Reads("IMG.HIF"));
        Assert.False(EmbeddedRating.Reads("logo.png"));
        Assert.False(EmbeddedRating.Reads("notes.txt"));
    }


    // --- Sidecar over photo -------------------------------------------------

    [Fact]
    public void Merge_TheSidecarsFieldWins_ItsZeroToo() {
        var photo = new SidecarRating(5, null, InPhoto: true);

        Assert.Equal(2, EmbeddedRating.Merge(new SidecarRating(2, 0), photo)?.Rank);
        // A sidecar's 0 is how the camera's stars come off.
        var cleared = EmbeddedRating.Merge(new SidecarRating(0, 0), photo);
        Assert.Equal(0, cleared?.Rank);
        Assert.False(cleared!.InPhoto);
    }

    [Fact]
    public void Merge_APhotoFieldTheSidecarLacks_ComesThrough() {
        var merged = EmbeddedRating.Merge(new SidecarRating(null, 3, "Green"), new SidecarRating(5, null, InPhoto: true));

        Assert.Equal(5, merged?.Rank);
        Assert.Equal(3, merged?.ColorLabel);
        Assert.True(merged!.InPhoto);
    }

    [Fact]
    public void Merge_OneSideMissing_IsTheOther() {
        var photo = new SidecarRating(5, null, InPhoto: true);
        var sidecar = new SidecarRating(1, 1);

        Assert.Same(photo, EmbeddedRating.Merge(null, photo));
        Assert.Same(sidecar, EmbeddedRating.Merge(sidecar, null));
        Assert.Null(EmbeddedRating.Merge(null, null));
    }


    // --- Bytes ----------------------------------------------------------------

    private static byte[] Box(string type, byte[] body) {
        var box = new byte[8 + body.Length];
        BinaryPrimitives.WriteUInt32BigEndian(box, (uint)box.Length);
        Encoding.ASCII.GetBytes(type).CopyTo(box, 4);
        body.CopyTo(box, 8);

        return box;
    }

    private static byte[] Uuid(byte[] uuid, byte[] body) {
        return Box("uuid", Concat(uuid, body));
    }

    private static byte[] Segment(byte marker, byte[] body) {
        var segment = new byte[4 + body.Length];
        segment[0] = 0xFF;
        segment[1] = marker;
        BinaryPrimitives.WriteUInt16BigEndian(segment.AsSpan(2), (ushort)(body.Length + 2));
        body.CopyTo(segment, 4);

        return segment;
    }

    private static byte[] Concat(params byte[][] parts) {
        return parts.SelectMany(p => p).ToArray();
    }

    private static void U16(byte[] b, int at, ushort value, bool little) {
        if (little) {
            BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(at), value);
        } else {
            BinaryPrimitives.WriteUInt16BigEndian(b.AsSpan(at), value);
        }
    }

    private static void U32(byte[] b, int at, uint value, bool little) {
        if (little) {
            BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(at), value);
        } else {
            BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(at), value);
        }
    }
}
