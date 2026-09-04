namespace Wander.Core.Logging;

/// <summary>
/// Keeps a log finite when the same line arrives over and over. A
/// dispatcher fault that repeats on every frame wrote 16 277 identical
/// records and 141 MB in one session (0.4), and offered a crash bundle
/// sixty times a second while it did.
///
/// <para>
/// The first line of a signature is always written - that is the
/// chronology, and it is what anybody reading the log is looking for.
/// Repeats arriving less than <see cref="Window"/> apart are counted
/// instead of written, and the count comes out as one summary line: when
/// a different line arrives, when the logger is disposed, and every
/// <see cref="SummaryInterval"/> while the repeats keep coming - so a
/// flood that never stops still shows up in the timeline as still
/// happening. A gap longer than the window ends the run: the same line
/// after a quiet minute is news again.
/// </para>
///
/// <para>
/// Pure and clock-driven from the outside: the caller passes the time, so
/// three minutes of flooding is a test rather than a three-minute test.
/// Which lines are fed to it is the logger's decision - warnings and
/// errors; an INFO line is chronology and is never collapsed.
/// </para>
/// </summary>
public sealed class RepeatCollapser {
    /// <summary>How close two occurrences have to be to count as one run.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromSeconds(5);

    /// <summary>How often a run that is still going reports itself.</summary>
    public static readonly TimeSpan SummaryInterval = TimeSpan.FromSeconds(60);

    private string? _signature;
    private DateTime _lastUtc;
    private DateTime _runStartUtc;
    private DateTime _lastSummaryUtc;
    private int _repeats;


    /// <summary>
    /// What identifies a line for collapsing: the level, the message, the
    /// exception type and the frame it was thrown from. Two faults with the
    /// same message from different places stay two lines.
    /// </summary>
    public static string Signature(string level, string message, Exception? ex) {
        if (ex is null) {
            return $"{level}|{message}";
        }

        string? stack = ex.StackTrace;
        int end = stack?.IndexOf('\n') ?? -1;
        string frame = stack is null ? "" : (end < 0 ? stack : stack[..end]).Trim();

        return $"{level}|{message}|{ex.GetType().FullName}|{frame}";
    }


    /// <summary>
    /// What to do with the line that just arrived, and whether a summary of
    /// the run before it goes out first.
    /// </summary>
    public Decision Decide(string signature, DateTime nowUtc) {
        if (_signature == signature && nowUtc - _lastUtc <= Window) {
            _repeats++;
            _lastUtc = nowUtc;
            if (nowUtc - _lastSummaryUtc >= SummaryInterval) {
                var running = Take(nowUtc);
                _runStartUtc = nowUtc;
                _lastSummaryUtc = nowUtc;

                return new Decision(Write: false, running);
            }

            return new Decision(Write: false, null);
        }

        var pending = Take(_lastUtc);
        _signature = signature;
        _lastUtc = nowUtc;
        _runStartUtc = nowUtc;
        _lastSummaryUtc = nowUtc;

        return new Decision(Write: true, pending);
    }

    /// <summary>
    /// The summary owed for the run in progress, if any. For the last line
    /// of a session, which has nothing coming after it to flush it out.
    /// </summary>
    public Summary? Flush() {
        var pending = Take(_lastUtc);
        _signature = null;

        return pending;
    }


    private Summary? Take(DateTime untilUtc) {
        if (_repeats == 0) {
            return null;
        }

        var summary = new Summary(_repeats, untilUtc - _runStartUtc);
        _repeats = 0;

        return summary;
    }


    /// <summary>One decision: write the incoming line or not, summary or not.</summary>
    public readonly record struct Decision(bool Write, Summary? Repeats);

    /// <summary>How many repeats were swallowed, and over how long.</summary>
    public readonly record struct Summary(int Count, TimeSpan Span) {
        /// <summary>
        /// The line the log gets in their place; the logger appends the
        /// message the run was about, since lines of other levels may have
        /// been written in between. English, like every other line the
        /// logger writes itself - the log is a diagnostic, not user-facing
        /// text.
        /// </summary>
        public string Line => $"repeated {Count} times over {(int)Math.Round(Span.TotalSeconds)} s";
    }
}
