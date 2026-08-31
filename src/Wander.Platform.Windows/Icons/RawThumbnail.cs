using System.Runtime.InteropServices.WindowsRuntime;
using Wander.Core.Icons;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Wander.Platform.Windows.Icons;

/// <summary>
/// Builds a thumbnail for a RAW file out of the display JPEG the container
/// already carries, instead of asking the shell for one.
///
/// <para>
/// This is the difference between a gallery that fills in and one that
/// crawls. Measured on 40 Canon CR3 files, cold (nothing in the system's
/// thumbcache), 256 px output:
/// </para>
/// <list type="bullet">
///   <item><b>Shell</b> (<c>IShellItemImageFactory</c>) — 75 ms per file,
///   worst 350 ms, and it does not parallelise usefully.</item>
///   <item><b>This</b> — 20 ms per file on one thread, 3 ms per file across
///   eight, because a JPEG decoder asked for a 256 px result decodes at a
///   scaled DCT rather than at full size.</item>
/// </list>
///
/// <para>
/// Two details are load-bearing. The embedded preview carries no EXIF of
/// its own — the orientation lives in the RAW container around it — so it
/// is read from there and applied here; without that, every portrait
/// photograph in the folder lies on its side. And the decode goes through
/// WinRT's imaging rather than <c>System.Drawing</c>: GDI+ serialises on
/// an internal lock, so the same work on eight threads measured no faster
/// than on one (19 ms per file either way).
/// </para>
///
/// <para>
/// Nothing here is load-bearing for correctness: a null return puts the
/// caller back on the shell path, so a container we cannot read costs the
/// old speed, never a missing tile.
/// </para>
/// </summary>
internal static class RawThumbnail {
    private static readonly MetadataExtractorImageReader _metadata = new();


    /// <summary>
    /// A <paramref name="side"/>-pixel PNG of the RAW file's embedded
    /// preview, oriented the way the camera held it. Null when the file
    /// is not a RAW container, carries no usable preview, or cannot be
    /// read.
    /// </summary>
    public static byte[]? Render(string path, int side) {
        if (!ImageFormats.IsRaw(path)) {
            return null;
        }

        byte[]? jpeg;
        try {
            using var file = File.OpenRead(path);
            jpeg = RawPreviewExtractor.Extract(file);
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
            return null;
        }
        if (jpeg is null) {
            return null;
        }

        try {
            return RenderAsync(jpeg, Orientation(path), side).GetAwaiter().GetResult();
        } catch {
            // A malformed preview, a codec that refused it — the shell path
            // below is a complete answer on its own.
            return null;
        }
    }


    /// <summary>
    /// EXIF orientation of the RAW container, 1..8, defaulting to 1. Read
    /// off the container rather than the extracted JPEG because that is
    /// where cameras put it.
    /// </summary>
    private static int Orientation(string path) {
        try {
            return _metadata.Read(path)?.Orientation ?? 1;
        } catch {
            return 1;
        }
    }

    private static async Task<byte[]> RenderAsync(byte[] jpeg, int orientation, int side) {
        using var input = new InMemoryRandomAccessStream();
        await input.WriteAsync(jpeg.AsBuffer());
        input.Seek(0);

        var decoder = await BitmapDecoder.CreateAsync(input);

        // Orientations 5..8 turn the picture a quarter turn, so the side
        // that has to fit the box is the *other* one. Scaling against the
        // unrotated size would leave a portrait shot 256 px wide and
        // taller than the cell.
        bool quarterTurn = orientation is 5 or 6 or 7 or 8;
        uint uprightLong = Math.Max(
            quarterTurn ? decoder.PixelHeight : decoder.PixelWidth,
            quarterTurn ? decoder.PixelWidth : decoder.PixelHeight);
        double scale = Math.Min(1.0, side / (double)uprightLong);

        var transform = new BitmapTransform {
            ScaledWidth = (uint)Math.Max(1, Math.Round(decoder.PixelWidth * scale)),
            ScaledHeight = (uint)Math.Max(1, Math.Round(decoder.PixelHeight * scale)),
            InterpolationMode = BitmapInterpolationMode.Fant,
        };

        // IgnoreExifOrientation, not Respect: the JPEG's own EXIF is absent
        // or wrong here, and the container's value is applied on the
        // encoder below.
        var pixels = await decoder.GetPixelDataAsync(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Premultiplied,
            transform,
            ExifOrientationMode.IgnoreExifOrientation,
            ColorManagementMode.DoNotColorManage);

        using var output = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, output);
        encoder.SetPixelData(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Premultiplied,
            transform.ScaledWidth,
            transform.ScaledHeight,
            96, 96,
            pixels.DetachPixelData());
        encoder.BitmapTransform.Rotation = orientation switch {
            3 or 4 => BitmapRotation.Clockwise180Degrees,
            5 or 6 => BitmapRotation.Clockwise90Degrees,
            7 or 8 => BitmapRotation.Clockwise270Degrees,
            _ => BitmapRotation.None,
        };
        encoder.BitmapTransform.Flip = orientation is 2 or 4 or 5 or 7
            ? BitmapFlip.Horizontal
            : BitmapFlip.None;
        await encoder.FlushAsync();

        var bytes = new byte[output.Size];
        output.Seek(0);
        await output.ReadAsync(bytes.AsBuffer(), (uint)bytes.Length, InputStreamOptions.None);

        return bytes;
    }
}
