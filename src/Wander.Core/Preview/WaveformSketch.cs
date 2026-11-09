namespace Wander.Core.Preview;

/// <summary>
/// A track's shape for its tile, read from short windows spread along it
/// rather than from all of it (2026-11-09). A tile draws a column a pixel,
/// and the whole of a four-minute MP3 cost 0,8-1,3 s a tile to decode (stand
/// on the owner's music); a folder of tracks waited for three of those.
/// The preview pane and the details column keep the whole track
/// (<see cref="IWaveformSource.Read"/>).
///
/// <para>
/// Each window stands for its share of the track: its loudest sample and
/// its mean square become the slices over that share. A window hears a
/// moment, not the share, so a peak between two windows is missed - the
/// shape is the track's outline, not its every hit.
/// </para>
/// </summary>
public static class WaveformSketch {
    /// <summary>Windows along the track: two columns of a 256-px tile each - a seek costs Media Foundation about 1,5 ms.</summary>
    public const int Windows = 128;

    /// <summary>
    /// Neighbours on each side a slice also hears: a 50-ms window alone
    /// gives a jagged outline, the loudest of three and their mean loudness
    /// read as the track does.
    /// </summary>
    public const int Spread = 1;

    /// <summary>How much of the track one window hears.</summary>
    public static readonly TimeSpan WindowLength = TimeSpan.FromMilliseconds(50);

    /// <summary>
    /// Shorter than this, a track is decoded whole: the windows would hear a
    /// third of it or more, and a short one decodes in tens of milliseconds.
    /// </summary>
    public static readonly TimeSpan MinLength = TimeSpan.FromSeconds(30);


    /// <summary>Whether a track this long is sketched; one that declares no length is decoded whole.</summary>
    public static bool Worth(TimeSpan? duration) {
        return duration is { } length && length >= MinLength;
    }


    /// <summary>Where each window starts: the track cut into equal shares, a window in the middle of each.</summary>
    public static TimeSpan[] Starts(TimeSpan duration, int windows = Windows) {
        var starts = new TimeSpan[windows];
        double share = (double)duration.Ticks / windows;
        long last = Math.Max(0, duration.Ticks - WindowLength.Ticks);
        for (int i = 0; i < windows; i++) {
            long middle = (long)(share * (i + 0.5));
            starts[i] = TimeSpan.FromTicks(Math.Clamp(middle - (WindowLength.Ticks / 2), 0, last));
        }

        return starts;
    }


    /// <summary>
    /// Takes up to <paramref name="most"/> frames of interleaved samples into a
    /// window's level - a frame's peak is its loudest channel, its square the
    /// mean over channels, as in <see cref="WaveformBuilder"/>. Returns the
    /// frames taken.
    /// </summary>
    public static int Hear(ReadOnlySpan<float> samples, int channels, int most, ref float peak, ref double square) {
        int frames = Math.Min(samples.Length / channels, most);
        for (int f = 0; f < frames; f++) {
            double sum = 0;
            for (int c = 0; c < channels; c++) {
                float s = samples[(f * channels) + c];
                peak = Math.Max(peak, Math.Abs(s));
                sum += (double)s * s;
            }

            square += sum / channels;
        }

        return frames;
    }


    /// <summary>
    /// The shape from what the windows heard: each of the
    /// <see cref="Waveform.Buckets"/> slices takes the window over its share.
    /// <paramref name="meanSquares"/> are per window, already divided by its frames.
    /// </summary>
    public static Waveform Build(IReadOnlyList<float> peaks, IReadOnlyList<double> meanSquares, TimeSpan duration) {
        var slicePeaks = new byte[Waveform.Buckets];
        var sliceRms = new byte[Waveform.Buckets];
        int windows = peaks.Count;
        // Each window with its neighbours first, then the slices between two
        // windows drawn on the line from one to the next: steps of a window's
        // width read as blocks on a 256-px tile.
        var heardPeak = new double[windows];
        var heardRms = new double[windows];
        for (int w = 0; w < windows; w++) {
            int from = Math.Max(0, w - Spread);
            int to = Math.Min(windows - 1, w + Spread);
            double square = 0;
            for (int n = from; n <= to; n++) {
                heardPeak[w] = Math.Max(heardPeak[w], peaks[n]);
                square += Math.Max(0, meanSquares[n]);
            }

            heardRms[w] = Math.Sqrt(square / (to - from + 1));
        }

        for (int i = 0; i < Waveform.Buckets; i++) {
            double at = Math.Clamp((((i + 0.5) * windows) / Waveform.Buckets) - 0.5, 0, windows - 1);
            int left = (int)at;
            int right = Math.Min(windows - 1, left + 1);
            double t = at - left;
            slicePeaks[i] = ToByte(heardPeak[left] + ((heardPeak[right] - heardPeak[left]) * t));
            sliceRms[i] = ToByte(heardRms[left] + ((heardRms[right] - heardRms[left]) * t));
        }

        return new Waveform(slicePeaks, sliceRms, duration);
    }


    private static byte ToByte(double value) {
        return (byte)Math.Round(Math.Clamp(value, 0, 1) * 255);
    }
}
