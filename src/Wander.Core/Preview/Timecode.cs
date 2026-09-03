namespace Wander.Core.Preview;

/// <summary>
/// The clock under a player: "1:07", "0:04", "1:02:30".
/// </summary>
public static class Timecode {
    /// <summary>
    /// Formats a position or a length.
    ///
    /// <para>
    /// <paramref name="roundUp"/> is for a length, and it is not cosmetic:
    /// a clip that runs 0.6 s truncates to "0:00", which reads as a file
    /// with nothing in it. A length rounds up - there is at least a second
    /// of something there - while a position rounds down, because a
    /// position that rounds up would show the second it has not reached
    /// yet and would hit the length before the clip ended.
    /// </para>
    /// </summary>
    public static string Format(TimeSpan t, bool roundUp = false) {
        double seconds = t.TotalSeconds;
        if (seconds < 0) {
            seconds = 0;
        }

        long whole = roundUp ? (long)Math.Ceiling(seconds) : (long)Math.Floor(seconds);
        long hours = whole / 3600;
        long minutes = whole % 3600 / 60;
        long rest = whole % 60;

        return hours >= 1
            ? $"{hours}:{minutes:D2}:{rest:D2}"
            : $"{minutes}:{rest:D2}";
    }
}
