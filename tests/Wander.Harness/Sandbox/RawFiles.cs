using System.Buffers.Binary;
using System.Globalization;
using System.IO;
using System.Text;

namespace Wander.Harness.Sandbox;

/// <summary>
/// Synthetic RAW files of realistic size. Neither is a decodable camera
/// file - the sensor payload is noise - but each has exactly the structure
/// Wander reads: the embedded display JPEG where <c>RawPreviewExtractor</c>
/// looks for it, and the EXIF block with the orientation where
/// MetadataExtractor looks for it. That is the whole RAW path of the app
/// (thumbnails, preview pane, rotation), and it is why a small real sample
/// would not do: the cost being tested is the file size on disk.
/// </summary>
public static class RawFiles {
    private static readonly byte[] _canonMetaUuid = {
        0x85, 0xc0, 0xb6, 0x87, 0x82, 0x0f, 0x11, 0xe0,
        0x81, 0x11, 0xf4, 0xce, 0x46, 0x2b, 0x6a, 0x48,
    };

    private static readonly byte[] _canonPreviewUuid = {
        0xea, 0xf4, 0x2b, 0x5e, 0x1c, 0x98, 0x4b, 0x88,
        0xb9, 0xfb, 0xb7, 0xdc, 0x40, 0x6e, 0x4d, 0x16,
    };

    private const ushort TagNewSubfileType = 0x00FE;
    private const ushort TagImageWidth = 0x0100;
    private const ushort TagImageLength = 0x0101;
    private const ushort TagBitsPerSample = 0x0102;
    private const ushort TagCompression = 0x0103;
    private const ushort TagPhotometric = 0x0106;
    private const ushort TagMake = 0x010F;
    private const ushort TagModel = 0x0110;
    private const ushort TagStripOffsets = 0x0111;
    private const ushort TagOrientation = 0x0112;
    private const ushort TagSamplesPerPixel = 0x0115;
    private const ushort TagRowsPerStrip = 0x0116;
    private const ushort TagStripByteCounts = 0x0117;
    private const ushort TagXResolution = 0x011A;
    private const ushort TagYResolution = 0x011B;
    private const ushort TagResolutionUnit = 0x0128;
    private const ushort TagSoftware = 0x0131;
    private const ushort TagDateTime = 0x0132;
    private const ushort TagSubIfds = 0x014A;
    private const ushort TagJpegOffset = 0x0201;
    private const ushort TagJpegLength = 0x0202;
    private const ushort TagCfaRepeatDim = 0x828D;
    private const ushort TagCfaPattern = 0x828E;
    private const ushort TagExposureTime = 0x829A;
    private const ushort TagFNumber = 0x829D;
    private const ushort TagExifIfd = 0x8769;
    private const ushort TagIso = 0x8827;
    private const ushort TagDateTimeOriginal = 0x9003;
    private const ushort TagFocalLength = 0x920A;
    private const ushort TagPixelX = 0xA002;
    private const ushort TagPixelY = 0xA003;
    private const ushort TagDngVersion = 0xC612;
    private const ushort TagDngBackwardVersion = 0xC613;
    private const ushort TagUniqueCameraModel = 0xC614;


    /// <summary>Orientations a sandbox cycles through: normal, rotate 90 CW, rotate 90 CCW, upside down.</summary>
    public static int OrientationFor(int index) {
        return (index % 4) switch {
            0 => 1,
            1 => 6,
            2 => 8,
            _ => 3,
        };
    }

    /// <summary>
    /// Canon CR3: ISO-BMFF. <c>ftyp</c>, a <c>moov</c> holding Canon's metadata
    /// uuid (CMT1 = IFD0 TIFF with the orientation, CMT2 = Exif TIFF), the
    /// top-level preview uuid with the <c>PRVW</c> JPEG, and an <c>mdat</c> of
    /// noise that brings the file to <paramref name="totalBytes"/>.
    /// </summary>
    public static void WriteCr3(string path, int orientation, long totalBytes, byte[] previewJpeg, int previewWidth, int previewHeight, int seed) {
        var ftyp = Box("ftyp", Concat(Ascii("crx "), U32(1), Ascii("isom"), Ascii("crx ")));
        var meta = UuidBox(_canonMetaUuid, Concat(
            Box("CNCV", Ascii("CanonCR3_001/01.09.00/00.00.00")),
            Box("CMT1", Ifd0Tiff(orientation, "Canon", "Canon EOS R5 (synthetic)")),
            Box("CMT2", ExifTiff(previewWidth, previewHeight))));
        var moov = Box("moov", meta);

        var prvw = Concat(
            U32(24 + previewJpeg.Length), Ascii("PRVW"), U32(0),
            U16((ushort)previewWidth), U16((ushort)previewHeight), U16(1), U16(0),
            U32(previewJpeg.Length), previewJpeg);
        var preview = UuidBox(_canonPreviewUuid, Concat(new byte[8], prvw));

        long fixedBytes = ftyp.Length + moov.Length + preview.Length + 8;
        long padding = Math.Max(1024, totalBytes - fixedBytes);

        using var file = File.Create(path);
        file.Write(ftyp);
        file.Write(moov);
        file.Write(preview);
        file.Write(U32(checked((int)(8 + padding))));
        file.Write(Ascii("mdat"));
        WriteNoise(file, padding, seed);
    }

