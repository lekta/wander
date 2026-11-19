using System.Globalization;
using System.Text.RegularExpressions;

namespace Wander.Core.Logging;

/// <summary>
/// The line a session's log ends with - "Session end: close, exit 0, up
/// 1:02:03" - and what the next start reads from it: a log without one is
/// a process that died without a word (audit L4), and its pid, from the
/// file's name, is what the Windows event log knows it by. Lines may follow
/// it - finalizers and the domain's handler still write - so it is looked
/// for in the tail, not on the last line.
/// </summary>
public static class SessionEnd {
    /// <summary>The window was closed.</summary>
    public const string Close = "close";

    /// <summary>Windows is logging off or shutting down.</summary>
    public const string SessionEnding = "session-ending";

    /// <summary>Closed to start again (a language change).</summary>
    public const string Restart = "restart";

    /// <summary>An exception nothing caught ended the process.</summary>
    public const string Fatal = "fatal";

    /// <summary>An exception on the way out; the process left at once.</summary>
    public const string ExitCrash = "exit-crash";

    private const string Marker = "Session end: ";

    // "12:00:00.000 INFO  Session end: ..." - the logger's time and level, then the line.
    private static readonly Regex _line = new(
        @"^\d\d:\d\d:\d\d\.\d{3} \S+ +" + Regex.Escape(Marker),
        RegexOptions.Multiline | RegexOptions.CultureInvariant);

    // "session-20261119-120000-4242.log" (FileLogger).
    private static readonly Regex _fileName = new(
        @"^session-\d{8}-\d{6}-(\d+)\.log$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);


    /// <summary>The line itself.</summary>
    public static string Line(string reason, int exitCode, TimeSpan up) {
        string uptime = string.Create(CultureInfo.InvariantCulture, $"{(int)up.TotalHours}:{up.Minutes:00}:{up.Seconds:00}");

        return $"{Marker}{reason}, exit {exitCode}, up {uptime}";
    }

    /// <summary>Whether <paramref name="tail"/> - the end of a session's log - holds the line.</summary>
    public static bool IsIn(string tail) {
        return _line.IsMatch(tail);
    }

    /// <summary>The process that wrote the log, from its file name; null for a name not of this shape.</summary>
    public static int? PidOf(string fileName) {
        var match = _fileName.Match(fileName);

        return match.Success && int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int pid)
            ? pid
            : null;
    }
}
