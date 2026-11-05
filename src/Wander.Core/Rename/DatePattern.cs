using System.Globalization;
using System.Text;

namespace Wander.Core.Rename;

/// <summary>
/// The date format of <c>[D:...]</c> and <c>[X:...]</c>, by Excel's rule
/// rather than .NET's (decision of 2026-11-03). In .NET <c>mm</c> is always
/// the minutes, and <c>[D:mm-dd]</c> printed 39-52 where the month was
/// meant. Here case does not matter, and <c>m</c> is the month unless it
/// stands right after the hours or right before the seconds - what lies
/// between does not count - where it is the minutes. A template written for
/// .NET (<c>yyyy-MM-dd HH-mm</c>) prints what it printed.
///
/// <para>
/// Letters: <c>y</c> the year - one or two, two digits; three and more,
/// four - and <c>m</c>, <c>d</c>, <c>h</c> (24 hours), <c>s</c>: one letter
/// without a leading zero, two and more with it. Anything else is printed
/// as it stands, so no format is wrong.
/// </para>
/// </summary>
public static class DatePattern {
    private const string Letters = "ymdhs";


    public static string Format(DateTime value, string pattern) {
        var runs = Runs(pattern);
        var text = new StringBuilder(pattern.Length + 8);
        for (int r = 0; r < runs.Count; r++) {
            var (letter, start, length) = runs[r];
            if (letter == '\0') {
                text.Append(pattern, start, length);
                continue;
            }

            text.Append(letter switch {
                'y' => length >= 3
                    ? value.Year.ToString("0000", CultureInfo.InvariantCulture)
                    : (value.Year % 100).ToString("00", CultureInfo.InvariantCulture),
                'm' => Number(Neighbour(runs, r, -1) == 'h' || Neighbour(runs, r, 1) == 's' ? value.Minute : value.Month, length),
                'd' => Number(value.Day, length),
                'h' => Number(value.Hour, length),
                _ => Number(value.Second, length),
            });
        }

        return text.ToString();
    }


    /// <summary>The pattern cut into runs of one letter, any case, and runs of everything else ('\0').</summary>
    private static List<(char Letter, int Start, int Length)> Runs(string pattern) {
        var runs = new List<(char, int, int)>();
        int i = 0;
        while (i < pattern.Length) {
            char letter = char.ToLowerInvariant(pattern[i]);
            bool isLetter = Letters.Contains(letter);
            int end = i + 1;
            while (end < pattern.Length && (isLetter
                ? char.ToLowerInvariant(pattern[end]) == letter
                : !Letters.Contains(char.ToLowerInvariant(pattern[end])))) {
                end++;
            }

            runs.Add((isLetter ? letter : '\0', i, end - i));
            i = end;
        }

        return runs;
    }

    /// <summary>The letter of the nearest letter run before (-1) or after (1) run <paramref name="at"/>; '\0' when there is none.</summary>
    private static char Neighbour(List<(char Letter, int Start, int Length)> runs, int at, int step) {
        for (int r = at + step; r >= 0 && r < runs.Count; r += step) {
            if (runs[r].Letter != '\0') {
                return runs[r].Letter;
            }
        }

        return '\0';
    }

    private static string Number(int value, int length) {
        return value.ToString(length >= 2 ? "00" : "0", CultureInfo.InvariantCulture);
    }
}
