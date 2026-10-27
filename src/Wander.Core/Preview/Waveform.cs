namespace Wander.Core.Preview;

/// <summary>
/// The shape of a track: how loud it is along its length, as
/// <see cref="Buckets"/> slices of equal time. Drawn in the preview pane, in
/// the details column and on the tile; every surface narrows it to its own
/// width (<see cref="Slice"/>).
///
/// <para>
/// Two numbers a slice, both 0..255 of full scale: the peak - the outline
/// that shows a click or a hit - and the RMS - the body that shows how loud
/// it sounds. Absolute, not stretched to the loudest slice: a quiet file
/// looks quiet, which is part of what the eye is looking for in a folder of
/// sounds.
/// </para>
/// </summary>
public sealed record Waveform(byte[] Peaks, byte[] Rms, TimeSpan Duration) {
    /// <summary>Slices per track - finer than any surface draws it at.</summary>
    public const int Buckets = 512;

    /// <summary>
    /// The longest track whose shape the list and the tiles work out
    /// (decision 2026-10-27): an hour-long mix would hold the disk for
    /// seconds a cell. The preview pane draws any length.
    /// </summary>
    public static readonly TimeSpan ListLimit = TimeSpan.FromMinutes(10);


    /// <summary>
    /// How tall a level (0..1) is drawn, of the full height: a square root,
    /// so a sound at a tenth of full scale still has a shape (a third of the
    /// height) instead of a flat line, and the loud ones keep their order.
    /// </summary>
    public static double Height(double level) {
        return Math.Sqrt(Math.Clamp(level, 0, 1));
    }


    /// <summary>
    /// Peak and RMS (0..1) of the part of the track that falls on column
    /// <paramref name="index"/> of <paramref name="count"/> - the loudest
    /// peak and the mean loudness of the slices under it.
    /// </summary>
    public (double Peak, double Rms) Slice(int index, int count) {
        int from = (int)((long)index * Peaks.Length / count);
        int to = Math.Max(from + 1, (int)((long)(index + 1) * Peaks.Length / count));
        to = Math.Min(to, Peaks.Length);
        if (from >= to) {
            return (0, 0);
        }

        int peak = 0;
        double square = 0;
        for (int i = from; i < to; i++) {
            peak = Math.Max(peak, Peaks[i]);
            square += (double)Rms[i] * Rms[i];
        }

        return (peak / 255.0, Math.Sqrt(square / (to - from)) / 255.0);
    }
}

