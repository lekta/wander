using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.Exif.Makernotes;
using MetadataExtractor.Formats.Jpeg;
using MetadataExtractor.Formats.Png;
using Wander.Core.Icons;
using Wander.Core.Imaging;

namespace Wander.Platform.Windows.Icons;

/// <summary>
/// Reads EXIF / shot details via MetadataExtractor. Supports JPEG, PNG, TIFF,
/// HEIC, and the major RAW formats including Canon CR2/CR3 — same set as
/// Explorer's Details pane on Win11.
///
/// <para>
/// Two readings of the same tags: the worded ones for the footer, and the
/// numbers for the converter, which writes them into the file it makes.
/// The position comes from the GPS block, altitude included.
/// </para>
/// </summary>
public sealed class MetadataExtractorImageReader : IImageMetadataReader {
    public ImageMetadata? Read(string path) {
        try {
            var dirs = ImageMetadataReader.ReadMetadata(path);

            var ifd0 = dirs.OfType<ExifIfd0Directory>().FirstOrDefault();
            var sub = dirs.OfType<ExifSubIfdDirectory>().FirstOrDefault();

            string? make = ifd0?.GetDescription(ExifDirectoryBase.TagMake);
            string? model = ifd0?.GetDescription(ExifDirectoryBase.TagModel);

            string? iso = sub?.GetDescription(ExifDirectoryBase.TagIsoEquivalent);
            string? aperture = sub?.GetDescription(ExifDirectoryBase.TagFNumber);
            string? shutter = sub?.GetDescription(ExifDirectoryBase.TagExposureTime);
            string? focal = sub?.GetDescription(ExifDirectoryBase.TagFocalLength);

            DateTime? taken = null;
            if (sub?.TryGetDateTime(ExifDirectoryBase.TagDateTimeOriginal, out var dto) == true) {
                taken = dto;
            }

            int? width = null;
            int? height = null;
            ReadDimensions(dirs, ref width, ref height);

            int? orientation = ifd0?.TryGetInt32(ExifDirectoryBase.TagOrientation, out int o) == true ? o : null;

            return new ImageMetadata(make, model, iso, aperture, shutter, focal, taken, width, height, orientation,
                Iso: Int(sub, ExifDirectoryBase.TagIsoEquivalent),
                ExposureSeconds: Number(sub, ExifDirectoryBase.TagExposureTime),
                FNumber: Number(sub, ExifDirectoryBase.TagFNumber),
                FocalLengthMm: Number(sub, ExifDirectoryBase.TagFocalLength),
                Position: Position(dirs.OfType<GpsDirectory>().FirstOrDefault()),
                Copyright: ifd0?.GetString(ExifDirectoryBase.TagCopyright),
                Rating: Int(ifd0, ExifDirectoryBase.TagRating),
                AfPoints: AfPoints(dirs.OfType<CanonMakernoteDirectory>().FirstOrDefault(), orientation));
        } catch {
            return null;
        }
    }


    /// <summary>
    /// Where a Canon focused (<see cref="CanonAfInfo"/>), turned upright.
    /// Null for other makers, for a frame focused by hand, and for a record
    /// that does not read - no areas is the whole answer then, not an error.
    /// </summary>
    private static IReadOnlyList<AfPoint>? AfPoints(CanonMakernoteDirectory? canon, int? orientation) {
        try {
            if (canon?.GetObject(CanonMakernoteDirectory.TagAfInfoArray2) is not ushort[] record) {
                return null;
            }

            var points = CanonAfInfo.Parse(record);

            return points.Count == 0 ? null : AfGeometry.Orient(points, orientation);
        } catch {
            return null;
        }
    }


    private static int? Int(MetadataExtractor.Directory? dir, int tag) {
        return dir?.TryGetInt32(tag, out int value) == true ? value : null;
    }

    private static double? Number(MetadataExtractor.Directory? dir, int tag) {
        return dir?.TryGetRational(tag, out var value) == true ? value.ToDouble() : null;
    }

    /// <summary>
    /// Latitude and longitude, altitude when the block has one. A block with
    /// zeros for both (a camera with GPS that never got a fix) reads as no
    /// position - that is MetadataExtractor's own rule.
    /// </summary>
    private static GeoPosition? Position(GpsDirectory? gps) {
        if (gps?.GetGeoLocation() is not { } location || location.IsZero) {
            return null;
        }

        double? altitude = null;
        if (gps.TryGetRational(GpsDirectory.TagAltitude, out var metres)) {
            bool below = gps.TryGetInt32(GpsDirectory.TagAltitudeRef, out int reference) && reference == 1;
            altitude = below ? -metres.ToDouble() : metres.ToDouble();
        }

        return new GeoPosition(location.Latitude, location.Longitude, altitude);
    }

    private static void ReadDimensions(IEnumerable<MetadataExtractor.Directory> dirs, ref int? width, ref int? height) {
        // EXIF SubIFD has PixelXDimension/PixelYDimension; JPEG/PNG file directories have their own.
        foreach (var dir in dirs) {
            switch (dir) {
                case JpegDirectory jpeg:
                    if (jpeg.TryGetInt32(JpegDirectory.TagImageWidth, out int jw)) {
                        width ??= jw;
                    }
                    if (jpeg.TryGetInt32(JpegDirectory.TagImageHeight, out int jh)) {
                        height ??= jh;
                    }
                    break;
                case PngDirectory png:
                    if (png.TryGetInt32(PngDirectory.TagImageWidth, out int pw)) {
                        width ??= pw;
                    }
                    if (png.TryGetInt32(PngDirectory.TagImageHeight, out int ph)) {
                        height ??= ph;
                    }
                    break;
                case ExifSubIfdDirectory sub:
                    if (sub.TryGetInt32(ExifDirectoryBase.TagExifImageWidth, out int ew)) {
                        width ??= ew;
                    }
                    if (sub.TryGetInt32(ExifDirectoryBase.TagExifImageHeight, out int eh)) {
                        height ??= eh;
                    }
                    break;
            }
        }
    }
}
