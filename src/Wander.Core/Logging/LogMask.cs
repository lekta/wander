using System.Text;

namespace Wander.Core.Logging;

/// <summary>
/// What the session log may say about the user's files while real paths are
/// off (<c>AppSettings.LogPaths</c>, the default): a path or a name becomes a
/// token that keeps the shape - drive, depth, extension, which lines speak of
/// the same file - and loses the words.
/// <c>C:\Users\Anna\Photos\IMG_0001.jpg</c> reads
/// <c>&lt;C:\~3fa91c\~0b2e4d\~9c1f07\~5e2a88.jpg&gt;</c>.
///
/// <para>
/// Every name is hashed on its own with the string hash of the process,
/// which .NET seeds at random per process: within one session a folder is
/// the same token in every line and a file inside it shares the folder's
/// part, in the next session both are other tokens, and no token can be
/// looked up in a list of likely names. Case is ignored, as Windows ignores
/// it. A drive root alone says nothing about anybody and stays as it is.
/// </para>
///
/// <para>
/// Pure text in, text out. Which text goes through it is decided elsewhere:
/// each <c>{hole}</c> of a log line (<see cref="LogMessage"/>), what the
/// file logger writes, the crash report - and whether at all,
/// <see cref="Log.RevealPaths"/>.
/// </para>
/// </summary>
public static class LogMask {
    /// <summary>A quoted run longer than this is not a name, and is left to the path rule.</summary>
    private const int MaxQuoted = 400;

    /// <summary>How long an extension may be to survive the mask; longer is part of the name.</summary>
    private const int MaxExtension = 6;


    /// <summary>
    /// One path or one name as a token: <c>C:\a\b.txt</c> becomes
    /// <c>&lt;C:\~xxxxxx\~xxxxxx.txt&gt;</c>, a bare <c>b.txt</c> becomes
    /// <c>&lt;~xxxxxx.txt&gt;</c>. A drive root, a <c>shell:</c> location
    /// and a token already made come back unchanged.
    /// </summary>
    public static string Path(string? value) {
        if (string.IsNullOrEmpty(value) || IsToken(value) || IsShellLocation(value)) {
            return value ?? "";
        }

        int root = RootLength(value, 0);
        if (root == value.Length) {
            return value;
        }

        var text = new StringBuilder(value.Length + 8);
        text.Append('<');
        if (IsDriveRoot(value, 0)) {
            // One drive, one spelling: c:\ and C:\ are the same place.
            text.Append(char.ToUpperInvariant(value[0])).Append(":\\");
        } else {
            text.Append(value, 0, root);
        }
        int start = root;
        for (int i = root; i <= value.Length; i++) {
            if (i < value.Length && !IsSeparator(value[i])) {
                continue;
            }

            if (i > start) {
                AppendSegment(text, value.AsSpan(start, i - start));
            }
            if (i < value.Length) {
                text.Append('\\');
            }
            start = i + 1;
        }
        text.Append('>');

        return text.ToString();
    }


    /// <summary>
    /// Free text with every path in it masked: absolute paths (<c>C:\...</c>,
    /// <c>\\server\share\...</c>, <c>\\?\...</c>) and whatever stands in
    /// quotes - single, double, guillemets - which in a log line is a name or
    /// a path: an exception message quotes the path it is about, a line of
    /// the app quotes a name.
    ///
    /// <para>
    /// A path in quotes ends at the closing quote. One outside quotes ends
    /// at the first character no path can hold, at the <c> -&gt; </c> of a
    /// move, where the next path of a list begins, or at the end of the
    /// text - fed one <c>{hole}</c> at a time (<see cref="LogMessage"/>),
    /// that end is exact. The source path of a stack frame
    /// (<c> in X:\...\File.cs:line 12</c>) is the build's, not the user's,
    /// and is kept.
    /// </para>
    /// </summary>
    public static string Scrub(string? text) {
        if (string.IsNullOrEmpty(text)) {
            return text ?? "";
        }

        StringBuilder? masked = null;
        int copied = 0;
        int i = 0;
        while (i < text.Length) {
            if (QuotedRun(text, i) is { } run) {
                string token = Path(run.Inner);
                if (!string.Equals(token, run.Inner, StringComparison.Ordinal)) {
                    masked ??= new StringBuilder(text.Length + 16);
                    masked.Append(text, copied, i + 1 - copied).Append(token);
                    copied = run.End;
                }
                i = run.End + 1;

                continue;
            }

            if (StartsPath(text, i) && RootLength(text, i) is > 0 and var root) {
                int end = PathEnd(text, i + root);
                if (end > i + root && !IsStackFrameSource(text, i, end)) {
                    masked ??= new StringBuilder(text.Length + 16);
                    masked.Append(text, copied, i - copied).Append(Path(text[i..end]));
                    copied = end;
                }
                i = Math.Max(end, i + 1);

                continue;
            }

            i++;
        }

        if (masked is null) {
            return text;
        }

        masked.Append(text, copied, text.Length - copied);

        return masked.ToString();
    }


