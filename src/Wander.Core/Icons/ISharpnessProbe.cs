namespace Wander.Core.Icons;

/// <summary>
/// How sharp a photograph is, as one number (<c>Imaging.Sharpness.Score</c>)
/// - for the gallery's "sharp is lighter" pass. Every file is measured on a
/// copy of the same size, so the numbers of one folder compare.
/// </summary>
public interface ISharpnessProbe {
    /// <summary>The score of the picture at <paramref name="path"/>; null when it cannot be read.</summary>
    double? Score(string path);
}
