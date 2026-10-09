using Wander.Core.FileSystem;
using Wander.Core.Imaging;

namespace Wander.Platform.Windows.Icons;

/// <summary>
/// A thumbnail for a Photoshop file, which Windows has no codec or
/// thumbnail provider for (unless Adobe's is installed). A small tile
/// takes the JPEG preview the file keeps (up to 160 px) - no decoding of
/// the picture; a large one decodes the flattened picture with Core's
/// <see cref="PsdDecoder"/>, falling back to the preview. Null puts the
/// caller back on the icon path.
/// </summary>
internal static class PsdThumbnail {
    /// <summary>The preview's size: a tile up to this takes it as it is.</summary>
    private const int PreviewSide = 160;

    /// <summary>Past this the flattened picture is not decoded for a tile; the preview stands in.</summary>
    private const long MaxDecodedFile = 256L * 1024 * 1024;


    public static bool Supports(string path) {
        return path.EndsWith(".psd", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".psb", StringComparison.OrdinalIgnoreCase);
    }


    /// <summary>A PNG of at most <paramref name="side"/> pixels on its long side, or null.</summary>
    public static byte[]? Render(string path, int side) {
        if (!Supports(path)) {
            return null;
        }

        PsdPicture? picture;
        try {
            using var file = SharedRead.Open(path);
            picture = PsdDecoder.Decode(file, composite: side > PreviewSide && file.Length <= MaxDecodedFile);
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OutOfMemoryException) {
            return null;
        }

        if (picture?.Composite is { } image) {
            try {
                return TgaThumbnail.EncodePngAsync(image, side).GetAwaiter().GetResult();
            } catch {
                return null;
            }
        }

        return picture?.Thumbnail is { } jpeg ? ThumbnailPicture.Fit(jpeg, side) : null;
    }
}
