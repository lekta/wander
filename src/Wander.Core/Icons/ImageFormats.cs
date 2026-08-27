namespace Wander.Core.Icons;

/// <summary>
/// Which extensions Wander treats as a picture. One table, because two
/// tables that must agree eventually do not: the preview pane routes a file
/// by this, the gallery decides whether a folder is a folder of photographs
/// by this, and a format added in one place has to appear in the other or
/// the two disagree about the same file.
///
/// <para>
/// <see cref="Raw"/> is a subset, not a separate world: a RAW container is
/// a picture for every purpose here, and only differs in <em>how</em> it is
/// decoded (see <c>RawPreviewExtractor</c> — handing sensor data to WIC is
/// about a hundred times slower than the JPEG the file already carries).
/// </para>
/// </summary>
public static class ImageFormats {
    /// <summary>RAW containers, by extension.</summary>
    public static readonly IReadOnlySet<string> Raw = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
        ".cr2", ".cr3", ".nef", ".arw", ".dng", ".raf", ".orf", ".rw2",
    };

    /// <summary>
    /// Everything that is a picture, RAW included. Animated formats
    /// (<c>.gif</c>, <c>.webp</c>) are here too: they are pictures in a
    /// folder listing even though the preview pane plays them through a
    /// different control.
    /// </summary>
    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
        ".png", ".jpg", ".jpeg", ".bmp", ".ico", ".tif", ".tiff", ".gif", ".webp",
        ".cr2", ".cr3", ".nef", ".arw", ".dng", ".raf", ".orf", ".rw2",
    };


    public static bool IsImage(string path) {
        return All.Contains(Path.GetExtension(path));
    }


    public static bool IsRaw(string path) {
        return Raw.Contains(Path.GetExtension(path));
    }
}