    private static void AppendSegment(StringBuilder text, ReadOnlySpan<char> segment) {
        if (IsMaskedSegment(segment)) {
            text.Append(segment);

            return;
        }

        int hash = string.GetHashCode(segment, StringComparison.OrdinalIgnoreCase);
        text.Append('~').Append((hash & 0xFFFFFF).ToString("x6"));
        int dot = segment.LastIndexOf('.');
        if (dot >= 0 && segment.Length - dot - 1 is > 0 and <= MaxExtension && IsExtension(segment[(dot + 1)..])) {
            // Lower case for the same reason the hash ignores case.
            text.Append('.').Append(segment[(dot + 1)..].ToString().ToLowerInvariant());
        }
    }

    private static bool IsExtension(ReadOnlySpan<char> extension) {
        foreach (char c in extension) {
            if (!char.IsAsciiLetterOrDigit(c)) {
                return false;
            }
        }

        return true;
    }

    /// <summary>A segment this class wrote itself: <c>~</c>, six hex digits, maybe an extension.</summary>
    private static bool IsMaskedSegment(ReadOnlySpan<char> segment) {
        if (segment.Length < 7 || segment[0] != '~') {
            return false;
        }
        for (int i = 1; i < 7; i++) {
            if (!char.IsAsciiHexDigitLower(segment[i])) {
                return false;
            }
        }

        return segment.Length == 7 || segment[7] == '.';
    }

    private static bool IsToken(string value) {
        return value.Length > 1 && value[0] == '<' && value[^1] == '>';
    }

