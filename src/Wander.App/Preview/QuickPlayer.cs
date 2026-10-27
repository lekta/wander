using System.Windows.Media;
using System.Windows.Threading;

namespace Wander.App.Preview;

/// <summary>
/// The play button on a sound file in the list (decision 2026-10-27): a
/// track heard without selecting it, so a folder of sounds is gone through
/// by ear with the selection and the preview pane left where they are.
///
/// <para>
/// One sound at a time in the whole window: playing here pauses the
/// preview pane (<see cref="Starting"/>), and the pane playing stops this
/// (<see cref="Stop"/>). Played by a <see cref="MediaPlayer"/>, as the
/// pane plays audio; UI thread only.
/// </para>
/// </summary>
public static class QuickPlayer {
    /// <summary>How often the position is told while playing - smooth enough for a cell's playhead.</summary>
    private const int TickMs = 50;

    private static readonly MediaPlayer _player = new();
    private static readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(TickMs) };

    private static string? _path;
    private static double _volume = 0.5;

    // Where to start, as a share of the track, until the file is open and
    // its length known - a position set before that is ignored.
    private static double _startAt;


    static QuickPlayer() {
        _player.MediaOpened += (_, _) => {
            if (_startAt > 0 && _player.NaturalDuration.HasTimeSpan) {
                _player.Position = _player.NaturalDuration.TimeSpan * _startAt;
            }
            _startAt = 0;
        };
        _player.MediaEnded += (_, _) => Stop();
        _player.MediaFailed += (_, _) => Stop();
        _timer.Tick += (_, _) => Changed?.Invoke();
    }


    /// <summary>A track is about to start here - whatever else plays, pauses.</summary>
    public static event Action? Starting;

    /// <summary>The track changed, stopped, or moved on.</summary>
    public static event Action? Changed;


    /// <summary>The file playing, or null.</summary>
    public static string? Path => _path;

    /// <summary>How far through the track it is, 0..1; 0 while its length is not known.</summary>
    public static double Progress {
        get {
            var duration = _player.NaturalDuration;

            return _path is null || !duration.HasTimeSpan || duration.TimeSpan <= TimeSpan.Zero
                ? 0
                : Math.Clamp(_player.Position / duration.TimeSpan, 0, 1);
        }
    }

    /// <summary>The preview pane's volume, kept in step by the pane.</summary>
    public static double Volume {
        get => _volume;
        set {
            _volume = value;
            _player.Volume = value;
        }
    }


    /// <summary>Plays <paramref name="path"/> from the start; stops it when it is the one playing.</summary>
    public static void Toggle(string path) {
        if (IsPlaying(path)) {
            Stop();

            return;
        }

        Play(path, 0);
    }

    /// <summary>Plays <paramref name="path"/> from <paramref name="share"/> of its length, 0..1; whatever played stops.</summary>
    public static void Play(string path, double share) {
        Starting?.Invoke();
        _player.Close();
        _path = path;
        _startAt = Math.Clamp(share, 0, 1);
        _player.Volume = _volume;
        _player.Open(new Uri(path));
        _player.Play();
        _timer.Start();
        Changed?.Invoke();
    }

    public static bool IsPlaying(string? path) {
        return _path is not null && string.Equals(_path, path, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Stops when the file playing is one of <paramref name="paths"/> or
    /// inside one - before a delete, a move or a rename takes it away, as
    /// the preview pane lets go (<c>PreviewController.Release</c>): the
    /// player holds the file open while it plays.
    /// </summary>
    public static void Release(IReadOnlyList<string> paths) {
        if (_path is not { } playing) {
            return;
        }

        foreach (string root in paths) {
            string trimmed = System.IO.Path.TrimEndingDirectorySeparator(root);
            if (string.Equals(playing, trimmed, StringComparison.OrdinalIgnoreCase)
                || playing.StartsWith(trimmed + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) {
                Stop();

                return;
            }
        }
    }

    public static void Stop() {
        if (_path is null) {
            return;
        }

        _timer.Stop();
        _player.Stop();
        _player.Close();
        _path = null;
        Changed?.Invoke();
    }
}
