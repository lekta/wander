using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Wander.Core.Imaging;

namespace Wander.App.Preview;

/// <summary>
/// The review helpers' way in and out of WPF (RAWHELPERS, step 2): the
/// picture on screen made into bytes for Core to measure, and Core's masks
/// made into something to draw over the picture or instead of it - once
/// for the picture fitted into the pane, once for the 1:1 zoom.
///
/// <para>
/// Two pictures go in: the one fitted into the pane and the biggest one
/// there is of the file, which the zoom draws. For a CR3 they differ (the
/// quick 1620-px preview and the 6000-px JPEG); otherwise they are one.
/// Anything about focus is measured on the big one only: shrunk, a missed
/// frame reads as crisp as the hit (stand 2026-09-22). The rest - clipping,
/// the levels, the tone curve - does not care, and is taken off a copy of
/// the fitted one no longer than 2560 px.
/// </para>
///
/// <para>
/// Everything here runs on a worker and hands back frozen objects: the
/// helpers are worked out off the UI thread and only the result crosses.
/// </para>
/// </summary>
internal static class ReviewOverlay {
    // Colours of the marks, by index into the palette of Marks(): 1..7 are
    // the clipped channels as bits, then shadows, then peaking.
    private const byte UnderIndex = 8;
    private const byte PeakIndex = 9;

    // A channel that clipped paints in its own colour, two channels in
    // their mix, all three white - the colours say which channel went, and
    // no theme should change that.
    private static readonly Color[] _clipColors = [
        Colors.Transparent,
        Color.FromRgb(255, 0, 0),
        Color.FromRgb(0, 255, 0),
        Color.FromRgb(255, 255, 0),
        Color.FromRgb(0, 0, 255),
        Color.FromRgb(255, 0, 255),
        Color.FromRgb(0, 255, 255),
        Color.FromRgb(255, 255, 255),
    ];


    /// <summary>
    /// Works out what <paramref name="request"/> asks for. What an earlier
    /// call learnt about the same pictures comes in as <paramref name="cache"/>
    /// and goes out with the result, so switching a second helper on does
    /// not scale or measure again.
    /// </summary>
    public static Result Build(
        BitmapSource fit, BitmapSource full, Request request, Cache? cache, CancellationToken ct) {
        var fitWork = ReferenceEquals(cache?.Fit, fit) ? cache!.FitWork : Working(fit);
        int w = fitWork.Width;
        int h = fitWork.Height;
        bool same = ReferenceEquals(full, fit) && w == fit.PixelWidth && h == fit.PixelHeight;
        var peaks = ReferenceEquals(cache?.Full, full) ? cache!.Peaks : null;
        BgraImage? fullWork = same ? fitWork : null;
        BgraImage Full() => fullWork ??= Working(full, int.MaxValue);
        ct.ThrowIfCancellationRequested();

        ImageSource? instead = null;
        ImageSource? zoomInstead = null;
        if (request.Gamma is { } gamma) {
            var lut = ToneCurve.Lut(gamma);
            instead = Toned(ToneCurve.Apply(fitWork, lut));
            if (request.FullReady) {
                zoomInstead = same ? instead : Toned(ToneCurve.Apply(Full(), lut));
            }
        }
        ct.ThrowIfCancellationRequested();

        if (request.Peaking && request.FullReady && peaks is null) {
            var big = Full();
            peaks = FocusPeaking.Mask(Sharpness.Crispness(Luma.Of(big), big.Width, big.Height));
        }
        ct.ThrowIfCancellationRequested();

        var af = request.AfPoints ? request.Af : null;
        var fitPeaks = request.Peaking && peaks is not null
            ? (same ? peaks : FocusPeaking.Shrink(peaks, full.PixelWidth, full.PixelHeight, w, h))
            : null;
        var overlay = Compose(
            Marks(w, h, request.Clipping ? Clipping.Mask(fitWork) : null, fitPeaks, request),
            af, w, h, Math.Max(w, h) / 700.0, request);
        ct.ThrowIfCancellationRequested();

        ImageSource? zoomOverlay = null;
        if (request.FullReady && (request.Clipping || fitPeaks is not null || af is not null)) {
            if (same) {
                zoomOverlay = overlay;
            } else {
                var clipped = request.Clipping ? Clipping.Mask(Full()) : null;
                zoomOverlay = Compose(
                    Marks(full.PixelWidth, full.PixelHeight, clipped, request.Peaking ? peaks : null, request),
                    af, full.PixelWidth, full.PixelHeight, 2, request);
            }
        }
        ct.ThrowIfCancellationRequested();

        var histogram = request.Histogram ? Histogram.Compute(fitWork) : null;
        double? score = request.Sharpness && request.FullReady ? Score(full, request.Af) : null;

        return new Result(
            instead, overlay, zoomInstead, zoomOverlay,
            histogram, histogram is null ? null : Outline(histogram, request.ChartWidth, request.ChartHeight),
            score,
            new Cache(fit, fitWork, full, peaks));
    }


