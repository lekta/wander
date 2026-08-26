namespace Wander.Core.Icons;

/// <summary>EXIF / shot details for a single image, suitable for a preview footer.</summary>
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
    int? Orientation);
