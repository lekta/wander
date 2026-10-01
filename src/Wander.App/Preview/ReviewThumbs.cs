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
/// curves and the clipping need none of that: they are taken off the
/// thumbnail itself.
/// </para>
///
/// <para>
/// Metered at two files at a time and kept by path, stamp and switches:
/// scrolling back to a cell costs a lookup. Only the cells on screen ever
/// ask (see <c>ReviewThumb</c>), so a folder of three hundred RAW files is
/// never measured whole. Of those, the host's rank decides who goes first
/// (<see cref="Prioritize"/>).
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

    private static Func<string, int> _rank = _ => 0;
    private static readonly RankedGate _gate = new(2, path => _rank(path));
    private static readonly Lock _lock = new();
    private static readonly Dictionary<string, ImageSource?> _cache = new(StringComparer.Ordinal);


    /// <summary>Which files are rendered first, lowest rank first. Set once, by the host.</summary>
    public static void Prioritize(Func<string, int> rank) {
        _rank = rank;
    }


    /// <summary>
    /// The cell's picture with the helpers on it, or null when there is
    /// nothing to draw. Frozen, off the UI thread.
    /// </summary>
    public static async Task<ImageSource?> RenderAsync(FileStamp stamp, string path, Ask ask, int side, CancellationToken ct) {
        string key = $"{path}|{stamp.Ticks}|{stamp.Size}|{side}|{(ask.Peaking ? "p" : "")}{(ask.Clipping ? "c" : "")}{(ask.Af ? "a" : "")}{ask.CurveTag}";
        lock (_lock) {
            if (_cache.TryGetValue(key, out var kept)) {
                return kept;
            }
        }

        await _gate.EnterAsync(path, ct);
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
        var metadata = ServiceLocator.TryGet<IImageMetadataReader>()?.Read(path);
        int? orientation = metadata?.Orientation;
        // Only the frames the camera called in focus: a cell has no room
        // for the grey rest of them.
        var af = ask.Af ? metadata?.AfPoints?.Where(p => p.InFocus).ToList() : null;
        if (af is { Count: 0 }) {
            af = null;
        }
        if (af is null && !ask.Peaking && !ask.Clipping && ask.Curve is null) {
            // The frames were all that was asked for, and this file has none.
            return null;
        }

        var thumb = Picture(path, side, orientation);
        if (thumb is null) {
            return null;
        }

        ct.ThrowIfCancellationRequested();
        var cell = ReviewOverlay.Working(thumb);
        ImageSource? toned = null;
        if (ask.Curve is { } curve) {
            toned = ReviewOverlay.Toned(ToneCurve.Apply(cell, curve));
        }

        byte[]? small = null;
        if (ask.Peaking && Picture(path, int.MaxValue, orientation) is { } full) {
            ct.ThrowIfCancellationRequested();
            var work = ReviewOverlay.Working(full, int.MaxValue);
            var map = Sharpness.Measure(Luma.Of(work), work.Width, work.Height, step: 2);
            ct.ThrowIfCancellationRequested();
            var marked = FocusPeaking.Continuous(FocusPeaking.Mask(map), map.Width, map.Height);
            small = FocusPeaking.Shrink(marked, map.Width, map.Height, cell.Width, cell.Height, CellDensity);
        }

        var clipped = ask.Clipping ? Clipping.Mask(cell) : null;
        var marks = ReviewOverlay.Marks(cell.Width, cell.Height, clipped, small, ask.Peak, ask.Under);
        if (marks is null && af is null) {
            return toned;
        }

        // Clipped to the picture: a frame's pen hanging over the edge would
        // grow the drawing, and the cell would show it that much off the
        // thumbnail under it (as in ReviewOverlay.Compose).
        var box = new Rect(0, 0, thumb.PixelWidth, thumb.PixelHeight);
        var group = new DrawingGroup { ClipGeometry = new RectangleGeometry(box) };
        group.Children.Add(new ImageDrawing(toned ?? thumb, box));
        if (marks is not null) {
            group.Children.Add(new ImageDrawing(marks, box));
        }
        if (af is not null) {
            var pen = new Pen(new SolidColorBrush(ask.AfMark), Math.Max(1.0, Math.Max(box.Width, box.Height) / 140.0));
            foreach (var point in af) {
                var frame = new Rect(
                    (point.X - point.W / 2) * box.Width, (point.Y - point.H / 2) * box.Height,
                    point.W * box.Width, point.H * box.Height);
                group.Children.Add(new GeometryDrawing(null, pen, new RectangleGeometry(frame)));
            }
        }
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


    /// <summary>What to make of a thumbnail: the marks, the autofocus frames, the curve, and the colours to mark in.</summary>
    /// <param name="CurveTag">Which curve it is, for the key that remembers the answer.</param>
    /// <param name="Under">Crushed shadows; clipped highlights paint in their own channels.</param>
    /// <param name="AfMark">The frames the camera focused by - quieter than in the pane, a cell is small.</param>
    public sealed record Ask(
        bool Peaking, bool Clipping, bool Af, byte[]? Curve, string CurveTag, Color Peak, Color Under, Color AfMark);
}
