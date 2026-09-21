namespace Wander.Core.Logging;

/// <summary>
/// What the status bar has said this session, with the time it said it.
///
/// <para>
/// The status line is where Wander answers "what just happened" - copied,
/// moved, undone, failed - and it holds one line at a time, so the answer
/// to the click before last is gone. That matters most exactly when it is
/// needed: an operation that reported a partial failure while the user was
/// looking at the file list, a rename that did something other than what
/// was expected. The journal keeps the lines so they can be read back.
/// </para>
///
/// <para>
/// Deliberately not the session log (<see cref="ILogger"/>). That one is
/// for diagnosing Wander and is written in the vocabulary of the code; this
/// is what was shown to the user, in their words, and it is theirs to read.
/// </para>
/// </summary>
public sealed class ActionJournal {
    /// <summary>
    /// How many lines are kept. A long session is a few hundred status
    /// lines; past that the oldest go, because a journal that grows without
    /// limit is a leak with a nice name.
    /// </summary>
    private const int Limit = 500;

    /// <summary>
    /// The marks in front of a warning and an error. The journal is plain
    /// text opened in the user's editor, so these are the Unicode signs for
    /// the status bar's glyphs, which live in an icon font no editor has.
    /// </summary>
    private const string WarningMark = "\u26A0 ";
    private const string ErrorMark = "\u2716 ";

    private readonly Queue<(DateTime At, string Text, StatusSeverity Severity)> _entries = new();
    private readonly Lock _lock = new();

    private string _last = "";


    /// <summary>How many lines are on record.</summary>
    public int Count {
        get {
            lock (_lock) {
                return _entries.Count;
            }
        }
    }


    /// <summary>
    /// Notes one status line. Blank lines and immediate repeats are
    /// dropped: the status bar is cleared and re-set constantly (a
    /// selection change rewrites the item count), and a journal of two
    /// hundred identical lines answers nothing.
    /// </summary>
    /// <param name="text">The line as the status bar showed it.</param>
    /// <param name="at">When.</param>
    /// <param name="severity">How much it matters - the mark it gets in <see cref="Render"/>.</param>
    public void Note(string? text, DateTime at, StatusSeverity severity = StatusSeverity.Info) {
        if (string.IsNullOrWhiteSpace(text)) {
            return;
        }

        lock (_lock) {
            if (string.Equals(text, _last, StringComparison.Ordinal)) {
                return;
            }
            _last = text;
            _entries.Enqueue((at, text, severity));
            while (_entries.Count > Limit) {
                _entries.Dequeue();
            }
        }
    }


    /// <summary>
    /// The journal as plain text, oldest first, a line each: the time, two
    /// spaces, the words - a warning or an error with its mark in front.
    /// </summary>
    public string Render() {
        lock (_lock) {
            var lines = new List<string>(_entries.Count);
            foreach (var (at, text, severity) in _entries) {
                string mark = severity switch {
                    StatusSeverity.Warning => WarningMark,
                    StatusSeverity.Error => ErrorMark,
                    _ => "",
                };
                lines.Add($"{at:yyyy-MM-dd HH:mm:ss}  {mark}{text}");
            }

            return string.Join(Environment.NewLine, lines);
        }
    }
}
