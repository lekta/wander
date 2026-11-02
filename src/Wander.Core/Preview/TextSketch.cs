using System.Text;

namespace Wander.Core.Preview;

/// <summary>
/// One mark on a text file's tile (<see cref="TextSketch"/>): a word as a
/// bar, in the units of the page's text area - body columns across, body
/// rows down.
/// </summary>
/// <param name="Left">Columns from the text area's left edge.</param>
/// <param name="Top">Rows from the text area's top.</param>
/// <param name="Width">Columns the word takes.</param>
/// <param name="Height">Rows the bar is thick.</param>
/// <param name="Rank">0 for body text; 1, 2, 3 for a heading, biggest first.</param>
public readonly record struct SketchBar(double Left, double Top, double Width, double Height, int Rank);


/// <summary>
/// A text file's tile as a page of greeked text, the way an online office
/// shows a document in its file list: every word a bar, no letter to read,
/// the shape of the text kept - where lines break, paragraphs and the blank
/// lines between them, indents, list markers, a heading set larger and
/// darker. Enough to tell notes from a log from a readme at a glance.
///
/// <para>
/// A heading is a markdown <c>#</c> line, a line underlined with
/// <c>===</c> or <c>---</c>, or a short first line with a blank line after
/// it - the way a plain-text note is titled. A line wider than the page
/// wraps at a word, as a document would; a list item's wrapped lines hang
/// under its text, not under its marker. Blank lines past two in a row add
/// no more space.
/// </para>
/// </summary>
public static class TextSketch {
    /// <summary>Body characters across the text area.</summary>
    public const int Columns = 44;

    /// <summary>Body rows down the text area: a row is about two and a half characters tall, as in print.</summary>
    public const int Rows = 28;

    /// <summary>The longest line still taken for a title or an underlined heading.</summary>
    public const int TitleLimit = 60;

    /// <summary>A tab moves to the next multiple of this many columns.</summary>
    private const int TabStop = 4;

    /// <summary>A body bar's thickness, of its row: the x-height of print.</summary>
    private const double BodyInk = 0.5;

    /// <summary>A heading bar's thickness, of its own row: bold.</summary>
    private const double HeadingInk = 0.62;

    /// <summary>Blank lines in a row that still add space.</summary>
    private const int MaxBlankRun = 2;

    /// <summary>The share of the page an indent may push a line to.</summary>
    private const double MaxIndent = 0.5;

    /// <summary>A heading's type against the body's, by rank.</summary>
    private static readonly double[] _scales = { 1.0, 1.6, 1.3, 1.1 };

    // Bullets a list item may start with besides - * +: bullet, en dash,
    // em dash, middle dot, triangular bullet, small black square.
    private static readonly char[] _bullets = { '-', '*', '+', '•', '–', '—', '·', '‣', '▪' };


    /// <summary>The page's bars, top to bottom; what does not fit on the page is left out.</summary>
    public static IReadOnlyList<SketchBar> Lay(string text) {
        var bars = new List<SketchBar>();
        double y = 0;
        int blanks = 0;
        foreach (var line in Classify(text)) {
            if (line.Text is null) {
                if (++blanks <= MaxBlankRun) {
                    y += 1;
                }
                continue;
            }

            blanks = 0;
            if (!LayLine(line, ref y, bars)) {
                break;
            }
        }

        return bars;
    }


    /// <summary>
    /// The lines with their ranks: blank ones as null text, an underline
    /// folded into the heading above it, a markdown heading without its
    /// hashes. Tabs are spaces by now.
    /// </summary>
    private static List<Line> Classify(string text) {
        var lines = new List<Line>();
        foreach (string raw in text.Split('\n')) {
            string line = ExpandTabs(raw.TrimEnd());
            string trimmed = line.Trim();
            if (trimmed.Length == 0) {
                lines.Add(new Line(null, 0));
                continue;
            }
            if (Underline(trimmed) is > 0 and var rank && lines.Count > 0
                && lines[^1] is { Text: { } above, Rank: 0 } && CanTitle(above)) {
                lines[^1] = new Line(above.Trim(), rank);
                continue;
            }

            lines.Add(Hashed(trimmed) ?? new Line(line, 0));
        }

        // A short first line with a blank line after it is the note's title.
        int first = lines.FindIndex(line => line.Text is not null);
        if (first >= 0 && first + 1 < lines.Count && lines[first + 1].Text is null
            && lines[first] is { Text: { } title, Rank: 0 } && CanTitle(title)) {
            lines[first] = new Line(title.Trim(), 1);
        }

        return lines;
    }

