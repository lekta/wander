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
    int? PixelHeight);
