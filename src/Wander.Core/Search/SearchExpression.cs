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
/// </summary>
public static class SearchExpression {
    /// <summary>Separates the name mask from the text. Illegal in file names, so unambiguous.</summary>
    public const char Separator = ':';


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
}
