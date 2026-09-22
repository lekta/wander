using Wander.Core.Localization;

namespace Wander.Core.Actions;

public enum ImageTarget {
    Jpeg,
    Png,
    Bmp,
    Tiff,
    Gif,
}


/// <summary>
/// What the built-in picture encoder (<see cref="ActionPresets.ImageConvert"/>)
/// is asked to do, read from its arguments:
/// <c>format=jpeg|png|bmp|tiff|gif; quality=1..100; maxside=N; source=preview|preview-full</c>.
/// The decisions that are arithmetic rather than imaging live here - the
/// size a picture is scaled to, and whether its metadata can travel whole.
/// </summary>
/// <param name="FromPreview">
/// Take the JPEG a RAW file carries instead of decoding the sensor data
/// (<c>source=preview</c> or <c>preview-full</c>).
/// </param>
/// <param name="FullSizePreview">
/// The biggest JPEG the file carries rather than the quickest one
/// (<c>source=preview-full</c>): in a CR3 the full-size JPEG instead of the
/// 1620-px one; a TIFF-shaped RAW gives its biggest either way.
/// </param>
public sealed record ImageConvertOptions(
    ImageTarget Format, int Quality, int MaxSide, bool FromPreview = false, bool FullSizePreview = false) {
    public const int DefaultQuality = 90;


    /// <summary>Only JPEG and TIFF have somewhere to put EXIF; the other encoders write none.</summary>
    public bool CarriesMetadata => Format is ImageTarget.Jpeg or ImageTarget.Tiff;

    /// <summary>
    /// The preview is written as the camera stored it, recompressing
    /// nothing: only when it stays a JPEG of its own size. The pixels then
    /// keep their stored orientation and the file says how to turn them.
    /// </summary>
    public bool KeepsPreviewAsIs => FromPreview && Format == ImageTarget.Jpeg && MaxSide == 0;


    /// <exception cref="FormatException">No format, or one the encoder does not write - the run reports it.</exception>
    public static ImageConvertOptions Parse(string arguments) {
        var args = BuiltinArguments.Parse(arguments);
        string? format = args.Get("format");
        var target = format?.ToLowerInvariant() switch {
            "jpeg" or "jpg" => ImageTarget.Jpeg,
            "png" => ImageTarget.Png,
            "bmp" => ImageTarget.Bmp,
            "tiff" or "tif" => ImageTarget.Tiff,
            "gif" => ImageTarget.Gif,
            null => throw new FormatException(Text.Get("ActionsErrorImageNoFormat")),
            _ => throw new FormatException(Text.Format("ActionsErrorImageFormat", format)),
        };

        string? source = args.Get("source");
        var (fromPreview, fullSize) = source?.ToLowerInvariant() switch {
            null or "" or "file" => (false, false),
            "preview" => (true, false),
            "preview-full" => (true, true),
            _ => throw new FormatException(Text.Format("ActionsErrorImageSource", source)),
        };

        return new ImageConvertOptions(
            target,
            args.GetInt("quality", DefaultQuality, 1, 100),
            args.GetInt("maxside", 0, 0, 65535),
            fromPreview,
            fullSize);
    }


    /// <summary>
    /// The size to encode at: the long side brought down to
    /// <see cref="MaxSide"/>, the proportions kept, never enlarged. Takes the
    /// size after orientation - a portrait photograph's long side is its height.
    /// </summary>
    public (int Width, int Height) Fit(int width, int height) {
        int longSide = Math.Max(width, height);
        if (MaxSide <= 0 || longSide <= MaxSide) {
            return (width, height);
        }

        double scale = (double)MaxSide / longSide;

        return (Scaled(width, scale), Scaled(height, scale));
    }

    /// <summary>Whether a source of this extension is the target's own container - then its metadata is copied whole.</summary>
    public bool SameContainer(string extension) {
        return extension.TrimStart('.').ToLowerInvariant() switch {
            "jpg" or "jpeg" or "jpe" or "jfif" => Format == ImageTarget.Jpeg,
            "tif" or "tiff" => Format == ImageTarget.Tiff,
            "png" => Format == ImageTarget.Png,
            "bmp" => Format == ImageTarget.Bmp,
            "gif" => Format == ImageTarget.Gif,
            _ => false,
        };
    }


    private static int Scaled(int side, double scale) {
        return Math.Max(1, (int)Math.Round(side * scale, MidpointRounding.AwayFromZero));
    }
}
