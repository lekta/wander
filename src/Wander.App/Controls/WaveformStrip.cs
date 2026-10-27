using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Wander.App.Preview;
using Wander.Core;
using Wander.Core.Preview;

namespace Wander.App.Controls;

/// <summary>
/// A sound file's shape (<see cref="Waveform"/>), pulled off the UI thread
/// the way <see cref="AsyncIcon"/> pulls a thumbnail: what is known is drawn
/// at once, the rest is decoded two files at a time, and an answer that
/// arrives after the cell was recycled onto another row is dropped. Any
/// other file draws nothing.
///
/// <para>
/// The part already played is drawn in <see cref="PlayedBrush"/>, up to
/// <see cref="Progress"/> - set by the preview pane's transport - or, with
/// <see cref="FollowsPlayer"/>, by the list's play button
/// (<see cref="QuickPlayer"/>) when it is this file that plays.
/// </para>
///
/// <para>
/// With <see cref="PlaysOnClick"/> a click plays from where it lands, or
/// stops what plays (decision 2026-10-27). Under the mouse a mark says which:
/// a line with a play mark where it would start, a stop mark while it plays.
/// The first <see cref="SnapPx"/> pixels, and the empty width past a short
/// shape, start from the very beginning - aiming at the first pixel is not
/// a way to say "from the start". With <see cref="FollowsPlayer"/> the click
/// goes to the list's player; otherwise it is asked of the owner
/// (<see cref="StartRequested"/>, <see cref="StopRequested"/>).
/// </para>
/// </summary>
public sealed class WaveformStrip : FrameworkElement {
    /// <summary>How far from the left edge a click still means "from the start".</summary>
    private const double SnapPx = 14;

    /// <summary>The side of the play and stop marks over the mouse.</summary>
    private const double MarkPx = 8;

    /// <summary>Files decoded at once. The disk is shared with the thumbnails.</summary>
    private static readonly SemaphoreSlim _gate = new(2);

    public static readonly DependencyProperty FilePathProperty =
        DependencyProperty.Register(
            nameof(FilePath), typeof(string), typeof(WaveformStrip),
            new PropertyMetadata(null, (d, _) => ((WaveformStrip)d).Reload()));

    public static readonly DependencyProperty LimitedProperty =
        DependencyProperty.Register(
            nameof(Limited), typeof(bool), typeof(WaveformStrip),
            new PropertyMetadata(true, (d, _) => ((WaveformStrip)d).Reload()));

