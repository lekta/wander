namespace Wander.Core.Icons;

/// <summary>Pixel dimensions of a picture, as one value the summary can count.</summary>
public readonly record struct PixelSize(int Width, int Height);


/// <summary>
/// What several pictures have in common, field by field. The rule is the
/// same for every field: one value everybody shares is the value; two or
/// three different ones are listed, in the order they were met; more than
/// that and the field says nothing at all - a list of forty shutter speeds
/// is not information about the selection. A picture that does not carry
/// a field simply does not vote on it.
/// </summary>
public sealed record ShotSummary(
    int Shots,
    IReadOnlyList<string> Cameras,
    IReadOnlyList<string> Iso,
    IReadOnlyList<string> Apertures,
    IReadOnlyList<string> Shutters,
    IReadOnlyList<string> FocalLengths,
    IReadOnlyList<PixelSize> PixelSizes) {

    /// <summary>How many different values a field may list before it is left out.</summary>
    public const int MaxDistinct = 3;


    /// <summary>Nothing to say beyond the count: every field varied too much or was absent.</summary>
    public bool IsEmpty =>
        Cameras.Count == 0 && Iso.Count == 0 && Apertures.Count == 0
        && Shutters.Count == 0 && FocalLengths.Count == 0 && PixelSizes.Count == 0;


    public static ShotSummary Aggregate(IReadOnlyList<ImageMetadata> shots) {
        return new ShotSummary(
            shots.Count,
            Shared(shots, m => CameraOf(m)),
            Shared(shots, m => m.IsoSpeed),
            Shared(shots, m => m.Aperture),
            Shared(shots, m => m.ShutterSpeed),
            Shared(shots, m => m.FocalLength),
            Shared(shots, m => m.PixelWidth is int w && m.PixelHeight is int h ? new PixelSize(w, h) : (PixelSize?)null));
    }


    /// <summary>Make and model as one name; null when the file names neither.</summary>
    private static string? CameraOf(ImageMetadata m) {
        string camera = string.Join(" ", new[] { m.CameraMake, m.CameraModel }.Where(s => !string.IsNullOrWhiteSpace(s)));

        return camera.Length > 0 ? camera : null;
    }

    private static IReadOnlyList<string> Shared(IReadOnlyList<ImageMetadata> shots, Func<ImageMetadata, string?> field) {
        return Distinct(shots.Select(field).Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v!));
    }

    private static IReadOnlyList<PixelSize> Shared(IReadOnlyList<ImageMetadata> shots, Func<ImageMetadata, PixelSize?> field) {
        return Distinct(shots.Select(field).Where(v => v is not null).Select(v => v!.Value));
    }

    /// <summary>
    /// The distinct values in order of first appearance - or none, once
    /// there are more of them than the summary is willing to list.
    /// </summary>
    private static IReadOnlyList<T> Distinct<T>(IEnumerable<T> values) where T : notnull {
        var seen = new List<T>();
        foreach (T value in values) {
            if (seen.Contains(value)) {
                continue;
            }
            seen.Add(value);
            if (seen.Count > MaxDistinct) {
                return Array.Empty<T>();
            }
        }

        return seen;
    }
}
