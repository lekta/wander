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
    /// <summary>
    /// RAW containers, by extension, maker by maker. Formats no camera
    /// writes any more - SRW, MRW, DCR, KDC, ERF, MEF, MOS - are left out
    /// (2026-09-30), all but the three asked for: CRW, SRF, SR2.
    /// </summary>
    public static readonly IReadOnlySet<string> Raw = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
        ".cr2", ".cr3", ".crw",
        ".nef", ".nrw",
        ".arw", ".srf", ".sr2",
        ".raf",
        ".orf", ".ori",
        ".rw2", ".rwl",
        ".pef",
        ".3fr", ".fff",
        ".iiq",
        ".dng",
    };

    /// <summary>
    /// Everything that is a picture, RAW included. Animated formats
    /// (<c>.gif</c>, <c>.webp</c>) are here too: they are pictures in a
    /// folder listing even though the preview pane plays them through a
    /// different control.
    /// </summary>
    public static readonly IReadOnlySet<string> All = new HashSet<string>(new[] {
        ".png", ".jpg", ".jpeg", ".jpe", ".jfif", ".bmp", ".ico", ".tif", ".tiff", ".gif", ".webp",
        ".jxr", ".wdp",
        // No codec in Windows: decoded by Wander (Imaging/TgaDecoder,
        // Imaging/PsdDecoder - the flattened picture, not the layers).
        ".tga", ".psd", ".psb",
        // Decoded by the system's codec from the Microsoft Store, when the
        // extensions for it are installed.
        ".heic", ".heif", ".hif", ".avif", ".jxl",
    }.Concat(Raw), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// HEIF: what the WIC decoder turns and mirrors by the container's own
    /// transforms, ignoring the EXIF tag (stand 2026-09-25), and what needs
    /// extensions from the Microsoft Store to be read at all. <c>.hif</c> is
    /// the same container from a camera.
    /// </summary>
    public static readonly IReadOnlySet<string> Heif = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
        ".heic", ".heif", ".hif",
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
