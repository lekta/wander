namespace Wander.Core.Search;

/// <summary>
/// The two text criteria of a search written as one line:
/// <c>маска:текст</c>.
///
/// <para>
/// It exists so the toolbar box can always show what is being filtered.
/// Without it, a search set up in the search window left the box empty
/// with a clear button next to it — the list was narrowed to eleven rows
/// and nothing on screen said by what. A filter you cannot read is a
/// filter you have to remember, and remembering is what the box is there
/// to spare.
/// </para>
///
/// <para>
/// The separator is a colon because Windows forbids it in file names, so
/// it can never be part of a mask and never needs escaping. Everything
/// before the first colon is the name mask, everything after is the text
/// to find inside files; later colons belong to the text, which is what
/// makes <c>:http://example.com</c> mean what it looks like.
/// </para>
///
/// <para>
/// No colon at all means "all of it is a name mask" — that is what the
/// box has always done, and typing a word into it still filters by name.
/// </para>
///
/// <para>
/// The name half has a half of its own: <c>маска /тип</c>, a group of
/// files after a slash - <c>/изображения</c>, <c>отпуск /видео</c>. The
/// slash is forbidden in names too, so it never needs escaping either; it
/// is looked for before the colon only, which keeps <c>:a/b</c> text.
/// </para>
/// </summary>
public static class SearchExpression {
    /// <summary>Separates the name mask from the text. Illegal in file names, so unambiguous.</summary>
    public const char Separator = ':';

    /// <summary>Separates the type from the name mask, in the name half. Illegal in file names as well.</summary>
    public const char TypeSeparator = '/';


    /// <summary>Splits one line into the two criteria.</summary>
    public static (string Name, string Text) Parse(string? expression) {
        if (string.IsNullOrEmpty(expression)) {
            return ("", "");
        }

        int at = expression.IndexOf(Separator);

        return at < 0
            ? (expression, "")
            : (expression[..at], expression[(at + 1)..]);
    }


    /// <summary>
    /// The two criteria as one line, in the shortest faithful form: the
    /// colon appears only when there is text to look for, so an ordinary
    /// name filter still reads as the plain word it is.
    /// </summary>
    public static string Format(string? name, string? text) {
        name ??= "";
        text ??= "";

        return text.Length == 0 ? name : name + Separator + text;
    }


    /// <summary>
    /// Splits the name half into the mask and the type word, both
    /// trimmed: the space a reader puts before the slash is not part of
    /// the mask.
    /// </summary>
    public static (string Name, string Type) SplitType(string? head) {
        if (string.IsNullOrEmpty(head)) {
            return ("", "");
        }

        int at = TypeSlash(head, head.Length);

        return at < 0
            ? (head, "")
            : (head[..at].TrimEnd(), head[(at + 1)..].Trim());
    }


    /// <summary>The name half from the mask and the type word: the slash only when there is a type.</summary>
    public static string JoinType(string? name, string? type) {
        name ??= "";
        if (string.IsNullOrEmpty(type)) {
            return name;
        }

        return name.Length == 0 ? TypeSeparator + type : name + " " + TypeSeparator + type;
    }


    /// <summary>
    /// Where the type word sits in the line: from past the slash to the
    /// colon, or to the end. Null without a slash in the name half. What
    /// the box's list of types is opened for, and what a pick replaces.
    /// </summary>
    public static (int Start, int End)? TypeSpan(string? expression) {
        if (string.IsNullOrEmpty(expression)) {
            return null;
        }

        int colon = expression.IndexOf(Separator);
        int head = colon < 0 ? expression.Length : colon;
        int slash = TypeSlash(expression, head);

        return slash < 0 ? null : (slash + 1, head);
    }


    /// <summary>
    /// The line with its type word replaced by <paramref name="type"/>, and
    /// where the caret goes - right after it. The line as it was without a
    /// type word to replace.
    /// </summary>
    public static (string Text, int Caret) WithType(string expression, string type) {
        if (TypeSpan(expression) is not { } span) {
            return (expression, expression.Length);
        }

        return (expression[..span.Start] + type + expression[span.End..], span.Start + type.Length);
    }


    /// <summary>
    /// The slash that starts the type word, before <paramref name="end"/>;
    /// -1 for none. Not the slash of a shooting parameter (2026-10-15,
    /// <see cref="ShotQuery"/>): one in a word that reads as a parameter,
    /// or right after an <c>f</c> or a number that start their word - a
    /// parameter still being typed, <c>f/</c>, <c>&gt;=1/</c>.
    /// </summary>
    private static int TypeSlash(string text, int end) {
        int at = text.IndexOf(TypeSeparator, 0, end);
        while (at >= 0) {
            int start = at;
            while (start > 0 && !char.IsWhiteSpace(text[start - 1])) {
                start--;
            }
            int stop = at;
            while (stop < end && !char.IsWhiteSpace(text[stop])) {
                stop++;
            }

            string before = text[start..at].TrimStart('<', '>', '=');
            bool parameter = ShotQuery.TryParse(text[start..stop]) is not null
                || before.Equals("f", StringComparison.OrdinalIgnoreCase)
                || (before.Length > 0 && before.All(char.IsAsciiDigit));
            if (!parameter) {
                return at;
            }

            at = at + 1 < end ? text.IndexOf(TypeSeparator, at + 1, end - at - 1) : -1;
        }

        return -1;
    }
}
