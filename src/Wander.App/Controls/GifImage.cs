using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Wander.App.Controls;

/// <summary>
/// Animated-image player built on plain WPF primitives — no NuGet dependency.
/// Decodes every frame up-front with <see cref="BitmapDecoder"/>, composites
/// them onto a single logical-screen canvas (respecting GIF disposal modes),
/// and advances them via a <see cref="DispatcherTimer"/>.
///
/// <para>
/// Why composition is needed: GIFs commonly encode a frame as a sub-rectangle
/// that's only the pixels that changed since the previous frame. Decoders
/// (including WPF's WIC) hand those back as-is. Painting each raw frame
/// directly produces "white artifact" rendering, because the unchanged
/// background regions outside the frame's rect are left transparent and
/// composite over the white pane background. The GIF spec's
/// <c>Disposal Method</c> tells us how the previous frame's pixels should
/// persist (1 = keep, 2 = clear to background, 3 = restore prior state).
/// We replay that pipeline ourselves and snapshot the canvas per frame so
/// each entry in <see cref="_frames"/> is a self-contained image.
/// </para>
///
/// <para>
/// Why not <see cref="MediaElement"/>: MediaElement support for animated
/// GIFs is codec-pack-dependent and historically renders quirky timings.
/// Why not WebView2: a browser engine would play any animated image, but
/// spinning one up has multi-second cold-start cost — disproportionate.
/// </para>
///
/// <para>
/// WebP: routed through the same decoder. Modern Windows WIC includes a
/// WebP codec; if it surfaces multiple frames, they animate. WebP frames
/// generally encode their own disposal info under different metadata
/// queries — we keep the GIF disposal path as default, which corresponds
/// to "no disposal / keep canvas". For most full-frame WebP animations
/// this works fine.
/// </para>
///
/// Trade-off accepted: the whole sequence is held in memory while
/// previewed (one composited PARGB32 BitmapSource per frame at logical
/// screen size). Fine for ordinary chat / reaction GIFs.
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
        BitmapDecoder decoder;
        try {
            decoder = BitmapDecoder.Create(
                uri,
                BitmapCreateOptions.None,
                BitmapCacheOption.OnLoad);
        } catch {
            // Unsupported codec / corrupt file — try the simple still-image
            // path so the user at least sees the first frame.
            LoadStatic(uri);
            return;
        }

        if (decoder.Frames.Count == 0) {
            return;
        }

        // Single frame: just show it as a still — no animation machinery.
        if (decoder.Frames.Count == 1) {
            var only = decoder.Frames[0];
            only.Freeze();
            Source = only;
            return;
        }

        // Composite all frames against a logical-screen canvas.
        if (!TryComposeFrames(decoder, out _frames, out _delaysMs)) {
            LoadStatic(uri);
            return;
        }
        if (_frames.Count == 0) {
            return;
        }

        _currentFrame = 0;
        Source = _frames[0];
        // Native-size cap is enforced externally via MaxWidth/MaxHeight
        // bound to Source through BitmapPixelSizeConverter in MainWindow.xaml.

        if (_frames.Count == 1) {
            return;
        }

        _timer = new DispatcherTimer(DispatcherPriority.Render) {
            Interval = TimeSpan.FromMilliseconds(_delaysMs[0]),
        };
        _timer.Tick += OnTick;
        _timer.Start();
    }

    private void LoadStatic(Uri uri) {
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
    }


    // ------------------------------------------------------------------
    // Frame composition: canvas + per-frame disposal handling.
    // ------------------------------------------------------------------

    private const int LogicalScreenFallback = 1;        // used when /logscrdesc/* is missing

    private static bool TryComposeFrames(
        BitmapDecoder decoder,
        out List<BitmapSource> frames,
        out List<int> delaysMs) {
        frames = new List<BitmapSource>(decoder.Frames.Count);
        delaysMs = new List<int>(decoder.Frames.Count);

        // Logical screen dimensions from the GIF's Logical Screen Descriptor.
        // Fall back to the first frame's pixel size if the container metadata
        // doesn't give us one (e.g. WebP — different metadata keys).
        int screenW = decoder.Frames[0].PixelWidth;
        int screenH = decoder.Frames[0].PixelHeight;
        if (decoder.Metadata is BitmapMetadata containerMeta) {
            TryGetInt32(containerMeta, "/logscrdesc/Width", ref screenW);
            TryGetInt32(containerMeta, "/logscrdesc/Height", ref screenH);
        }
        if (screenW < LogicalScreenFallback || screenH < LogicalScreenFallback) {
            return false;
        }

        // Canvas stored as Pbgra32 byte[] — easier to alpha-blend without
        // running into WriteableBitmap Lock/Unlock ceremony per frame.
        int stride = screenW * 4;
        var canvas = new byte[screenH * stride];
        byte[]? saved = null;

        int prevDisposal = 0;
        int prevLeft = 0, prevTop = 0, prevW = 0, prevH = 0;

        foreach (BitmapFrame frame in decoder.Frames) {
            // Apply previous frame's disposal *before* drawing the next.
            //  1 = "do not dispose"  — leave canvas alone (default fall-through).
            //  2 = "background"      — clear previous frame's rect.
            //  3 = "previous"        — restore the snapshot we saved earlier.
            if (prevDisposal == 2) {
                ClearRect(canvas, stride, prevLeft, prevTop, prevW, prevH, screenH);
            } else if (prevDisposal == 3 && saved is not null) {
                Buffer.BlockCopy(saved, 0, canvas, 0, canvas.Length);
            }

            // Read this frame's metadata.
            int left = 0, top = 0, disposal = 0;
            if (frame.Metadata is BitmapMetadata fm) {
                TryGetInt32(fm, "/imgdesc/Left", ref left);
                TryGetInt32(fm, "/imgdesc/Top", ref top);
                TryGetInt32(fm, "/grctlext/Disposal", ref disposal);
            }

            // Save canvas BEFORE drawing if this frame's disposal is
            // "restore to previous" — that's the state we'll restore to.
            if (disposal == 3) {
                saved = (byte[])canvas.Clone();
            }

            // Composite this frame.
            CompositeFrame(canvas, screenW, screenH, frame, left, top);

            // Snapshot the canvas as a frozen BitmapSource for display.
            // Clone the byte[] so subsequent edits to `canvas` don't show
            // through in the BitmapSource's pixel buffer.
            var snapshot = BitmapSource.Create(
                screenW, screenH, 96, 96,
                PixelFormats.Pbgra32, null,
                (byte[])canvas.Clone(),
                stride);
            snapshot.Freeze();
            frames.Add(snapshot);

            delaysMs.Add(ExtractDelayMs(frame));

            prevDisposal = disposal;
            prevLeft = left; prevTop = top;
            prevW = frame.PixelWidth; prevH = frame.PixelHeight;
        }

        return true;
    }

    private static void CompositeFrame(
        byte[] canvas, int canvasW, int canvasH,
        BitmapSource frame, int x, int y) {
        // Normalise to Pbgra32 so canvas + source share a pixel format —
        // FormatConvertedBitmap handles palette → ARGB and premultiplication
        // for GIF's index-based frames.
        BitmapSource src = frame.Format == PixelFormats.Pbgra32
            ? frame
            : new FormatConvertedBitmap(frame, PixelFormats.Pbgra32, null, 0);

        int sw = src.PixelWidth;
        int sh = src.PixelHeight;
        int srcStride = sw * 4;
        var srcPixels = new byte[sh * srcStride];
        src.CopyPixels(srcPixels, srcStride, 0);

        int dstStride = canvasW * 4;
        int x0 = Math.Max(0, x);
        int y0 = Math.Max(0, y);
        int x1 = Math.Min(canvasW, x + sw);
        int y1 = Math.Min(canvasH, y + sh);

        for (int dstY = y0; dstY < y1; dstY++) {
            int srcRowOff = (dstY - y) * srcStride;
            int dstRowOff = dstY * dstStride;
            for (int dstX = x0; dstX < x1; dstX++) {
                int sOff = srcRowOff + (dstX - x) * 4;
                byte sa = srcPixels[sOff + 3];
                if (sa == 0) {
                    continue;       // fully transparent — preserve canvas
                }
                int dOff = dstRowOff + dstX * 4;
                if (sa == 255) {
                    canvas[dOff] = srcPixels[sOff];
                    canvas[dOff + 1] = srcPixels[sOff + 1];
                    canvas[dOff + 2] = srcPixels[sOff + 2];
                    canvas[dOff + 3] = 255;
                } else {
                    // Source-over with premultiplied alpha:
                    //   result.rgb = src.rgb + dst.rgb * (1 - src.alpha)
                    //   result.a   = src.a   + dst.a   * (1 - src.alpha)
                    int inv = 255 - sa;
                    canvas[dOff] = (byte)(srcPixels[sOff] + (canvas[dOff] * inv + 127) / 255);
                    canvas[dOff + 1] = (byte)(srcPixels[sOff + 1] + (canvas[dOff + 1] * inv + 127) / 255);
                    canvas[dOff + 2] = (byte)(srcPixels[sOff + 2] + (canvas[dOff + 2] * inv + 127) / 255);
                    canvas[dOff + 3] = (byte)(sa + (canvas[dOff + 3] * inv + 127) / 255);
                }
            }
        }
    }

    private static void ClearRect(byte[] canvas, int stride, int x, int y, int w, int h, int canvasH) {
        int x0 = Math.Max(0, x);
        int y0 = Math.Max(0, y);
        int x1 = Math.Min(stride / 4, x + w);
        int y1 = Math.Min(canvasH, y + h);
        for (int row = y0; row < y1; row++) {
            int rowOff = row * stride;
            for (int col = x0; col < x1; col++) {
                int o = rowOff + col * 4;
                canvas[o] = 0;
                canvas[o + 1] = 0;
                canvas[o + 2] = 0;
                canvas[o + 3] = 0;
            }
        }
    }


    // ------------------------------------------------------------------
    // Metadata helpers.
    // ------------------------------------------------------------------

    private static void TryGetInt32(BitmapMetadata meta, string query, ref int target) {
        try {
            if (meta.ContainsQuery(query)) {
                object? raw = meta.GetQuery(query);
                if (raw is not null) {
                    target = Convert.ToInt32(raw);
                }
            }
        } catch {
            // Bad metadata — keep the existing target value.
        }
    }

    private static int ExtractDelayMs(BitmapFrame frame) {
        // /grctlext/Delay is in 1/100 s units. GIFs sometimes encode 0
        // for "as fast as possible"; browsers historically clamp that to
        // ~100 ms to avoid runaway CPU and match common file expectations.
        // Convert.ToInt32 accepts ushort / int / uint alike, which is what
        // we hit in practice across WIC versions.
        const int DefaultMs = 100;
        const int MinMs = 20;
        if (frame.Metadata is BitmapMetadata meta) {
            try {
                if (meta.ContainsQuery("/grctlext/Delay")) {
                    object? raw = meta.GetQuery("/grctlext/Delay");
                    if (raw is not null) {
                        int delay = Convert.ToInt32(raw);
                        int ms = delay * 10;
                        return ms <= 10 ? DefaultMs : Math.Max(MinMs, ms);
                    }
                }
            } catch {
                // metadata query fails on weird files — fall through
            }
        }
        return DefaultMs;
    }


    // ------------------------------------------------------------------
    // Playback loop.
    // ------------------------------------------------------------------

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
    }
}
