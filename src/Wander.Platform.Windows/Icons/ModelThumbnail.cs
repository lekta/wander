using Wander.Core;
using Wander.Core.Preview;

namespace Wander.Platform.Windows.Icons;

/// <summary>
/// A tile for a 3D model. Windows draws none - the 3D Viewer that did is
/// gone from new installs - so the model is read with Core's
/// <see cref="MeshFile"/> and drawn by <see cref="MeshRaster"/>, turned
/// the way it was last turned in the preview (<see cref="IModelViews"/>)
/// or at <see cref="ModelView.Default"/>. A 3MF never turned that carries
/// its slicer's picture of the plate shows that instead: it has the
/// colours and the layout the slicer gave it. Null puts the caller back
/// on the icon.
/// </summary>
internal static class ModelThumbnail {
    /// <summary>Models past this are left to the icon: the whole file is read to draw one tile, as for TGA.</summary>
    private const long MaxFileSize = 64L * 1024 * 1024;


    public static bool Supports(string path) {
        return MeshFile.IsMesh(path);
    }


    /// <summary>A PNG of <paramref name="side"/> pixels square, or null.</summary>
    public static byte[]? Render(string path, int side) {
        if (!Supports(path)) {
            return null;
        }
        var view = ServiceLocator.TryGet<IModelViews>()?.Get(path);
        if (view is null && EmbeddedThumbnail.TryRead(path) is { } picture && ThumbnailPicture.Fit(picture, side) is { } fitted) {
            return fitted;
        }

        try {
            if (new FileInfo(path).Length > MaxFileSize) {
                return null;
            }
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
            return null;
        }

        if (MeshFile.Read(path) is not { } mesh || MeshRaster.Render(mesh, side, view) is not { } image) {
            return null;
        }

        try {
            return TgaThumbnail.EncodePngAsync(image, side).GetAwaiter().GetResult();
        } catch {
            return null;
        }
    }
}
