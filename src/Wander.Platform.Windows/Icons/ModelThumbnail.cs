using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Wander.Core.Preview;

namespace Wander.Platform.Windows.Icons;

/// <summary>
/// A tile for a 3D model. Windows draws none - the 3D Viewer that did is
/// gone from new installs - so the model is read with Core's
/// <see cref="MeshFile"/> and drawn by <see cref="MeshRaster"/>, the view
/// the preview pane opens on. A 3MF that carries its slicer's picture of
/// the plate shows that instead: it has the colours and the layout the
/// slicer gave it. Null puts the caller back on the icon.
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
        if (EmbeddedThumbnail.TryRead(path) is { } picture && Fit(picture, side) is { } fitted) {
            return fitted;
        }

        try {
            if (new FileInfo(path).Length > MaxFileSize) {
                return null;
            }
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
            return null;
        }

        if (MeshFile.Read(path) is not { } mesh || MeshRaster.Render(mesh, side) is not { } image) {
            return null;
        }

        try {
            return TgaThumbnail.EncodePngAsync(image, side).GetAwaiter().GetResult();
        } catch {
            return null;
        }
    }


    /// <summary>A picture fitted into the square, on transparency, as a PNG; null when GDI+ cannot read it.</summary>
    private static byte[]? Fit(byte[] picture, int side) {
        try {
            using var input = new MemoryStream(picture);
            using var source = new Bitmap(input);
            using var canvas = new Bitmap(side, side, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(canvas)) {
                g.Clear(Color.Transparent);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                double scale = Math.Min((double)side / source.Width, (double)side / source.Height);
                int w = Math.Max(1, (int)Math.Round(source.Width * scale));
                int h = Math.Max(1, (int)Math.Round(source.Height * scale));
                g.DrawImage(source, new Rectangle((side - w) / 2, (side - h) / 2, w, h));
            }

            using var output = new MemoryStream();
            canvas.Save(output, ImageFormat.Png);

            return output.ToArray();
        } catch (Exception ex) when (ex is ArgumentException or OutOfMemoryException or ExternalException) {
            return null;
        }
    }
}
