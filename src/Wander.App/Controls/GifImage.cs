using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Wander.App.Controls;

/// <summary>
/// Minimal animated-GIF player built on plain WPF primitives — no NuGet
/// dependency. Decodes every frame up-front with <see cref="BitmapDecoder"/>
/// and advances them via a single <see cref="DispatcherTimer"/>, honouring
/// the per-frame delay stored in the GIF's Graphics Control Extension
/// (<c>/grctlext/Delay</c>, encoded in 1/100s units).
///
/// Why not <see cref="MediaElement"/>: WPF MediaElement support for animated
/// GIFs depends on the Media Foundation codec set installed on the machine
/// and historically renders quirky frame timings. Our manual decode is small
/// and predictable.
///
/// Why not WebView2: a browser engine would play any GIF, but spinning one
/// up has multi-second cold-start cost and a heavy DOM for a 100 KB image —
/// disproportionate.
///
/// Trade-off accepted: the whole GIF is held in memory while previewed
/// (one <see cref="BitmapSource"/> per frame). Fine for ordinary chat /
/// reaction GIFs; if we later want to preview 200 MB film-strip GIFs this
/// becomes the place to add lazy / streaming decode.
/// </summary>
public sealed class GifImage : Image {

    /// <summary>
    /// Source URI of the GIF on disk. <c>null</c> stops playback and
    /// clears the displayed frame.
    /// </summary>
    public static readonly DependencyProperty GifUriProperty =
        DependencyProperty.Register(
            nameof(GifUri),
            typeof(Uri),
            typeof(GifImage),
            new PropertyMetadata(null, OnGifUriChanged));

    public Uri? GifUri {
        get => (Uri?)GetValue(GifUriProperty);
        set => SetValue(GifUriProperty, value);
    }


    private List<BitmapSource> _frames = new();
    private List<int> _delaysMs = new();
    private int _currentFrame;
    private DispatcherTimer? _timer;


    public GifImage() {
        Unloaded += (_, _) => Stop();
    }


    private static void OnGifUriChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
        var self = (GifImage)d;
        self.Stop();
        if (e.NewValue is Uri uri) {
            self.Load(uri);
        } else {
            self.Source = null;
        }
    }

    private void Load(Uri uri) {
        try {
            // OnLoad caches the bytes immediately so the file isn't kept
            // locked — important because the user may delete / move the
            // file while the preview is up.
            var decoder = BitmapDecoder.Create(
                uri,
                BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad);

            _frames = new List<BitmapSource>(decoder.Frames.Count);
            _delaysMs = new List<int>(decoder.Frames.Count);
            foreach (var f in decoder.Frames) {
                _frames.Add(f);
                _delaysMs.Add(ExtractDelayMs(f));
            }
        } catch {
            // Corrupt / unsupported GIF — fall back to whatever WPF can
            // decode as a single still image. Better than a blank pane.
            try {
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.UriSource = uri;
                bi.EndInit();
                bi.Freeze();
                Source = bi;
            } catch {
                Source = null;
            }
            return;
        }

        if (_frames.Count == 0) {
            return;
        }

        _currentFrame = 0;
        Source = _frames[0];

        // Cap to the GIF's natural pixel size so small animations don't
        // upscale to fill the preview pane. StretchDirection=DownOnly on
        // the Image base class should already enforce this, but pinning
        // MaxWidth/MaxHeight makes the contract explicit and survives
        // frame-by-frame Source swaps without WPF having to re-measure.
        var first = _frames[0];
        if (first.PixelWidth > 0 && first.PixelHeight > 0) {
            MaxWidth = first.PixelWidth;
            MaxHeight = first.PixelHeight;
        }

        if (_frames.Count == 1) {
            // Static GIF — no timer needed.
            return;
        }

        _timer = new DispatcherTimer(DispatcherPriority.Render) {
            Interval = TimeSpan.FromMilliseconds(_delaysMs[0]),
        };
        _timer.Tick += OnTick;
        _timer.Start();
    }


    private static int ExtractDelayMs(BitmapFrame frame) {
        // Per-frame GIF delay is in /grctlext/Delay in units of 1/100 s.
        // GIFs often encode 0 for "as fast as possible"; browsers
        // historically clamp that to ~100 ms to avoid CPU melting and to
        // match common file expectations — we do the same.
        const int DefaultMs = 100;
        const int MinMs = 20;
        try {
            if (frame.Metadata is BitmapMetadata meta && meta.ContainsQuery("/grctlext/Delay")) {
                if (meta.GetQuery("/grctlext/Delay") is ushort delay) {
                    int ms = delay * 10;
                    return ms <= 10 ? DefaultMs : Math.Max(MinMs, ms);
                }
            }
        } catch {
            // metadata query fails on weird GIFs — fall through
        }
        return DefaultMs;
    }


    private void OnTick(object? sender, EventArgs e) {
        if (_frames.Count == 0) {
            Stop();
            return;
        }
        _currentFrame = (_currentFrame + 1) % _frames.Count;
        Source = _frames[_currentFrame];
        if (_timer is not null) {
            _timer.Interval = TimeSpan.FromMilliseconds(_delaysMs[_currentFrame]);
        }
    }


    private void Stop() {
        if (_timer is not null) {
            _timer.Stop();
            _timer.Tick -= OnTick;
            _timer = null;
        }
        _frames.Clear();
        _delaysMs.Clear();
        _currentFrame = 0;
        // Reset cap so the next loaded GIF starts unconstrained until its
        // own dimensions are known.
        MaxWidth = double.PositiveInfinity;
        MaxHeight = double.PositiveInfinity;
    }
}
