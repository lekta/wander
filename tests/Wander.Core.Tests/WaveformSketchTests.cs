using Wander.Core.Preview;

namespace Wander.Core.Tests;

/// <summary>A track's shape for its tile from windows along it (2026-11-09).</summary>
public class WaveformSketchTests {
    [Fact]
    public void ShortTracks_AreDecodedWhole_LongOnesSketched() {
        Assert.False(WaveformSketch.Worth(null));
        Assert.False(WaveformSketch.Worth(TimeSpan.FromSeconds(29)));
        Assert.True(WaveformSketch.Worth(TimeSpan.FromSeconds(30)));
        Assert.True(WaveformSketch.Worth(TimeSpan.FromMinutes(4)));
    }

    [Fact]
    public void Windows_StandInTheMiddleOfEqualShares_InsideTheTrack() {
        var length = TimeSpan.FromSeconds(WaveformSketch.Windows);
        var starts = WaveformSketch.Starts(length);

        Assert.Equal(WaveformSketch.Windows, starts.Length);
        // One second a share: the first window centred on half a second.
        Assert.Equal(TimeSpan.FromSeconds(0.5) - (WaveformSketch.WindowLength / 2), starts[0]);
        Assert.Equal(TimeSpan.FromSeconds(1), starts[1] - starts[0]);
        Assert.True(starts[^1] + WaveformSketch.WindowLength <= length);
        Assert.Equal(starts.OrderBy(s => s), starts);
    }

    [Fact]
    public void ATrackShorterThanAWindow_IsReadFromItsStart() {
        var starts = WaveformSketch.Starts(TimeSpan.FromMilliseconds(20), windows: 4);

        Assert.All(starts, s => Assert.Equal(TimeSpan.Zero, s));
    }

    [Fact]
    public void AFramePeak_IsItsLoudestChannel_ItsSquare_TheMeanOverChannels() {
        float peak = 0;
        double square = 0;
        float[] stereo = { 0.5f, -1f, 0.2f, 0.2f, 0.9f, 0.9f };

        int taken = WaveformSketch.Hear(stereo, 2, most: 2, ref peak, ref square);

        Assert.Equal(2, taken);
        Assert.Equal(1f, peak);
        Assert.Equal(((0.25 + 1) / 2) + ((0.04 + 0.04) / 2), square, 6);
    }

    [Fact]
    public void ASteadyTrack_IsOneLevelAlongIt() {
        var peaks = Enumerable.Repeat(0.5f, WaveformSketch.Windows).ToArray();
        var squares = Enumerable.Repeat(0.04, WaveformSketch.Windows).ToArray();

        var shape = WaveformSketch.Build(peaks, squares, TimeSpan.FromMinutes(4));

        Assert.Equal(Waveform.Buckets, shape.Peaks.Length);
        Assert.All(shape.Peaks, p => Assert.Equal(128, p));
        Assert.All(shape.Rms, r => Assert.Equal(51, r));
        Assert.Equal(TimeSpan.FromMinutes(4), shape.Duration);
    }

    [Fact]
    public void Silence_IsAFlatLine() {
        var shape = WaveformSketch.Build(new float[WaveformSketch.Windows], new double[WaveformSketch.Windows], TimeSpan.FromMinutes(1));

        Assert.All(shape.Peaks, p => Assert.Equal(0, p));
        Assert.All(shape.Rms, r => Assert.Equal(0, r));
    }

    /// <summary>One loud window: heard by its neighbours too, then falling off on a line, not in a step.</summary>
    [Fact]
    public void ALoudMoment_SpreadsToItsNeighbours_AndFadesOnALine() {
        var peaks = new float[WaveformSketch.Windows];
        var squares = new double[WaveformSketch.Windows];
        int loud = WaveformSketch.Windows / 2;
        peaks[loud] = 1f;
        squares[loud] = 1;

        var shape = WaveformSketch.Build(peaks, squares, TimeSpan.FromMinutes(4));
        int perWindow = Waveform.Buckets / WaveformSketch.Windows;
        int middle = (loud * perWindow) + (perWindow / 2);

        Assert.Equal(255, shape.Peaks[middle]);
        Assert.Equal(255, shape.Peaks[middle - perWindow]);
        // The neighbour after it is already on its way down to the next window.
        Assert.InRange(shape.Peaks[middle + perWindow], (byte)200, (byte)254);
        Assert.Equal(0, shape.Peaks[0]);
        Assert.Equal(0, shape.Peaks[^1]);
        // Past the neighbours, down slice by slice to silence.
        var fall = shape.Peaks[(middle + perWindow)..(middle + (3 * perWindow))];
        Assert.Equal(fall.OrderByDescending(p => p), fall);
        Assert.Contains(fall, p => p is > 0 and < 255);
    }
}