    /// <summary>
    /// The picture as bytes, scaled down so its long side is at most
    /// <paramref name="longSide"/>. Never scaled up.
    /// </summary>
    public static BgraImage Working(BitmapSource source, int longSide = 2560) {
        double scale = Math.Min(1.0, longSide / (double)Math.Max(source.PixelWidth, source.PixelHeight));
        BitmapSource scaled = scale < 1.0 ? new TransformedBitmap(source, new ScaleTransform(scale, scale)) : source;
        BitmapSource bgra = scaled.Format == PixelFormats.Bgra32
            ? scaled
            : new FormatConvertedBitmap(scaled, PixelFormats.Bgra32, null, 0);

        int w = bgra.PixelWidth;
        int h = bgra.PixelHeight;
        int stride = w * 4;
        var pixels = new byte[stride * h];
        bgra.CopyPixels(pixels, stride, 0);

        return new BgraImage(pixels, w, h, stride);
    }


    /// <summary>
    /// The sharpness score (<see cref="Sharpness.Score"/>) of the picture at
    /// its own resolution, over <see cref="Sharpness.Area"/> - only that
    /// part of it is read. The gallery's probe reads the same part of the
    /// same JPEG, so the two numbers agree.
    /// </summary>
    public static double? Score(BitmapSource full, IReadOnlyList<AfPoint>? af) {
        var area = Sharpness.Area(full.PixelWidth, full.PixelHeight, af);
        if (area.Width < 3 || area.Height < 3) {
            return null;
        }

        BitmapSource bgra = full.Format == PixelFormats.Bgra32
            ? full
            : new FormatConvertedBitmap(full, PixelFormats.Bgra32, null, 0);
        int stride = area.Width * 4;
        var pixels = new byte[stride * area.Height];
        bgra.CopyPixels(new Int32Rect(area.X, area.Y, area.Width, area.Height), pixels, stride, 0);
        var crop = new BgraImage(pixels, area.Width, area.Height, stride);

        return Sharpness.Score(Sharpness.Crispness(Luma.Of(crop), crop.Width, crop.Height), crop.Width, crop.Height);
    }


    /// <summary>The picture after a tone curve, to show in place of the original.</summary>
    public static BitmapSource Toned(BgraImage toned) {
        var bitmap = BitmapSource.Create(
            toned.Width, toned.Height, 96, 96, PixelFormats.Bgra32, null, toned.Pixels, toned.Stride);
        bitmap.Freeze();

        return bitmap;
    }


    /// <summary>
    /// The red, green and blue levels as three outlines to fill, one point
    /// per unit of <paramref name="width"/>. Scaled to the tallest bin of
    /// the three short of the two ends: a clipped frame piles its pixels
    /// into bin 255, and scaled to that spike the rest of the chart would
    /// be a flat line. The spike itself then stands at full height, which
    /// is what it has to say.
    /// </summary>
    public static PointCollection[] Outline(HistogramData data, double width, double height) {
        int peak = 1;
        foreach (var bins in new[] { data.Red, data.Green, data.Blue }) {
            for (int i = 1; i < 255; i++) {
                peak = Math.Max(peak, bins[i]);
            }
        }

        return [Line(data.Red), Line(data.Green), Line(data.Blue)];

        PointCollection Line(int[] bins) {
            int columns = Math.Max(1, (int)width);
            var points = new PointCollection(columns + 2) { new Point(0, height) };
            for (int c = 0; c < columns; c++) {
                int from = c * 256 / columns;
                int to = Math.Max(from + 1, (c + 1) * 256 / columns);
                int tallest = 0;
                for (int i = from; i < to; i++) {
                    tallest = Math.Max(tallest, bins[i]);
                }
                points.Add(new Point(c, height * (1 - Math.Min(1.0, tallest / (double)peak))));
            }
            points.Add(new Point(columns, height));
            points.Freeze();

            return points;
        }
    }


