using System.Runtime.InteropServices.WindowsRuntime;
using Wander.Core.FileSystem;
using Wander.Core.Icons;
using Wander.Core.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Wander.Platform.Windows.Icons;

/// <summary>
/// The sharpness score of one photograph, for the gallery's pass
/// (RAWHELPERS, step 6) - the same number the preview pane shows for it:
/// <see cref="Sharpness.Score"/> over <see cref="Sharpness.Area"/>, at the
/// picture's own resolution. Shrunk, a missed frame reads as crisp as the
/// hit (stand 2026-09-22), so only the area is decoded, not a smaller
/// copy of the whole.
///
/// <para>
/// A RAW is measured on the biggest JPEG it carries; one that carries none
/// is left unscored rather than decoded from the sensor, which costs a
/// second a frame. The pixels are read as stored, unturned, so the
/// autofocus area is turned back to match (<see cref="AfGeometry.Restore"/>).
/// </para>
/// </summary>
public sealed class SharpnessProbe : ISharpnessProbe {
    private readonly MetadataExtractorImageReader _metadata = new();


    public double? Score(string path) {
        try {
            var shot = _metadata.Read(path);
            var af = shot?.AfPoints is { } points ? AfGeometry.Restore(points, shot.Orientation) : null;

            if (ImageFormats.IsRaw(path)) {
                using var raw = SharedRead.Open(path);
                if (RawPreviewExtractor.Extract(raw, fullSize: true) is not { } jpeg) {
                    return null;
                }

                using var memory = new InMemoryRandomAccessStream();
                memory.WriteAsync(jpeg.AsBuffer()).AsTask().GetAwaiter().GetResult();
                memory.Seek(0);

                return ScoreAsync(memory, af).GetAwaiter().GetResult();
            }

            using var file = SharedRead.Open(path);
            using var stream = file.AsRandomAccessStream();

            return ScoreAsync(stream, af).GetAwaiter().GetResult();
        } catch {
            // Unreadable, not a picture after all, a codec that refused it:
            // the frame simply goes unscored.
            return null;
        }
    }


    private static async Task<double?> ScoreAsync(IRandomAccessStream input, IReadOnlyList<AfPoint>? af) {
        var decoder = await BitmapDecoder.CreateAsync(input);
        var area = Sharpness.Area((int)decoder.PixelWidth, (int)decoder.PixelHeight, af);
        if (area.Width < 3 || area.Height < 3) {
            return null;
        }

        var transform = new BitmapTransform {
            Bounds = new BitmapBounds {
                X = (uint)area.X, Y = (uint)area.Y, Width = (uint)area.Width, Height = (uint)area.Height,
            },
        };
        var pixels = await decoder.GetPixelDataAsync(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Ignore,
            transform,
            ExifOrientationMode.IgnoreExifOrientation,
            ColorManagementMode.DoNotColorManage);

        var crop = new BgraImage(pixels.DetachPixelData(), area.Width, area.Height, area.Width * 4);
        var crisp = Sharpness.Crispness(Luma.Of(crop), crop.Width, crop.Height);

        return Sharpness.Score(crisp, crop.Width, crop.Height);
    }
}
