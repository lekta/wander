using Wander.App.Resources;

namespace Wander.App.Util;

/// <summary>
/// "Осталось" as a person reads it: seconds under a minute, minutes and
/// seconds under an hour, hours and minutes above. Never three units, and
/// never a decimal - the number is an estimate, and a precise-looking
/// estimate is a lie about how much it knows.
/// </summary>
public static class DurationFormat {
    public static string Format(TimeSpan span) {
        if (span < TimeSpan.Zero) {
            span = TimeSpan.Zero;
        }

        int seconds = (int)Math.Round(span.TotalSeconds);
        if (seconds < 60) {
            return string.Format(Strings.DurationSeconds, seconds);
        }

        if (seconds < 3600) {
            return string.Format(Strings.DurationMinutes, seconds / 60, seconds % 60);
        }

        int hours = seconds / 3600;

        return string.Format(Strings.DurationHours, hours, (seconds - hours * 3600) / 60);
    }
}
