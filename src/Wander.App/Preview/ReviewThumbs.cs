using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Wander.Core;
using Wander.Core.Icons;
using Wander.Core.Imaging;

namespace Wander.App.Preview;

/// <summary>
/// The review helpers over a gallery thumbnail (RAWHELPERS): the same marks
/// and curves the preview pane draws, made small enough for a cell.
///
/// <para>
/// Peaking is measured on the frame at its own resolution, like everywhere
/// else - a mask taken from a thumbnail would say "crisp" about every
/// frame. Only every second pixel is asked about
/// (<see cref="Sharpness.Measure"/>'s step), which is three times less work
/// for an answer a two-hundred-pixel cell cannot tell apart. The tone
/// curves need none of that: they are a table over the thumbnail itself.
/// </para>
///
/// <para>
/// Metered at two files at a time and kept by path, stamp and switches:
/// scrolling back to a cell costs a lookup. Only the cells on screen ever
/// ask (see <c>ReviewThumb</c>), so a folder of three hundred RAW files is
/// never measured whole.
/// </para>
/// </summary>
internal static class ReviewThumbs {
    /// <summary>How many rendered cells are kept. A screenful is some thirty.</summary>
    private const int CacheLimit = 120;

    /// <summary>
    /// The share of a cell's block that has to be crisp for the cell to be
    /// marked there. One pixel of a cell covers a block some fifteen across,
    /// and "anything at all" marks nearly every block of nearly every frame.
    /// </summary>
    private const double CellDensity = 0.06;

    private static readonly SemaphoreSlim _gate = new(2);
    private static readonly Lock _lock = new();
    private static readonly Dictionary<string, ImageSource?> _cache = new(StringComparer.Ordinal);


    /// <summary>
    /// The cell's picture with the helpers on it, or null when there is
    /// nothing to draw. Frozen, off the UI thread.
    /// </summary>
    public static async Task<ImageSource?> RenderAsync(FileStamp stamp, string path, Ask ask, int side, CancellationToken ct) {
        string key = $"{path}|{stamp.Ticks}|{stamp.Size}|{side}|{(ask.Peaking ? "p" : "")}{ask.CurveTag}";
        lock (_lock) {
            if (_cache.TryGetValue(key, out var kept)) {
                return kept;
            }
        }

        await _gate.WaitAsync(ct);
        ImageSource? made;
        try {
            made = await Task.Run(() => Render(path, ask, side, ct), ct);
        } finally {
            _gate.Release();
        }

        lock (_lock) {
            if (_cache.Count >= CacheLimit) {
                _cache.Clear();
            }
            _cache[key] = made;
        }

        return made;
    }


    /// <summary>Drops what was made for one file - it has been rewritten underneath.</summary>
    public static void Invalidate(string path) {
        lock (_lock) {
            foreach (string key in _cache.Keys.Where(k => k.StartsWith(path + "|", StringComparison.OrdinalIgnoreCase)).ToList()) {
                _cache.Remove(key);
            }
        }
    }


    private static ImageSource? Render(string path, Ask ask, int side, CancellationToken ct) {
        int? orientation = ServiceLocator.TryGet<IImageMetadataReader>()?.Read(path)?.Orientation;
        var thumb = Picture(path, side, orientation);
        if (thumb is null) {
            return null;
        }

        ct.ThrowIfCancellationRequested();
        ImageSource? toned = null;
        if (ask.Curve is { } curve) {
            toned = ReviewOverlay.Toned(ToneCurve.Apply(ReviewOverlay.Working(thumb), curve));
        }

        ImageSource? marks = null;
        if (ask.Peaking && Picture(path, int.MaxValue, orientation) is { } full) {
            ct.ThrowIfCancellationRequested();
            var work = ReviewOverlay.Working(full, int.MaxValue);
            var map = Sharpness.Measure(Luma.Of(work), work.Width, work.Height, step: 2);
            ct.ThrowIfCancellationRequested();
            var marked = FocusPeaking.Continuous(FocusPeaking.Mask(map), map.Width, map.Height);
            var small = FocusPeaking.Shrink(
                marked, map.Width, map.Height, thumb.PixelWidth, thumb.PixelHeight, CellDensity);
            marks = ReviewOverlay.Marks(thumb.PixelWidth, thumb.PixelHeight, null, small, ask.Peak, ask.Peak);
        }

        if (marks is null) {
            return toned;
        }

        var group = new DrawingGroup();
        var box = new Rect(0, 0, thumb.PixelWidth, thumb.PixelHeight);
        group.Children.Add(new ImageDrawing(toned ?? thumb, box));
        group.Children.Add(new ImageDrawing(marks, box));
        var image = new DrawingImage(group);
        image.Freeze();

        return image;
    }


    /// <summary>
    /// The file's picture, upright: for a RAW the JPEG it carries (the
    /// quick one for a cell, the big one to measure), for anything else the
    /// file itself, decoded no larger than asked.
    /// </summary>
    private static BitmapSource? Picture(string path, int side, int? orientation) {
        BitmapSource? decoded;
        if (ImageFormats.IsRaw(path)) {
            bool full = side == int.MaxValue;
            decoded = ImageDecoder.RawPreviewBytes(path, full) is { } jpeg ? ImageDecoder.Stream(jpeg) : null;
            if (decoded is not null && !full && decoded.PixelWidth > side) {
                double scale = side / (double)Math.Max(decoded.PixelWidth, decoded.PixelHeight);
                var scaled = new TransformedBitmap(decoded, new ScaleTransform(scale, scale));
                scaled.Freeze();
                decoded = scaled;
            }
        } else {
            decoded = side == int.MaxValue ? ImageDecoder.File(path) : ImageDecoder.File(path, side);
        }

        return decoded is null ? null : ImageDecoder.ApplyOrientation(decoded, orientation);
    }


    /// <summary>What to make of a thumbnail: the marks, the curve, and the colour to mark in.</summary>
    /// <param name="CurveTag">Which curve it is, for the key that remembers the answer.</param>
    public sealed record Ask(bool Peaking, byte[]? Curve, string CurveTag, Color Peak);
}
