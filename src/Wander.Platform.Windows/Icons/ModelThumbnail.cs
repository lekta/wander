using System.Runtime.InteropServices.WindowsRuntime;
using Wander.Core;
using Wander.Core.Diagnostics;
using Wander.Core.FileSystem;
using Wander.Core.Imaging;
using Wander.Core.Preview;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Wander.Platform.Windows.Icons;

/// <summary>
/// A tile for a 3D model. Windows draws none - the 3D Viewer that did is
/// gone from new installs - so the model is read with Core's
/// <see cref="MeshFile"/> and drawn by <see cref="MeshRaster"/>, with the
/// textures the preview shows, turned the way it was last turned in the
/// preview (<see cref="IModelViews"/>) or at <see cref="ModelView.Default"/>.
/// A 3MF never turned that carries its slicer's picture of the plate shows
/// that instead: it has the colours and the layout the slicer gave it. A
/// Blender backup is drawn darker. Null puts the caller back on the icon.
/// </summary>
internal static class ModelThumbnail {
    /// <summary>Models past this are left to the icon: the whole file is read to draw one tile, as for TGA.</summary>
    private const long MaxFileSize = 64L * 1024 * 1024;

    /// <summary>A picture is decoded to this many tile sides: an atlas gives a part only a corner of itself.</summary>
    private const int PictureSidesPerTile = 4;

    private const int MaxPictureSide = 2048;

    /// <summary>The most pictures one tile decodes; a model of more keeps its colours on the rest.</summary>
    private const int MaxPictures = 16;


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

        if (MeshFile.Read(path) is not { } mesh) {
            return null;
        }

        Dictionary<MeshTexture, BgraImage> pictures;
        using (PerfLog.Measure("bg.model-pictures")) {
            pictures = Pictures(mesh, side);
        }

        BgraImage? image;
        using (PerfLog.Measure("bg.model-raster")) {
            image = MeshRaster.Render(mesh, side, view, pictures);
        }
        if (image is null) {
            return null;
        }
        if (MeshFile.IsBackup(path)) {
            image = MeshRaster.Dimmed(image);
        }

        try {
            return TgaThumbnail.EncodePngAsync(image, side).GetAwaiter().GetResult();
        } catch {
            return null;
        }
    }


    /// <summary>The parts' pictures, each decoded once. One that will not decode is left out: its part keeps its colour.</summary>
    private static Dictionary<MeshTexture, BgraImage> Pictures(MeshData mesh, int side) {
        var pictures = new Dictionary<MeshTexture, BgraImage>();
        if (mesh.TexCoords is null) {
            return pictures;
        }

        int longest = Math.Min(side * PictureSidesPerTile, MaxPictureSide);
        foreach (var texture in mesh.Parts.Select(p => p.Texture).OfType<MeshTexture>().Distinct().Take(MaxPictures)) {
            try {
                byte[]? bytes = texture.Data ?? (texture.File is { } file ? SharedRead.ReadAllBytes(file) : null);
                var decoded = bytes is null
                    ? null
                    : Path.GetExtension(texture.Name).Equals(".tga", StringComparison.OrdinalIgnoreCase)
                        ? TgaDecoder.Decode(bytes)
                        : DecodeAsync(bytes, longest).GetAwaiter().GetResult();
                if (decoded is not null) {
                    pictures[texture] = decoded;
                }
            } catch (Exception ex) when (ex is not OutOfMemoryException) {
                // No codec, a cut file, a file gone: the part keeps its colour.
            }
        }

        return pictures;
    }


    /// <summary>Straight BGRA, no longer than <paramref name="longest"/> on its long side.</summary>
    private static async Task<BgraImage> DecodeAsync(byte[] bytes, int longest) {
        using var input = new InMemoryRandomAccessStream();
        await input.WriteAsync(bytes.AsBuffer());
        input.Seek(0);

        var decoder = await BitmapDecoder.CreateAsync(input);
        double scale = Math.Min(1.0, longest / (double)Math.Max(decoder.PixelWidth, decoder.PixelHeight));
        int width = (int)Math.Max(1, Math.Round(decoder.PixelWidth * scale));
        int height = (int)Math.Max(1, Math.Round(decoder.PixelHeight * scale));
        var pixels = await decoder.GetPixelDataAsync(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Straight,
            new BitmapTransform {
                ScaledWidth = (uint)width,
                ScaledHeight = (uint)height,
                InterpolationMode = BitmapInterpolationMode.Fant,
            },
            ExifOrientationMode.IgnoreExifOrientation,
            ColorManagementMode.DoNotColorManage);

        return new BgraImage(pixels.DetachPixelData(), width, height, width * 4);
    }
}
