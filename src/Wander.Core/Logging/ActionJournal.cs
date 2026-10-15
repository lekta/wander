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
///
/// <para>
/// An entry is a headline and, for an operation, a line per item under it
/// (<see cref="Record"/>): "copied to E:\Video - 12 files, 12.3 GB"
/// says what happened, the lines say to which files. A plain status line is
/// an entry without lines (<see cref="Note"/>).
/// </para>
/// </summary>
public sealed class ActionJournal {
    /// <summary>How many lines of one entry are written out before the rest is counted.</summary>
    public const int LinesShown = 50;

    /// <summary>
    /// How many entries are kept. A long session is a few hundred status
    /// lines; past that the oldest go, because a journal that grows without
    /// limit is a leak with a nice name.
    /// </summary>
    private const int Limit = 500;

    /// <summary>
    /// The marks in front of a warning and an error. The journal is plain
    /// text opened in the user's editor, so these are the Unicode signs for
    /// the status bar's glyphs, which live in an icon font no editor has.
    /// </summary>
    private const string WarningMark = "⚠ ";
    private const string ErrorMark = "✖ ";

    /// <summary>What an entry's lines are indented by under its headline.</summary>
    private const string Indent = "  ";

    private readonly Queue<JournalEntry> _entries = new();
    private readonly Lock _lock = new();

    private string _last = "";


    /// <summary>How many entries are on record.</summary>
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
            Add(new JournalEntry(at, JournalKind.Note, text, Array.Empty<string>(), severity));
        }
    }

    /// <summary>
    /// Keeps one entry with its lines - an operation and the files it went
    /// through. Not dropped as a repeat: copying the same thing twice is two
    /// things that happened.
    /// </summary>
    public void Record(JournalEntry entry) {
        if (string.IsNullOrWhiteSpace(entry.Headline)) {
            return;
        }

        lock (_lock) {
            Add(entry);
        }
    }

    /// <summary>
    /// The journal as plain text, oldest first: per entry the time, two
    /// spaces, the headline - a warning or an error with its mark in front -
    /// and its lines under it, indented, the first <see cref="LinesShown"/>
    /// of them and then how many more.
    /// </summary>
    /// <param name="andMore">The line for what a long entry leaves out, with the count as {0}.</param>
    public string Render(string andMore = "... {0} more") {
        lock (_lock) {
            var lines = new List<string>(_entries.Count);
            foreach (var entry in _entries) {
                string mark = entry.Severity switch {
                    StatusSeverity.Warning => WarningMark,
                    StatusSeverity.Error => ErrorMark,
                    _ => "",
                };
                lines.Add($"{entry.At:yyyy-MM-dd HH:mm:ss}  {mark}{entry.Headline}");
                foreach (string line in entry.Lines.Take(LinesShown)) {
                    lines.Add(Indent + line);
                }
                if (entry.Lines.Count > LinesShown) {
                    lines.Add(Indent + string.Format(andMore, entry.Lines.Count - LinesShown));
                }
            }

            return string.Join(Environment.NewLine, lines);
        }
    }


    /// <summary>Under the lock.</summary>
    private void Add(JournalEntry entry) {
        _last = entry.Headline;
        _entries.Enqueue(entry);
        while (_entries.Count > Limit) {
            _entries.Dequeue();
        }
    }
}


/// <summary>What a journal entry is about - for whoever reads the journal by kind rather than by text.</summary>
public enum JournalKind {
    FolderOpened,
    FileOpened,
    Copied,
    Moved,
    Renamed,
    Recycled,
    Deleted,
    Restored,
    Created,
    Extracted,
    Cancelled,
    ActionRun,
    Error,

    /// <summary>A status line with nothing structured behind it.</summary>
    Note,
}


/// <summary>One thing that happened, in the user's words.</summary>
/// <param name="Headline">The line the status bar showed, or the operation's summary.</param>
/// <param name="Lines">One per file it went through - "IMG_0001.CR3", "a.txt -> b.txt"; empty for a plain line.</param>
public sealed record JournalEntry(
    DateTime At, JournalKind Kind, string Headline, IReadOnlyList<string> Lines,
    StatusSeverity Severity = StatusSeverity.Info);
