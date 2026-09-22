namespace Wander.Core.Imaging;

/// <summary>
/// Counts of pixels per level, 256 levels per channel, and the two shares
/// that go under the chart in words.
/// </summary>
/// <param name="Pixels">How many pixels were counted - the sum of any one channel's bins.</param>
/// <param name="Over">Share of pixels with at least one channel at 255.</param>
/// <param name="Under">Share of pixels with brightness 0.</param>
public sealed record HistogramData(
    int[] Red, int[] Green, int[] Blue, int[] Luma, int Pixels, double Over, double Under);
