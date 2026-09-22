using System.Runtime.InteropServices.WindowsRuntime;
using Wander.Core.FileSystem;
using Wander.Core.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Wander.Platform.Windows.Icons;

/// <summary>
/// A thumbnail for a TGA texture (PLAN B8). Windows has no codec and no
/// thumbnail provider for the format, so the shell draws the generic icon;
/// this decodes it with Core's <see cref="TgaDecoder"/> and scales it the
/// way <see cref="RawThumbnail"/> does. A null return puts the caller back
/// on the icon path.
/// </summary>
internal static class TgaThumbnail {
    /// <summary>Textures bigger than this are left to the icon: the whole file is read to draw one tile.</summary>
    private const long MaxFileSize = 64L * 1024 * 1024;


    public static bool Supports(string path) {
        return path.EndsWith(".tga", StringComparison.OrdinalIgnoreCase);
    }


    /// <summary>A PNG of at most <paramref name="side"/> pixels on its long side, or null.</summary>
    public static byte[]? Render(string path, int side) {
        if (!Supports(path)) {
            return null;
        }

        BgraImage? image;
        try {
            using var file = SharedRead.Open(path);
            if (file.Length > MaxFileSize) {
                return null;
            }
            var bytes = new byte[file.Length];
            file.ReadExactly(bytes);
            image = TgaDecoder.Decode(bytes);
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
            return null;
        }
        if (image is null) {
            return null;
        }

        try {
            return RenderAsync(image, side).GetAwaiter().GetResult();
        } catch {
            return null;
        }
    }


    private static async Task<byte[]> RenderAsync(BgraImage image, int side) {
        double scale = Math.Min(1.0, side / (double)Math.Max(image.Width, image.Height));
        using var output = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, output);
        encoder.SetPixelData(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Straight,
            (uint)image.Width,
            (uint)image.Height,
            96, 96,
            image.Pixels);
        encoder.BitmapTransform.ScaledWidth = (uint)Math.Max(1, Math.Round(image.Width * scale));
        encoder.BitmapTransform.ScaledHeight = (uint)Math.Max(1, Math.Round(image.Height * scale));
        encoder.BitmapTransform.InterpolationMode = BitmapInterpolationMode.Fant;
        await encoder.FlushAsync();

        var bytes = new byte[output.Size];
        output.Seek(0);
        await output.ReadAsync(bytes.AsBuffer(), (uint)bytes.Length, InputStreamOptions.None);

        return bytes;
    }
}
