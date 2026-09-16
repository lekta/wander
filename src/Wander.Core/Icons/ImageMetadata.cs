namespace Wander.Core.Icons;

/// <summary>
/// EXIF / shot details for a single image. The six strings are worded for
/// the preview footer ("1/250 сек", "f/2,8"); the numbers behind them are
/// what a converter writes into the file it produces - a footer cannot be
/// parsed back into EXIF, and a picture that goes through the converter
/// must not lose where and how it was taken.
/// </summary>
public sealed record ImageMetadata(
    string? CameraMake,
    string? CameraModel,
    string? IsoSpeed,
    string? Aperture,
    string? ShutterSpeed,
    string? FocalLength,
    DateTime? DateTaken,
    int? PixelWidth,
    int? PixelHeight,
    /// <summary>
    /// EXIF orientation tag (1..8), or null when the file carries none.
    /// Cameras that are told not to rotate the stored image record the
    /// intended rotation here instead — a RAW preview pulled straight out
    /// of the container is therefore un-rotated and needs this applied.
    /// </summary>
    int? Orientation,
    int? Iso = null,
    /// <summary>Exposure time in seconds: 0.004 for 1/250.</summary>
    double? ExposureSeconds = null,
    double? FNumber = null,
    double? FocalLengthMm = null,
    /// <summary>Where the picture was taken, when the camera knew.</summary>
    GeoPosition? Position = null,
    string? Copyright = null,
    /// <summary>Stars 1..5 as Windows stores them in EXIF; null when unrated.</summary>
    int? Rating = null);
