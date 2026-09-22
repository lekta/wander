using System.IO;
using System.Windows.Media.Imaging;
using Wander.Core.Icons;
using Wander.Core.Imaging;

namespace Wander.App.Preview;

/// <summary>
/// A picture decoded for the pane: the copy fitted into it and what is
/// known about the whole frame.
/// </summary>
/// <param name="Fit">What the pane draws, turned upright; at most the pane's size (PLAN AK, step 3).</param>
/// <param name="Meta">EXIF, when the file carries any.</param>
/// <param name="IsRaw">A RAW container; the fit came from its embedded JPEG or its sensor decode.</param>
/// <param name="Embedded">The embedded JPEG the fit was decoded from (RAW only), for the zoom's whole decode.</param>
/// <param name="Downscaled">The fit is a smaller copy - the 1:1 zoom needs the whole frame decoded.</param>
/// <param name="NaturalWidth">Pixels of the whole frame as shown - what the pane caps the picture at.</param>
/// <param name="NaturalHeight">Same.</param>
internal sealed record DecodedPicture(
    BitmapSource Fit, ImageMetadata? Meta, bool IsRaw, byte[]? Embedded, bool Downscaled,
    int NaturalWidth, int NaturalHeight);


/// <summary>
/// Decoding a picture for the pane, off the UI thread (PLAN AK, step 3);
/// the decoded ones are kept by <see cref="PictureCache"/>.
/// </summary>
internal static class PictureLoader {
    private static readonly HashSet<string> _jpeg = new(StringComparer.OrdinalIgnoreCase) {
        ".jpg", ".jpeg", ".jpe", ".jfif",
    };


    /// <summary>
    /// The fitted copy of <paramref name="path"/> for a box of
    /// <paramref name="boxWidth"/> x <paramref name="boxHeight"/> device
    /// pixels. JPEGs - files and the ones RAW containers carry - are
    /// decoded at that size; everything else whole, as before. Null when
    /// nothing could be decoded.
    /// </summary>
    public static DecodedPicture? Decode(
        string path, IImageMetadataReader? reader, double boxWidth, double boxHeight, CancellationToken ct) {
        ct.ThrowIfCancellationRequested();
        var meta = reader?.Read(path);
        int? orientation = meta?.Orientation;

        // Nothing WIC decodes here turns the picture by itself: the RAW
        // decode and the embedded preview ignore the container's tag, and
        // BitmapImage leaves a JPEG the way the sensor stored it. The camera
        // records the turn in EXIF and every viewer applies it - Explorer,
        // Photos, a browser - so a portrait JPEG shown as it is lies on its
        // side here and nowhere else (found 2026-09-16 on a preview taken
        // out of a RAW as it is, tag and all). The tag is applied to every
        // picture; a file without one is unchanged.
        if (ImageFormats.IsRaw(path)) {
            // The quick embedded preview - ten milliseconds, and the pane
            // has the picture. Without one the sensor decode is all there is.
            if (ImageDecoder.RawPreviewBytes(path, fullSize: false) is { } jpeg
                && Fitted(ImageDecoder.StoredSize(jpeg), orientation, boxWidth, boxHeight,
                    whole => ImageDecoder.Stream(jpeg, whole)) is { } embedded) {
                return embedded with { Meta = meta, IsRaw = true, Embedded = jpeg };
            }

            ct.ThrowIfCancellationRequested();

            return Whole(ImageDecoder.File(path), orientation) is { } sensor
                ? sensor with { Meta = meta, IsRaw = true }
                : null;
        }

        if (_jpeg.Contains(Path.GetExtension(path))) {
            return Fitted(ImageDecoder.StoredSize(path), orientation, boxWidth, boxHeight,
                width => width is { } w ? ImageDecoder.File(path, w) : ImageDecoder.File(path)) is { } fitted
                ? fitted with { Meta = meta }
                : null;
        }

        if (Path.GetExtension(path).Equals(".tga", StringComparison.OrdinalIgnoreCase)) {
            return ImageDecoder.Tga(path) is { } tga
                ? new DecodedPicture(tga, meta, false, null, false, tga.PixelWidth, tga.PixelHeight)
                : null;
        }

        return Whole(ImageDecoder.File(path), orientation) is { } plain ? plain with { Meta = meta } : null;
    }


    /// <summary>
    /// The whole frame, for the 1:1 zoom: the file decoded without a size,
    /// or the embedded JPEG the fit came from. Null when the fit already is
    /// the whole frame.
    /// </summary>
    public static BitmapSource? Whole(string path, DecodedPicture picture) {
        if (!picture.Downscaled) {
            return null;
        }

        var whole = picture.Embedded is { } jpeg ? ImageDecoder.Stream(jpeg) : ImageDecoder.File(path);

        return whole is null ? null : ImageDecoder.ApplyOrientation(whole, picture.Meta?.Orientation);
    }


    /// <summary>
    /// A fitted copy decoded from a bigger frame now in memory - the full
    /// JPEG of a CR3, when the pane is wider than its quick preview.
    /// </summary>
    public static BitmapSource? Refit(byte[] jpeg, int? orientation, double boxWidth, double boxHeight) {
        var size = ImageDecoder.StoredSize(jpeg);
        int? width = size is { } s ? PictureFit.DecodeWidth(s.Width, s.Height, orientation, boxWidth, boxHeight) : null;
        var bitmap = width is { } fit ? ImageDecoder.Stream(jpeg, fit) : ImageDecoder.Stream(jpeg);

        return bitmap is null ? null : ImageDecoder.ApplyOrientation(bitmap, orientation);
    }


    private static DecodedPicture? Fitted(
        (int Width, int Height)? stored, int? orientation, double boxWidth, double boxHeight,
        Func<int?, BitmapImage?> decode) {
        int? width = stored is { } s ? PictureFit.DecodeWidth(s.Width, s.Height, orientation, boxWidth, boxHeight) : null;
        if (decode(width) is not { } bitmap) {
            return null;
        }

        var fit = ImageDecoder.ApplyOrientation(bitmap, orientation);
        if (width is null || stored is not { } whole) {
            return new DecodedPicture(fit, null, false, null, false, fit.PixelWidth, fit.PixelHeight);
        }

        bool turned = orientation is >= 5 and <= 8;

        return new DecodedPicture(
            fit, null, false, null, Downscaled: true,
            turned ? whole.Height : whole.Width, turned ? whole.Width : whole.Height);
    }

    private static DecodedPicture? Whole(BitmapImage? bitmap, int? orientation) {
        if (bitmap is null) {
            return null;
        }

        var fit = ImageDecoder.ApplyOrientation(bitmap, orientation);

        return new DecodedPicture(fit, null, false, null, false, fit.PixelWidth, fit.PixelHeight);
    }
}