    /// <summary>
    /// DNG: TIFF with IFD0 carrying the preview JPEG, orientation and make /
    /// model, an Exif IFD, and a sub-IFD whose single uncompressed strip of
    /// noise is what makes the file large.
    /// </summary>
    public static void WriteDng(string path, int orientation, long totalBytes, byte[] previewJpeg, int previewWidth, int previewHeight, int seed) {
        const int rawWidth = 3000;
        long stripBytes = Math.Max(1024 * 1024, totalBytes - previewJpeg.Length - 4096);
        stripBytes -= stripBytes % (rawWidth * 2);
        int rawHeight = (int)(stripBytes / (rawWidth * 2));

        var ifd0 = new TiffBuilder.TiffIfd()
            .Long(TagNewSubfileType, 1)
            .Long(TagImageWidth, (uint)previewWidth)
            .Long(TagImageLength, (uint)previewHeight)
            .Short(TagBitsPerSample, 8, 8, 8)
            .Short(TagCompression, 6)
            .Short(TagPhotometric, 6)
            .Ascii(TagMake, "Wander Harness")
            .Ascii(TagModel, "Synthetic DNG")
            .Short(TagOrientation, (ushort)orientation)
            .Short(TagSamplesPerPixel, 3)
            .Ascii(TagSoftware, "Wander.Harness")
            .Ascii(TagDateTime, Now())
            .Ref(TagSubIfds, "raw")
            .Ref(TagJpegOffset, "preview")
            .Long(TagJpegLength, (uint)previewJpeg.Length)
            .Ref(TagExifIfd, "exif")
            .Bytes(TagDngVersion, 1, 4, 0, 0)
            .Bytes(TagDngBackwardVersion, 1, 1, 0, 0)
            .Ascii(TagUniqueCameraModel, "Wander Synthetic DNG");

        var exif = ExifIfd(previewWidth, previewHeight);

        var raw = new TiffBuilder.TiffIfd()
            .Long(TagNewSubfileType, 0)
            .Long(TagImageWidth, rawWidth)
            .Long(TagImageLength, (uint)rawHeight)
            .Short(TagBitsPerSample, 16)
            .Short(TagCompression, 1)
            .Short(TagPhotometric, 32803)
            .Ref(TagStripOffsets, "rawdata")
            .Short(TagSamplesPerPixel, 1)
            .Long(TagRowsPerStrip, (uint)rawHeight)
            .Long(TagStripByteCounts, (uint)stripBytes)
            .Short(TagCfaRepeatDim, 2, 2)
            .Bytes(TagCfaPattern, 0, 1, 1, 2);

        var builder = new TiffBuilder()
            .Ifd("ifd0", ifd0)
            .Ifd("exif", exif)
            .Ifd("raw", raw)
            .Blob("preview", previewJpeg)
            .Blob("rawdata", stripBytes, (stream, length) => WriteNoise(stream, length, seed));

        using var file = File.Create(path);
        builder.Build(file);
    }


    // --- TIFF blocks ---------------------------------------------------

    private static byte[] Ifd0Tiff(int orientation, string make, string model) {
        var ifd0 = new TiffBuilder.TiffIfd()
            .Ascii(TagMake, make)
            .Ascii(TagModel, model)
            .Short(TagOrientation, (ushort)orientation)
            .Rational(TagXResolution, 72, 1)
            .Rational(TagYResolution, 72, 1)
            .Short(TagResolutionUnit, 2)
            .Ascii(TagDateTime, Now());

        return new TiffBuilder().Ifd("ifd0", ifd0).Build();
    }

    private static byte[] ExifTiff(int width, int height) {
        return new TiffBuilder().Ifd("ifd0", ExifIfd(width, height)).Build();
    }

    private static TiffBuilder.TiffIfd ExifIfd(int width, int height) {
        return new TiffBuilder.TiffIfd()
            .Rational(TagExposureTime, 1, 250)
            .Rational(TagFNumber, 28, 10)
            .Short(TagIso, 400)
            .Ascii(TagDateTimeOriginal, Now())
            .Rational(TagFocalLength, 50, 1)
            .Long(TagPixelX, (uint)width)
            .Long(TagPixelY, (uint)height);
    }

    private static string Now() {
        return DateTime.Now.ToString("yyyy:MM:dd HH:mm:ss", CultureInfo.InvariantCulture);
    }


    // --- ISO-BMFF boxes ------------------------------------------------

    private static byte[] Box(string type, byte[] payload) {
        return Concat(U32(8 + payload.Length), Ascii(type), payload);
    }

    private static byte[] UuidBox(byte[] uuid, byte[] payload) {
        return Concat(U32(8 + 16 + payload.Length), Ascii("uuid"), uuid, payload);
    }

    private static byte[] Ascii(string text) {
        return Encoding.ASCII.GetBytes(text);
    }

    private static byte[] U32(int value) {
        var bytes = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, (uint)value);

        return bytes;
    }

    private static byte[] U16(ushort value) {
        var bytes = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(bytes, value);

        return bytes;
    }

    private static byte[] Concat(params byte[][] parts) {
        var result = new byte[parts.Sum(p => p.Length)];
        int at = 0;
        foreach (var part in parts) {
            part.CopyTo(result, at);
            at += part.Length;
        }

        return result;
    }

    private static void WriteNoise(Stream stream, long length, int seed) {
        var random = new Random(seed);
        var chunk = new byte[64 * 1024];
        long left = length;
        while (left > 0) {
            random.NextBytes(chunk);
            int take = (int)Math.Min(chunk.Length, left);
            stream.Write(chunk, 0, take);
            left -= take;
        }
    }
}