    /// <summary>
    /// Lays one line from row <paramref name="y"/>, wrapping at words, and
    /// moves <paramref name="y"/> past it. False when the page ran out.
    /// </summary>
    private static bool LayLine(Line line, ref double y, List<SketchBar> bars) {
        double scale = _scales[line.Rank];
        if (y + scale > Rows) {
            return false;
        }

        string text = line.Text!;
        int indent = text.Length - text.TrimStart().Length;
        double left = Math.Min(indent, Columns * MaxIndent);
        double hang = Math.Min(left + MarkerWidth(text, indent) * scale, Columns * MaxIndent);
        double x = left;
        bool rowStart = true;
        int i = indent;
        while (i < text.Length) {
            int gapStart = i;
            while (i < text.Length && char.IsWhiteSpace(text[i])) {
                i++;
            }
            if (i == text.Length) {
                break;
            }

            int start = i;
            while (i < text.Length && !char.IsWhiteSpace(text[i])) {
                i++;
            }

            double gap = rowStart ? 0 : (start - gapStart) * scale;
            double word = (i - start) * scale;
            if (!rowStart && x + gap + word > Columns) {
                if (!NextRow(ref y, scale)) {
                    return false;
                }
                x = hang;
                gap = 0;
            }

            x += gap;
            // A word wider than the row - a link, a rule of dashes - goes on in pieces.
            while (x + word > Columns) {
                double piece = Columns - x;
                if (piece > 0) {
                    bars.Add(Bar(x, y, piece, scale, line.Rank));
                    word -= piece;
                }
                if (!NextRow(ref y, scale)) {
                    return false;
                }
                x = hang;
            }

            bars.Add(Bar(x, y, word, scale, line.Rank));
            x += word;
            rowStart = false;
        }

        y += scale;

        return true;
    }

    /// <summary>Moves to the next row of a wrapped line; false when it would not fit on the page.</summary>
    private static bool NextRow(ref double y, double scale) {
        y += scale;

        return y + scale <= Rows;
    }

    private static SketchBar Bar(double x, double y, double width, double scale, int rank) {
        double thickness = (rank == 0 ? BodyInk : HeadingInk) * scale;

        return new SketchBar(x, y + (scale - thickness) / 2, width, thickness, rank);
    }

    /// <summary>
    /// Columns from a list item's marker to its text - where its wrapped
    /// lines hang - or 0 when the line is not a list item.
    /// </summary>
    private static int MarkerWidth(string text, int at) {
        int i = at;
        if (i < text.Length && Array.IndexOf(_bullets, text[i]) >= 0) {
            i++;
        } else {
            while (i < text.Length && i - at < 3 && char.IsAsciiDigit(text[i])) {
                i++;
            }
            if (i == at || i == text.Length || text[i] is not ('.' or ')')) {
                return 0;
            }
            i++;
        }

        int marker = i;
        while (i < text.Length && text[i] == ' ') {
            i++;
        }

        return i > marker && i < text.Length ? i - at : 0;
    }

    /// <summary>Whether a line reads as a heading's text: short, and not a list item.</summary>
    private static bool CanTitle(string line) {
        string trimmed = line.Trim();

        return trimmed.Length <= TitleLimit && MarkerWidth(trimmed, 0) == 0;
    }

    /// <summary>1 for a line of <c>=</c>, 2 for a line of <c>-</c>, three or more; 0 for anything else.</summary>
    private static int Underline(string trimmed) {
        if (trimmed.Length < 3) {
            return 0;
        }
        if (trimmed.All(c => c == '=')) {
            return 1;
        }

        return trimmed.All(c => c == '-') ? 2 : 0;
    }

    /// <summary>A markdown heading - one to six hashes, a space, the text - without its hashes; null for any other line.</summary>
    private static Line? Hashed(string trimmed) {
        int hashes = 0;
        while (hashes < trimmed.Length && trimmed[hashes] == '#') {
            hashes++;
        }
        if (hashes is 0 or > 6 || hashes == trimmed.Length || trimmed[hashes] != ' ') {
            return null;
        }

        string rest = trimmed[hashes..].TrimEnd('#').Trim();

        return rest.Length == 0 ? null : new Line(rest, Math.Min(hashes, 3));
    }

    private static string ExpandTabs(string line) {
        if (!line.Contains('\t')) {
            return line;
        }

        var expanded = new StringBuilder(line.Length + TabStop * 2);
        foreach (char c in line) {
            if (c == '\t') {
                expanded.Append(' ', TabStop - expanded.Length % TabStop);
            } else {
                expanded.Append(c);
            }
        }

        return expanded.ToString();
    }


    /// <summary>A line of the file: its text (null when blank) and its rank (<see cref="SketchBar.Rank"/>).</summary>
    private readonly record struct Line(string? Text, int Rank);
}
