using System.Text;

namespace Wander.Core.Search;

/// <summary>
/// Finds the query inside a file's text and cuts out the piece worth
/// showing.
///
/// <para>
/// A result row that says only "this file contains it somewhere" makes the
/// user open every hit to find out which one they meant. The line the
/// match sits on answers that from the list, which is most of what a
/// content search is for.
/// </para>
/// </summary>
public static class ContentMatcher {
    /// <summary>Characters of context around the match. About one line of the status strip.</summary>
    public const int SnippetLength = 160;

    /// <summary>How much of the snippet is spent on what comes before the match.</summary>
    private const int LeadingContext = 48;

    private const char Ellipsis = '…';


    /// <summary>
    /// Case-insensitive substring search, ordinal. Ordinal rather than
    /// culture-aware on purpose: a file manager's search has to be
    /// predictable across the mixed-language folders people actually have,
    /// and ordinal case folding still handles Cyrillic and every other
    /// alphabet — it is only the collation quirks (Turkish dotless i,
    /// ligature equivalence) that it leaves out, and those surprise far
    /// more often than they help.
    /// </summary>
    public static bool Contains(string text, string query) {
        return text.Contains(query, StringComparison.OrdinalIgnoreCase);
    }


    /// <summary>
    /// The first occurrence of <paramref name="query"/> as a piece of text
    /// to display, plus the 1-based line it was found on. False when the
    /// text does not contain the query at all.
    /// </summary>
    public static bool TryMatch(string text, string query, out string snippet, out int line) {
        snippet = "";
        line = 0;

        if (string.IsNullOrEmpty(query)) {
            return false;
        }

        int at = text.IndexOf(query, StringComparison.OrdinalIgnoreCase);
        if (at < 0) {
            return false;
        }

        line = LineNumberAt(text, at);
        snippet = Cut(text, at, query.Length);

        return true;
    }


    /// <summary>
    /// How many line breaks precede the offset, plus one. Counts <c>\n</c>
    /// only: every line ending in use either is <c>\n</c> or contains one,
    /// and the lone-<c>\r</c> files that would fool this stopped being
    /// written around the time the Macintosh grew a Unix underneath.
    /// </summary>
    private static int LineNumberAt(string text, int offset) {
        int line = 1;
        for (int i = 0; i < offset; i++) {
            if (text[i] == '\n') {
                line++;
            }
        }

        return line;
    }


    /// <summary>
    /// The window around the match, clipped to the line it sits on and
    /// then to <see cref="SnippetLength"/>. The second clip is what keeps a
    /// minified <c>.json</c> — one line, two megabytes long — from becoming
    /// a two-megabyte "snippet".
    /// </summary>
    private static string Cut(string text, int at, int matchLength) {
        int lineStart = at;
        while (lineStart > 0 && text[lineStart - 1] is not ('\n' or '\r')) {
            lineStart--;
        }

        int lineEnd = at + matchLength;
        while (lineEnd < text.Length && text[lineEnd] is not ('\n' or '\r')) {
            lineEnd++;
        }

        int start = Math.Max(lineStart, at - LeadingContext);
        int end = Math.Min(lineEnd, start + SnippetLength);

        var cut = new StringBuilder(SnippetLength + 2);
        if (start > lineStart) {
            cut.Append(Ellipsis);
        }
        AppendCollapsed(cut, text.AsSpan(start, end - start));
        if (end < lineEnd) {
            cut.Append(Ellipsis);
        }

        return cut.ToString();
    }


    /// <summary>
    /// Copies the span with every run of whitespace turned into one space.
    /// Indented code and the tab-padded columns of a log otherwise arrive
    /// as a snippet that is mostly nothing.
    /// </summary>
    private static void AppendCollapsed(StringBuilder into, ReadOnlySpan<char> span) {
        bool pendingSpace = false;
        foreach (char c in span) {
            if (char.IsWhiteSpace(c)) {
                pendingSpace = into.Length > 0;
                continue;
            }
            if (pendingSpace) {
                into.Append(' ');
                pendingSpace = false;
            }
            into.Append(c);
        }
    }
}