    public static readonly DependencyProperty ProgressProperty =
        DependencyProperty.Register(
            nameof(Progress), typeof(double), typeof(WaveformStrip),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FollowsPlayerProperty =
        DependencyProperty.Register(
            nameof(FollowsPlayer), typeof(bool), typeof(WaveformStrip),
            new PropertyMetadata(false));

    public static readonly DependencyProperty ShareProperty =
        DependencyProperty.Register(
            nameof(Share), typeof(double), typeof(WaveformStrip),
            new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty PlaysOnClickProperty =
        DependencyProperty.Register(
            nameof(PlaysOnClick), typeof(bool), typeof(WaveformStrip),
            new PropertyMetadata(false));

    public static readonly DependencyProperty IsPlayingProperty =
        DependencyProperty.Register(
            nameof(IsPlaying), typeof(bool), typeof(WaveformStrip),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty PeakBrushProperty =
        DependencyProperty.Register(
            nameof(PeakBrush), typeof(Brush), typeof(WaveformStrip),
            new FrameworkPropertyMetadata(Brushes.LightSteelBlue, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BodyBrushProperty =
        DependencyProperty.Register(
            nameof(BodyBrush), typeof(Brush), typeof(WaveformStrip),
            new FrameworkPropertyMetadata(Brushes.SteelBlue, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty PlayedBrushProperty =
        DependencyProperty.Register(
            nameof(PlayedBrush), typeof(Brush), typeof(WaveformStrip),
            new FrameworkPropertyMetadata(Brushes.DarkOrange, FrameworkPropertyMetadataOptions.AffectsRender));

    private Waveform? _shape;
    private CancellationTokenSource? _load;
    private int _generation;
    private bool _following;
    private double? _hoverX;


    public WaveformStrip() {
        // The palette's, by reference, so a switch of the theme repaints it.
        SetResourceReference(PeakBrushProperty, "WaveformPeak");
        SetResourceReference(BodyBrushProperty, "WaveformBody");
        SetResourceReference(PlayedBrushProperty, "WaveformPlayed");
        Loaded += (_, _) => Follow(FollowsPlayer);
        Unloaded += (_, _) => Follow(false);
    }


    /// <summary>The file whose shape is drawn.</summary>
    public string? FilePath {
        get => (string?)GetValue(FilePathProperty);
        set => SetValue(FilePathProperty, value);
    }

    /// <summary>Whether tracks over <see cref="Waveform.ListLimit"/> are left blank - true in the list, false in the preview pane.</summary>
    public bool Limited {
        get => (bool)GetValue(LimitedProperty);
        set => SetValue(LimitedProperty, value);
    }

    /// <summary>How much of the track is drawn as played, 0..1.</summary>
    public double Progress {
        get => (double)GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }

    /// <summary>Whether <see cref="Progress"/> comes from the list's play button.</summary>
    public bool FollowsPlayer {
        get => (bool)GetValue(FollowsPlayerProperty);
        set => SetValue(FollowsPlayerProperty, value);
    }

    /// <summary>
    /// How much of the width the shape takes, from the left, 0..1 - its
    /// length against the longest of a stack (<c>SoundStack.Shares</c>).
    /// </summary>
    public double Share {
        get => (double)GetValue(ShareProperty);
        set => SetValue(ShareProperty, value);
    }

    /// <summary>Whether a click plays from where it lands, or stops it when it plays.</summary>
    public bool PlaysOnClick {
        get => (bool)GetValue(PlaysOnClickProperty);
        set => SetValue(PlaysOnClickProperty, value);
    }

    /// <summary>Whether this file plays now - what a click does then is stop it.</summary>
    public bool IsPlaying {
        get => (bool)GetValue(IsPlayingProperty);
        set => SetValue(IsPlayingProperty, value);
    }

    public Brush PeakBrush {
        get => (Brush)GetValue(PeakBrushProperty);
        set => SetValue(PeakBrushProperty, value);
    }

    public Brush BodyBrush {
        get => (Brush)GetValue(BodyBrushProperty);
        set => SetValue(BodyBrushProperty, value);
    }

    public Brush PlayedBrush {
        get => (Brush)GetValue(PlayedBrushProperty);
        set => SetValue(PlayedBrushProperty, value);
    }

    /// <summary>Whether a shape is drawn - false while it loads, and for files without one.</summary>
    public bool HasShape => _shape is not null;

    /// <summary>A click asked to play from this share of the track, 0..1 - for an owner that plays it itself.</summary>
    public event EventHandler<double>? StartRequested;

    /// <summary>A click asked to stop - for an owner that plays it itself.</summary>
    public event EventHandler? StopRequested;


    /// <summary>
    /// As wide as it is let be - a column's width, the pane's up to its
    /// <c>MaxWidth</c> - and as tall as it is set; a shape has no size of its
    /// own.
    /// </summary>
    protected override Size MeasureOverride(Size constraint) {
        return new Size(
            double.IsInfinity(constraint.Width) ? 0 : constraint.Width,
            double.IsInfinity(constraint.Height) ? 0 : constraint.Height);
    }

    protected override void OnMouseMove(MouseEventArgs e) {
        base.OnMouseMove(e);
        if (PlaysOnClick && _shape is not null) {
            _hoverX = e.GetPosition(this).X;
            InvalidateVisual();
        }
    }

    protected override void OnMouseLeave(MouseEventArgs e) {
        base.OnMouseLeave(e);
        if (_hoverX is not null) {
            _hoverX = null;
            InvalidateVisual();
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e) {
        base.OnMouseLeftButtonUp(e);
        if (!PlaysOnClick || _shape is null || FilePath is not { } path) {
            return;
        }

        double share = StartAt(e.GetPosition(this).X) / Columns();
        if (FollowsPlayer) {
            if (IsPlaying) {
                QuickPlayer.Stop();
            } else {
                QuickPlayer.Play(path, share);
            }
        } else if (IsPlaying) {
            StopRequested?.Invoke(this, EventArgs.Empty);
        } else {
            StartRequested?.Invoke(this, share);
        }

        e.Handled = true;
    }

    protected override void OnRender(DrawingContext dc) {
        var size = RenderSize;

        // Transparent rather than nothing: the preview pane seeks on a click
        // anywhere along the strip, not only on the drawn part.
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(size));
        if (_shape is null || size.Width < 2 || size.Height < 2) {
            return;
        }

        int columns = Columns();
        dc.DrawGeometry(PeakBrush, null, Outline(_shape, columns, size.Height, rms: false));
        var body = Outline(_shape, columns, size.Height, rms: true);
        dc.DrawGeometry(BodyBrush, null, body);

        double played = Math.Clamp(Progress, 0, 1);
        if (played > 0) {
            dc.PushClip(new RectangleGeometry(new Rect(0, 0, columns * played, size.Height)));
            dc.DrawGeometry(PlayedBrush, null, body);
            dc.Pop();
        }

        if (PlaysOnClick && _hoverX is { } hover) {
            DrawMark(dc, hover, size.Height);
        }
    }


    private void Reload() {
        _generation++;
        _load?.Cancel();
        _load = null;
        _shape = null;
        InvalidateVisual();

        string? path = FilePath;
        var source = ServiceLocator.TryGet<IWaveformSource>();
        if (path is null || source is null || !AudioTags.IsAudio(path)) {
            return;
        }

        TimeSpan? limit = Limited ? Waveform.ListLimit : null;
        if (source.TryGetCached(path, limit, out var known)) {
            _shape = known;

            return;
        }

        _ = LoadAsync(source, path, limit, _generation);
    }

    private async Task LoadAsync(IWaveformSource source, string path, TimeSpan? limit, int generation) {
        var load = new CancellationTokenSource();
        _load = load;
        Waveform? shape;
        try {
            await _gate.WaitAsync(load.Token);
            try {
                shape = await Task.Run(() => source.Read(path, limit, load.Token), load.Token);
            } finally {
                _gate.Release();
            }
        } catch (OperationCanceledException) {
            return;
        }

        if (generation == _generation) {
            _shape = shape;
            InvalidateVisual();
        }
    }

    /// <summary>How many columns the shape takes - its share of the width.</summary>
    private int Columns() {
        return Math.Max(2, (int)(RenderSize.Width * Math.Clamp(Share, 0, 1)));
    }

    /// <summary>Where a click at <paramref name="x"/> starts, in columns: the start itself near the left edge and past the shape.</summary>
    private double StartAt(double x) {
        return x <= SnapPx || x > Columns() ? 0 : x;
    }

    /// <summary>
    /// What a click under the mouse would do: stopped, a line where it would
    /// start with a play mark on top; playing, a stop mark by the mouse.
    /// </summary>
    private void DrawMark(DrawingContext dc, double hover, double height) {
        var mark = new StreamGeometry();
        using (var ctx = mark.Open()) {
            if (IsPlaying) {
                double left = Math.Clamp(hover - MarkPx / 2, 0, Math.Max(0, RenderSize.Width - MarkPx));
                ctx.BeginFigure(new Point(left, 0), isFilled: true, isClosed: true);
                ctx.LineTo(new Point(left + MarkPx, 0), false, false);
                ctx.LineTo(new Point(left + MarkPx, MarkPx), false, false);
                ctx.LineTo(new Point(left, MarkPx), false, false);
            } else {
                double at = StartAt(hover);
                dc.DrawRectangle(PlayedBrush, null, new Rect(at, 0, 1.5, height));
                ctx.BeginFigure(new Point(at, 0), isFilled: true, isClosed: true);
                ctx.LineTo(new Point(at + MarkPx, MarkPx / 2), false, false);
                ctx.LineTo(new Point(at, MarkPx), false, false);
            }
        }

        mark.Freeze();
        dc.DrawGeometry(PlayedBrush, null, mark);
    }

    private void Follow(bool on) {
        if (on == _following) {
            return;
        }

        _following = on;
        if (on) {
            QuickPlayer.Changed += OnPlayerChanged;
            OnPlayerChanged();
        } else {
            QuickPlayer.Changed -= OnPlayerChanged;
        }
    }

    private void OnPlayerChanged() {
        bool playing = QuickPlayer.IsPlaying(FilePath);
        double progress = playing ? QuickPlayer.Progress : 0;
        if (playing != IsPlaying) {
            IsPlaying = playing;
        }
        if (progress != Progress) {
            Progress = progress;
        }
    }

    /// <summary>
    /// The shape as one closed figure: the top edge left to right, the
    /// bottom edge - its mirror - back. A column per device-independent
    /// pixel; the shape itself is narrowed to that by <see cref="Waveform.Slice"/>.
    /// </summary>
    private static StreamGeometry Outline(Waveform shape, int columns, double height, bool rms) {
        double middle = height / 2;
        var tops = new double[columns];
        for (int x = 0; x < columns; x++) {
            var (peak, body) = shape.Slice(x, columns);
            tops[x] = Math.Max(0.5, Waveform.Height(rms ? body : peak) * middle);
        }

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open()) {
            ctx.BeginFigure(new Point(0, middle - tops[0]), isFilled: true, isClosed: true);
            for (int x = 0; x < columns; x++) {
                ctx.LineTo(new Point(x, middle - tops[x]), false, false);
                ctx.LineTo(new Point(x + 1, middle - tops[x]), false, false);
            }
            for (int x = columns - 1; x >= 0; x--) {
                ctx.LineTo(new Point(x + 1, middle + tops[x]), false, false);
                ctx.LineTo(new Point(x, middle + tops[x]), false, false);
            }
        }

        geometry.Freeze();

        return geometry;
    }
}