    /// <summary>
    /// Clipping flags and peaking marks as one picture, four bits a pixel -
    /// at 6000 x 4000 that is 12 MB where a colour picture would be 96.
    /// Peaking over clipping: the edges are lines, the clipped areas
    /// patches, and a line under a patch would be lost. Null when there is
    /// nothing to mark.
    /// </summary>
    private static BitmapSource? Marks(int w, int h, byte[]? clipped, byte[]? peaks, Request request) {
        if (clipped is null && peaks is null) {
            return null;
        }

        int stride = (w + 1) / 2;
        var pixels = new byte[stride * h];
        Parallel.For(0, h, y => {
            for (int x = 0; x < w; x++) {
                int i = y * w + x;
                byte index = 0;
                if (peaks is not null && peaks[i] != 0) {
                    index = PeakIndex;
                } else if (clipped is not null) {
                    int high = clipped[i] & (Clipping.Red | Clipping.Green | Clipping.Blue);
                    index = high != 0 ? (byte)high
                        : (clipped[i] & Clipping.Dark) != 0 ? UnderIndex
                        : (byte)0;
                }
                if (index != 0) {
                    int at = y * stride + x / 2;
                    pixels[at] |= (byte)(x % 2 == 0 ? index << 4 : index);
                }
            }
        });

        var colors = new List<Color>(_clipColors) { request.Under, request.Peak };
        var bitmap = BitmapSource.Create(w, h, 96, 96, PixelFormats.Indexed4, new BitmapPalette(colors), pixels, stride);
        bitmap.Freeze();

        return bitmap;
    }


    /// <summary>
    /// The marks and the autofocus frames as one picture of
    /// <paramref name="w"/> by <paramref name="h"/>. The frames are drawn
    /// <paramref name="pen"/> pixels wide - wider on the fitted picture,
    /// which the pane shrinks, than on the zoom, which it does not.
    /// </summary>
    private static ImageSource? Compose(
        BitmapSource? marks, IReadOnlyList<AfPoint>? af, int w, int h, double pen, Request request) {
        if (af is not { Count: > 0 }) {
            return marks;
        }

        var group = new DrawingGroup();
        // The whole frame, drawn in nothing: what makes the picture w by h
        // rather than the box round the frames.
        group.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, w, h))));
        if (marks is not null) {
            group.Children.Add(new ImageDrawing(marks, new Rect(0, 0, w, h)));
        }
        var focused = new Pen(new SolidColorBrush(request.AfFocused), pen * 1.5);
        var other = new Pen(new SolidColorBrush(request.AfOther), pen);
        foreach (var point in af.OrderBy(p => p.InFocus)) {
            var box = new Rect((point.X - point.W / 2) * w, (point.Y - point.H / 2) * h, point.W * w, point.H * h);
            group.Children.Add(new GeometryDrawing(null, point.InFocus ? focused : other, new RectangleGeometry(box)));
        }
        var image = new DrawingImage(group);
        image.Freeze();

        return image;
    }


    /// <summary>
    /// What to work out: the switches that are on, the curve to show the
    /// picture through (null for none), where the camera focused, the
    /// colours to mark with and the size of the chart.
    /// </summary>
    /// <param name="FullReady">
    /// The big picture is the one that stays. False for the moment a CR3
    /// shows its quick preview and its full JPEG is on the way: what is
    /// measured on the quick one would be withdrawn a moment later.
    /// </param>
    public sealed record Request(
        bool Peaking, bool Sharpness, bool Clipping, bool Histogram, bool AfPoints, double? Gamma,
        IReadOnlyList<AfPoint>? Af, bool FullReady,
        Color Peak, Color Under, Color AfFocused, Color AfOther, double ChartWidth, double ChartHeight);

    /// <summary>
    /// What came of it: <see cref="Instead"/> is drawn in place of the fitted
    /// picture and <see cref="Overlay"/> over it, <see cref="ZoomInstead"/>
    /// and <see cref="ZoomOverlay"/> the same for the zoom;
    /// <see cref="Outline"/> is the red, green and blue of
    /// <see cref="Histogram"/>.
    /// </summary>
    public sealed record Result(
        ImageSource? Instead, ImageSource? Overlay, ImageSource? ZoomInstead, ImageSource? ZoomOverlay,
        HistogramData? Histogram, PointCollection[]? Outline, double? Score, Cache Cache);

    /// <summary>What is worth keeping between two calls about the same pictures: the fitted copy and the peaking marks.</summary>
    public sealed record Cache(BitmapSource Fit, BgraImage FitWork, BitmapSource Full, byte[]? Peaks);
}
