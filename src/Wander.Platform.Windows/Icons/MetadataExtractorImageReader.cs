using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.FileType;
using MetadataExtractor.Formats.Jpeg;
using MetadataExtractor.Formats.Png;
using Wander.Core.Icons;

namespace Wander.Platform.Windows.Icons;

/// <summary>
/// Reads EXIF / shot details via MetadataExtractor. Supports JPEG, PNG, TIFF,
/// HEIC, and the major RAW formats including Canon CR2/CR3 — same set as
/// Explorer's Details pane on Win11.
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

            return new ImageMetadata(make, model, iso, aperture, shutter, focal, taken, width, height, orientation);
        } catch {
            return null;
        }
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
