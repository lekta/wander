using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Wander.Core.Logging;
using Windows.Graphics.DirectX;
using Windows.Graphics.Imaging;
using Windows.Media.Core;
using VideoFrame = Windows.Media.VideoFrame;
using WinRtPlayer = Windows.Media.Playback.MediaPlayer;

namespace Wander.App.Preview;

/// <summary>
/// The system's own player (WinRT <c>Windows.Media.Playback.MediaPlayer</c>),
/// for what WPF's engine does not open: WebM, Ogg (Theora, Vorbis, Opus),
/// AV1 - every codec installed from the Microsoft Store, which the older
/// engine does not see (stand 2026-09-29). The pane falls back to it when
/// its own player fails; a file it plays as well stays where it was.
///
/// <para>
/// Sound needs nothing more. A picture has no WPF element to land in, so
/// the player runs as a frame server: each frame is copied into a GPU
/// surface, from there into memory, into a <see cref="WriteableBitmap"/>
/// an <c>Image</c> shows (stand 2026-10-09: 3-5 ms a 1080p frame). One
/// frame in flight at a time - a frame that arrives while the last is
/// still being drawn is dropped, not queued. Frames larger than
/// <see cref="MaxFrameSide"/> are scaled down on the GPU.
/// </para>
/// </summary>
internal sealed class SystemPlayer : IDisposable {
    /// <summary>The long side of the frames copied out; a 4K picture in a pane does not need its every pixel.</summary>
    private const int MaxFrameSide = 1920;

    private readonly Dispatcher _dispatcher;
    private WinRtPlayer? _player;

    // Bumped by every open and close: events of a player let go of are
    // dropped by it.
    private int _generation;

    // The frame copy, touched only by the one frame in flight (_busy).
    private int _busy;
    private VideoFrame? _surface;
    private VideoFrame? _software;
    private byte[] _pixels = Array.Empty<byte>();


    public SystemPlayer(Dispatcher dispatcher) {
        _dispatcher = dispatcher;
    }


    public event Action? Opened;

    public event Action? Ended;

    public event Action? Failed;

    /// <summary>A new <see cref="Frame"/> bitmap - the size changed; the same one is drawn into otherwise.</summary>
    public event Action? FrameChanged;

    /// <summary>The picture, drawn into as frames arrive; null for sound and before the first frame.</summary>
    public WriteableBitmap? Frame { get; private set; }

    public bool IsOpen => _player is not null;

    public TimeSpan Position {
        get => _player?.PlaybackSession.Position ?? TimeSpan.Zero;
        set {
            if (_player is not null) {
                _player.PlaybackSession.Position = value;
            }
        }
    }

    /// <summary>The length, or null while it is not known.</summary>
    public TimeSpan? Duration => _player?.PlaybackSession.NaturalDuration is { Ticks: > 0 } length ? length : null;

    public int VideoWidth => (int)(_player?.PlaybackSession.NaturalVideoWidth ?? 0);

    public int VideoHeight => (int)(_player?.PlaybackSession.NaturalVideoHeight ?? 0);

    public double Volume {
        set {
            if (_player is not null) {
                _player.Volume = value;
            }
        }
    }


    public void Open(Uri uri, bool video, double volume) {
        Close();
        int generation = _generation;
        var player = new WinRtPlayer {
            AutoPlay = false,
            Volume = volume,
            IsVideoFrameServerEnabled = video,
        };
        player.MediaOpened += (_, _) => Post(generation, () => Opened?.Invoke());
        player.MediaEnded += (_, _) => Post(generation, () => Ended?.Invoke());
        player.MediaFailed += (_, e) => {
            Log.Info($"Preview: the system player did not open the file either - {e.Error} {e.ErrorMessage}");
            Post(generation, () => Failed?.Invoke());
        };
        if (video) {
            player.VideoFrameAvailable += (sender, _) => CopyFrame(sender, generation);
        }
        player.Source = MediaSource.CreateFromUri(uri);
        _player = player;
    }

    public void Play() {
        _player?.Play();
    }

    public void Pause() {
        _player?.Pause();
    }

    /// <summary>Lets the file go: the player is disposed, its late events and frames are dropped.</summary>
    public void Close() {
        _generation++;
        var player = _player;
        _player = null;
        if (player is not null) {
            try {
                player.Pause();
                player.Source = null;
            } catch (Exception ex) when (ex is COMException or InvalidOperationException) {
                // Already torn down by the system.
            }
            player.Dispose();
        }
        if (Frame is not null) {
            Frame = null;
            FrameChanged?.Invoke();
        }
    }

    public void Dispose() {
        Close();
        _surface?.Dispose();
        _software?.Dispose();
    }


    private void Post(int generation, Action action) {
        _dispatcher.BeginInvoke(() => {
            if (generation == _generation) {
                action();
            }
        });
    }

    /// <summary>On the player's thread: the current frame copied out and handed to the dispatcher to draw.</summary>
    private async void CopyFrame(WinRtPlayer sender, int generation) {
        if (Interlocked.Exchange(ref _busy, 1) == 1) {
            return;
        }

        bool handedOver = false;
        try {
            var session = sender.PlaybackSession;
            if (session.NaturalVideoWidth == 0 || session.NaturalVideoHeight == 0) {
                return;
            }

            double scale = Math.Min(1.0, MaxFrameSide / (double)Math.Max(session.NaturalVideoWidth, session.NaturalVideoHeight));
            int width = Math.Max(2, (int)Math.Round(session.NaturalVideoWidth * scale) & ~1);
            int height = Math.Max(2, (int)Math.Round(session.NaturalVideoHeight * scale) & ~1);
            if (_surface is null || _software is null || _pixels.Length != width * height * 4) {
                _surface?.Dispose();
                _software?.Dispose();
                _surface = VideoFrame.CreateAsDirect3D11SurfaceBacked(DirectXPixelFormat.B8G8R8A8UIntNormalized, width, height);
                _software = new VideoFrame(BitmapPixelFormat.Bgra8, width, height, BitmapAlphaMode.Ignore);
                _pixels = new byte[width * height * 4];
            }

            sender.CopyFrameToVideoSurface(_surface.Direct3DSurface);
            await _surface.CopyToAsync(_software);
            _software.SoftwareBitmap.CopyToBuffer(_pixels.AsBuffer());

            var pixels = _pixels;
            handedOver = true;
            _ = _dispatcher.BeginInvoke(DispatcherPriority.Render, () => {
                try {
                    if (generation == _generation) {
                        Draw(pixels, width, height);
                    }
                } finally {
                    Volatile.Write(ref _busy, 0);
                }
            });
        } catch (Exception) {
            // Everything, on purpose: this is async void on the player's
            // thread, where an escaping exception ends the process. A frame
            // lost - a player being closed, a device reset, no Direct3D
            // under remote desktop - and the next one tries again.
        } finally {
            if (!handedOver) {
                Volatile.Write(ref _busy, 0);
            }
        }
    }

    private void Draw(byte[] pixels, int width, int height) {
        if (Frame is null || Frame.PixelWidth != width || Frame.PixelHeight != height) {
            Frame = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgr32, null);
            FrameChanged?.Invoke();
        }
        Frame.WritePixels(new Int32Rect(0, 0, width, height), pixels, width * 4, 0);
    }
}
