namespace Wander.Core.Search;

/// <summary>
/// What the user typed into the "name" field, as something that can answer
/// "does this file match".
///
/// <para>
/// Two languages in one field, and which one is in use is decided by the
/// text itself: a pattern containing <c>*</c> or <c>?</c> is a wildcard
/// pattern matched against the <em>whole</em> name, anything else is a
/// plain substring. That split is what Everything does, and it is the only
/// arrangement where both common cases stay one keystroke each — typing
/// <c>doc</c> to narrow a folder, and typing <c>*.cs</c> to mean the
/// extension. A single language would have made one of them worse:
/// substring-only cannot say "ends with .cs", and wildcard-only turns
/// every casual filter into <c>*doc*</c>.
/// </para>
///
/// <para>
/// Several patterns are separated by <c>;</c> and any of them matching is
/// enough — <c>*.cs;*.xaml</c> reads the way it looks.
/// </para>
/// </summary>
public sealed class NameFilter {
    /// <summary>Matches everything. What an empty field means.</summary>
    public static readonly NameFilter Empty = new("", Array.Empty<Pattern>());

    private readonly Pattern[] _patterns;


    private NameFilter(string text, Pattern[] patterns) {
        Text = text;
        _patterns = patterns;
    }


    /// <summary>The text this was parsed from, verbatim. Round-trips into the box and into <c>state.json</c>.</summary>
    public string Text { get; }

    /// <summary>True when this filter lets everything through.</summary>
    public bool IsEmpty => _patterns.Length == 0;

    /// <summary>True when at least one part uses <c>*</c> or <c>?</c>. Tests assert on it; the UI may explain it.</summary>
    public bool HasWildcards {
        get {
            foreach (var pattern in _patterns) {
                if (pattern.IsGlob) {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// The parsed parts. Public for one caller only: the Windows Search
    /// index speaks SQL and has to be handed the same criteria in its own
    /// dialect, and <c>LIKE</c> happens to have exactly these two
    /// metacharacters under different names (<c>%</c> and <c>_</c>).
    /// </summary>
    public IReadOnlyList<Pattern> Patterns => _patterns;


    /// <summary>
    /// Reads a filter out of the field. Never fails: every string is a
    /// legal pattern, because the only two metacharacters cannot be
    /// mis-nested. A filter of nothing but separators and spaces is
    /// <see cref="Empty"/> rather than a filter that matches nothing —
    /// a half-typed <c>;</c> should not blank the list.
    /// </summary>
    public static NameFilter Parse(string? text) {
        if (string.IsNullOrWhiteSpace(text)) {
            return Empty;
        }

        var patterns = new List<Pattern>();
        foreach (string part in text.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)) {
            patterns.Add(new Pattern(part, part.Contains('*') || part.Contains('?')));
        }

        return patterns.Count == 0 ? Empty : new NameFilter(text, patterns.ToArray());
    }


    /// <summary>
    /// Whether the name passes. Case-insensitive and ordinal, the same
    /// comparison the rest of search uses — see <see cref="ContentMatcher"/>
    /// for why ordinal.
    /// </summary>
    public bool Matches(string name) {
        if (_patterns.Length == 0) {
            return true;
        }

        foreach (var pattern in _patterns) {
            bool hit = pattern.IsGlob
                ? GlobMatches(pattern.Text, name)
                : name.Contains(pattern.Text, StringComparison.OrdinalIgnoreCase);
            if (hit) {
                return true;
            }
        }

        return false;
    }


    /// <summary>
    /// Wildcard match over the whole name: <c>*</c> is any run of
    /// characters including none, <c>?</c> is exactly one.
    ///
    /// <para>
    /// Written out rather than translated into a regular expression on
    /// purpose. A pattern comes from a text box that is re-read on every
    /// keystroke, and the regex route would mean either compiling one per
    /// keystroke or carrying a cache — plus catastrophic backtracking is a
    /// real shape for translated globs (<c>*a*a*a*a*b</c>). This walks with
    /// one backtrack point, which is linear in practice and cannot blow up.
    /// </para>
    /// </summary>
    private static bool GlobMatches(string pattern, string name) {
        int p = 0;
        int n = 0;
        int starAt = -1;
        int matchAt = 0;

        while (n < name.Length) {
            if (p < pattern.Length && (pattern[p] == '?' || SameChar(pattern[p], name[n]))) {
                p++;
                n++;
            } else if (p < pattern.Length && pattern[p] == '*') {
                // Remember where the star was: if the rest fails further
                // on, we come back and let the star swallow one more.
                starAt = p;
                matchAt = n;
                p++;
            } else if (starAt >= 0) {
                p = starAt + 1;
                matchAt++;
                n = matchAt;
            } else {
                return false;
            }
        }

        while (p < pattern.Length && pattern[p] == '*') {
            p++;
        }

        return p == pattern.Length;
    }


    private static bool SameChar(char a, char b) {
        return a == b || char.ToUpperInvariant(a) == char.ToUpperInvariant(b);
    }


    /// <summary>One part of the filter, and which of the two languages it is in.</summary>
    public readonly record struct Pattern(string Text, bool IsGlob);
}
