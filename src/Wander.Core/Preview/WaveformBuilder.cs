namespace Wander.Core.Preview;

/// <summary>
/// Folds a stream of samples into a <see cref="Waveform"/> without knowing
/// how long the stream is: slices fill up to twice the target count, then
/// neighbours merge in pairs and every slice from then on takes twice as
/// many frames. Memory stays at <c>2 x Buckets</c> whatever the length, and
/// a container that lies about its duration costs nothing.
/// </summary>
public sealed class WaveformBuilder {
    private readonly int _buckets;
    private readonly float[] _peak;
    private readonly double[] _square;
    private readonly long[] _frames;

    private long _framesPerBucket = 1;
    private int _count;
    private long _total;


    public WaveformBuilder(int buckets = Waveform.Buckets) {
        _buckets = buckets;
        _peak = new float[buckets * 2];
        _square = new double[buckets * 2];
        _frames = new long[buckets * 2];
    }


    /// <summary>Frames taken so far.</summary>
    public long Frames => _total;


    /// <summary>
    /// Takes interleaved samples of <paramref name="channels"/> channels, full
    /// scale +-1. A frame's peak is its loudest channel, its square - the mean
    /// over channels.
    /// </summary>
    public void Add(ReadOnlySpan<float> samples, int channels) {
        int frames = samples.Length / channels;
        for (int f = 0; f < frames; f++) {
            float peak = 0;
            double square = 0;
            for (int c = 0; c < channels; c++) {
                float s = samples[f * channels + c];
                float abs = Math.Abs(s);
                if (abs > peak) {
                    peak = abs;
                }

                square += (double)s * s;
            }

            Put(peak, square / channels);
        }
    }

    /// <summary>
    /// The shape of what was taken, <paramref name="duration"/> long; null
    /// when nothing was.
    /// </summary>
    public Waveform? Build(TimeSpan duration) {
        if (_total == 0) {
            return null;
        }

        int filled = _frames[_count] > 0 ? _count + 1 : _count;
        var peaks = new byte[_buckets];
        var rms = new byte[_buckets];
        for (int i = 0; i < _buckets; i++) {
            int from = (int)((long)i * filled / _buckets);
            int to = Math.Max(from + 1, (int)((long)(i + 1) * filled / _buckets));
            float peak = 0;
            double square = 0;
            long frames = 0;
            for (int j = from; j < to; j++) {
                peak = Math.Max(peak, _peak[j]);
                square += _square[j];
                frames += _frames[j];
            }

            peaks[i] = ToByte(peak);
            rms[i] = frames == 0 ? (byte)0 : ToByte(Math.Sqrt(square / frames));
        }

        return new Waveform(peaks, rms, duration);
    }


    private void Put(float peak, double square) {
        if (_frames[_count] == _framesPerBucket) {
            _count++;
            if (_count == _peak.Length) {
                Halve();
            }
        }

        if (peak > _peak[_count]) {
            _peak[_count] = peak;
        }

        _square[_count] += square;
        _frames[_count]++;
        _total++;
    }

    private void Halve() {
        for (int i = 0; i < _buckets; i++) {
            _peak[i] = Math.Max(_peak[2 * i], _peak[2 * i + 1]);
            _square[i] = _square[2 * i] + _square[2 * i + 1];
            _frames[i] = _frames[2 * i] + _frames[2 * i + 1];
        }

        Array.Clear(_peak, _buckets, _buckets);
        Array.Clear(_square, _buckets, _buckets);
        Array.Clear(_frames, _buckets, _buckets);
        _count = _buckets;
        _framesPerBucket *= 2;
    }

    private static byte ToByte(double value) {
        return (byte)Math.Round(Math.Clamp(value, 0, 1) * 255);
    }
}