    private static bool IsShellLocation(string value) {
        return value.StartsWith("shell:", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("::", StringComparison.Ordinal);
    }


    /// <summary>
    /// How much of <paramref name="text"/> at <paramref name="at"/> is the
    /// root a path keeps: <c>C:\</c>, <c>\\?\C:\</c>, <c>\\?\UNC\</c>,
    /// <c>\\.\</c>, the <c>\\</c> of a share (whose server and share names
    /// are masked like any other). Zero for a relative path or a name.
    /// </summary>
    private static int RootLength(string text, int at) {
        if (IsDriveRoot(text, at)) {
            return 3;
        }
        if (!(At(text, at, '\\') && At(text, at + 1, '\\'))) {
            return 0;
        }
        if ((At(text, at + 2, '?') || At(text, at + 2, '.')) && At(text, at + 3, '\\')) {
            if (IsDriveRoot(text, at + 4)) {
                return 7;
            }

            return string.Compare(text, at + 4, @"UNC\", 0, 4, StringComparison.OrdinalIgnoreCase) == 0 ? 8 : 4;
        }

        return at + 2 < text.Length && IsNameChar(text[at + 2]) ? 2 : 0;
    }

    private static bool IsDriveRoot(string text, int at) {
        return at + 2 < text.Length
            && char.IsAsciiLetter(text[at])
            && text[at + 1] == ':'
            && IsSeparator(text[at + 2]);
    }

    /// <summary>
    /// Whether a path may begin here: not in the middle of a word
    /// (<c>https:</c>), not inside a token already made, not at the second
    /// backslash of a share.
    /// </summary>
    private static bool StartsPath(string text, int at) {
        if (at == 0) {
            return true;
        }

        char before = text[at - 1];

        return !char.IsLetterOrDigit(before) && before is not ('<' or '\\' or '~' or '_');
    }

    /// <summary>
    /// Where an unquoted path with content from <paramref name="from"/> on
    /// ends: at a character no path can hold, at the arrow of
    /// <c>a -&gt; b</c>, where the next path of a list begins, or at a quote
    /// that closes around it. Trailing spaces, dots and list commas are not
    /// part of it - Windows keeps none of them at the end of a name.
    /// </summary>
    private static int PathEnd(string text, int from) {
        int end = text.Length;
        for (int j = from; j < text.Length; j++) {
            char c = text[j];
            if (c is '"' or '<' or '>' or '|' or '*' or '?' or ':' or '\r' or '\n' or '\t') {
                end = j;
                break;
            }
            if (IsClosingQuote(c) && ClosesRun(text, j)) {
                end = j;
                break;
            }
            if (j > from && text[j - 1] is ' ' or ',' or ';'
                && (IsDriveRoot(text, j) || (At(text, j, '\\') && At(text, j + 1, '\\')))) {
                end = j;
                break;
            }
        }

        while (end > from && text[end - 1] is ' ' or '.' or ',' or ';' or '-') {
            // A dash goes only as the start of an arrow: "a -> b" stops at "a -".
            if (text[end - 1] == '-' && !At(text, end, '>')) {
                break;
            }
            end--;
        }

        return end;
    }

    /// <summary>
    /// The quoted run starting at <paramref name="at"/>, with something in
    /// it: the index of its closing quote and what stands between. A quote
    /// counts as opening only where a word may start, and as closing only
    /// where one may end - the apostrophe of <c>doesn't</c> or of
    /// <c>Tom's.txt</c> is neither.
    /// </summary>
    private static (int End, string Inner)? QuotedRun(string text, int at) {
        char close = text[at] switch {
            '\'' => '\'',
            '"' => '"',
            '\u00AB' => '\u00BB',
            '\u201C' => '\u201D',
            '\u2018' => '\u2019',
            _ => '\0',
        };
        if (close == '\0' || (at > 0 && !OpensRun(text[at - 1]))) {
            return null;
        }

        int limit = Math.Min(text.Length, at + 1 + MaxQuoted);
        for (int j = at + 1; j < limit; j++) {
            if (text[j] is '\r' or '\n') {
                return null;
            }
            if (text[j] == close && ClosesRun(text, j)) {
                return j > at + 1 ? (j, text[(at + 1)..j]) : null;
            }
        }

        return null;
    }

    private static bool IsClosingQuote(char c) {
        return c is '\'' or '\u2019' or '\u00BB' or '\u201D';
    }

    private static bool OpensRun(char before) {
        return char.IsWhiteSpace(before) || before is '(' or '[' or '{' or '=' or ':' or ',';
    }

    private static bool ClosesRun(string text, int at) {
        return at + 1 >= text.Length
            || char.IsWhiteSpace(text[at + 1])
            || text[at + 1] is '.' or ',' or ';' or ':' or '!' or '?' or ')' or ']' or '}';
    }

    /// <summary>
    /// <c> in X:\...\File.cs:line 12</c> of a stack frame: where the build
    /// kept the source, not anything of the user's.
    /// </summary>
    private static bool IsStackFrameSource(string text, int start, int end) {
        return start >= 4
            && string.CompareOrdinal(text, start - 4, " in ", 0, 4) == 0
            && string.CompareOrdinal(text, end, ":line ", 0, 6) == 0;
    }

    private static bool IsSeparator(char c) {
        return c is '\\' or '/';
    }

    private static bool IsNameChar(char c) {
        return !char.IsWhiteSpace(c) && !IsSeparator(c) && c is not ('"' or '<' or '>' or '|' or '*' or '?' or ':');
    }

    private static bool At(string text, int at, char c) {
        return at < text.Length && text[at] == c;
    }
}
