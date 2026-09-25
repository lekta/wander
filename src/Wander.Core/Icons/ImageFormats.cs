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
        ".png", ".jpg", ".jpeg", ".jpe", ".jfif", ".bmp", ".ico", ".tif", ".tiff", ".gif", ".webp",
        // No codec in Windows: decoded by Wander (Imaging/TgaDecoder).
        ".tga",
        // Decoded by the system's codec from the Microsoft Store, when the
        // extensions for it are installed (PLAN B10).
        ".heic", ".heif",
        ".cr2", ".cr3", ".nef", ".arw", ".dng", ".raf", ".orf", ".rw2",
    };

    /// <summary>
    /// HEIF: what the WIC decoder turns and mirrors by the container's own
    /// transforms, ignoring the EXIF tag (stand 2026-09-25), and what needs
    /// extensions from the Microsoft Store to be read at all (PLAN B10).
    /// </summary>
    public static readonly IReadOnlySet<string> Heif = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
        ".heic", ".heif",
    };


    public static bool IsImage(string path) {
        return All.Contains(Path.GetExtension(path));
    }


    public static bool IsRaw(string path) {
        return Raw.Contains(Path.GetExtension(path));
    }


    public static bool IsHeif(string path) {
        return Heif.Contains(Path.GetExtension(path));
    }
}
