using System.IO;
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
/// <see cref="Sharpness.Score"/> over <see cref="Sharpness.Area"/> - the
/// middle of the frame - at the picture's own resolution. Shrunk, a missed frame reads as crisp as the
/// hit (stand 2026-09-22), so only the area is decoded, not a smaller
/// copy of the whole.
///
/// <para>
/// A RAW is measured on the biggest JPEG it carries; one that carries none
/// is left unscored rather than decoded from the sensor, which costs a
/// second a frame. The pixels are read as stored, unturned, which the
/// middle of the frame does not care about.
/// </para>
/// </summary>
public sealed class SharpnessProbe : ISharpnessProbe {
    /// <summary>How many answers are kept. Past this the cache starts over.</summary>
    private const int CacheLimit = 4000;


    private readonly Lock _lock = new();
    private readonly Dictionary<string, (FileStamp Stamp, double? Score)> _scored = new(StringComparer.OrdinalIgnoreCase);


    /// <summary>
    /// The score of the picture at <paramref name="path"/>. Measured once a
    /// session per file: the answer is kept until the file changes, so a
    /// helper switched off and on again, or a folder walked back into, costs
    /// a lookup rather than a decode.
    /// </summary>
    public double? Score(string path) {
        FileStamp stamp;
        try {
            var file = new FileInfo(path);
            stamp = FileStamp.Of(file.LastWriteTimeUtc, file.Length);
        } catch {
            return null;
        }

        lock (_lock) {
            if (_scored.TryGetValue(path, out var kept) && kept.Stamp == stamp) {
                return kept.Score;
            }
        }

        double? score = Measure(path);
        lock (_lock) {
            if (_scored.Count >= CacheLimit) {
                _scored.Clear();
            }
            _scored[path] = (stamp, score);
        }

        return score;
    }


    private static double? Measure(string path) {
        try {
            if (ImageFormats.IsRaw(path)) {
                using var raw = SharedRead.Open(path);
                if (RawPreviewExtractor.Extract(raw, fullSize: true) is not { } jpeg) {
                    return null;
                }

                using var memory = new InMemoryRandomAccessStream();
                memory.WriteAsync(jpeg.AsBuffer()).AsTask().GetAwaiter().GetResult();
                memory.Seek(0);

                return ScoreAsync(memory).GetAwaiter().GetResult();
            }

            using var file = SharedRead.Open(path);
            using var stream = file.AsRandomAccessStream();

            return ScoreAsync(stream).GetAwaiter().GetResult();
        } catch {
            // Unreadable, not a picture after all, a codec that refused it:
            // the frame simply goes unscored.
            return null;
        }
    }


    private static async Task<double?> ScoreAsync(IRandomAccessStream input) {
        var decoder = await BitmapDecoder.CreateAsync(input);
        var area = Sharpness.Area((int)decoder.PixelWidth, (int)decoder.PixelHeight);
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
        var map = Sharpness.Measure(Luma.Of(crop), crop.Width, crop.Height);
        var crisp = FocusPeaking.Continuous(FocusPeaking.Mask(map), map.Width, map.Height);

        return Sharpness.Score(map, crisp);
    }
}
