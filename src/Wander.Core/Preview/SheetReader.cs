using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;
using Wander.Core.FileSystem;

namespace Wander.Core.Preview;

/// <summary>One sheet of a table, cut to what a preview shows.</summary>
/// <param name="Rows">From the top, in place: an empty row of the sheet is an empty row here.</param>
/// <param name="TotalRows">Rows up to the last filled one; null when only "more than shown" is known.</param>
/// <param name="Clipped">Rows were left out past <see cref="SheetReader.MaxRows"/>.</param>
public sealed record Sheet(string Name, IReadOnlyList<string[]> Rows, int? TotalRows, bool Clipped) {
    public int Columns => Rows.Count == 0 ? 0 : Rows.Max(r => r.Length);
}


/// <summary>
/// Tables for the preview pane's grid: CSV and TSV, Excel's XLSX and
/// OpenDocument's ODS. Each comes back as sheets of display strings - the
/// cells as a spreadsheet would show them, near enough: a date as a date,
/// a share as a percentage, a number as itself without the format's
/// padding, a formula as its cached result.
///
/// <para>
/// A preview, not an import: the first <see cref="MaxRows"/> rows of
/// <see cref="MaxColumns"/> columns, a long cell cut, the rest of a sheet
/// skipped. The legacy binary <c>.xls</c> is not here - another container
/// and record format altogether (PLAN.md, the deferred types of 0.6).
/// </para>
/// </summary>
public static class SheetReader {
    public const int MaxRows = 1000;
    public const int MaxColumns = 100;

    /// <summary>A cell past this is cut: the grid shows a line of it, all of it once clicked.</summary>
    public const int MaxCellChars = 2000;

    private const int MaxSheets = 32;

    /// <summary>A text table is read to here: a thousand rows of anything are in it.</summary>
    private const int MaxTextBytes = 4 * 1024 * 1024;

    /// <summary>A packed workbook past this is left to the text preview.</summary>
    private const long MaxFileSize = 64L * 1024 * 1024;

    /// <summary>The shared string table is read whole; past this it is not a preview.</summary>
    private const long MaxSharedStrings = 128L * 1024 * 1024;

    /// <summary>How many lines of a text table the delimiter is guessed from.</summary>
    private const int SniffLines = 20;

    /// <summary>Rows of a workbook's sheet read past the shown ones to find its last filled row.</summary>
    private const int MaxScanRows = 200_000;

    private static readonly char[] _delimiters = { ',', ';', '\t', '|' };

    private static readonly XmlReaderSettings _xml = new() {
        DtdProcessing = DtdProcessing.Ignore,
        XmlResolver = null,
        IgnoreComments = true,
        IgnoreProcessingInstructions = true,
        IgnoreWhitespace = false,
        CheckCharacters = false,
        CloseInput = true,
    };


    /// <summary>Extensions read as tables.</summary>
    public static readonly IReadOnlySet<string> Extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
        ".csv", ".tsv", ".xlsx", ".xlsm", ".ods",
    };


    /// <summary>A table that is text underneath - the pane offers its plain text too.</summary>
    public static bool IsText(string path) {
        string ext = Path.GetExtension(path);

        return ext.Equals(".csv", StringComparison.OrdinalIgnoreCase) || ext.Equals(".tsv", StringComparison.OrdinalIgnoreCase);
    }


    /// <summary>
    /// The sheets of the file at <paramref name="path"/>, or null when it
    /// is not one of these formats, is damaged or is too large. Never
    /// throws for a malformed file.
    /// </summary>
    public static IReadOnlyList<Sheet>? Read(string path) {
        try {
            using var stream = SharedRead.Open(path);
            if (IsText(path)) {
                var bytes = new byte[(int)Math.Min(stream.Length, MaxTextBytes)];
                stream.ReadExactly(bytes);
                string text = EncodingProbe.Decode(bytes);
                bool cut = stream.Length > bytes.Length;
                if (cut) {
                    // The read stopped mid-line, likely mid-character: the
                    // last line is not the file's.
                    int lastBreak = text.LastIndexOf('\n');
                    text = lastBreak > 0 ? text[..lastBreak] : text;
                }
                char? delimiter = Path.GetExtension(path).Equals(".tsv", StringComparison.OrdinalIgnoreCase) ? '\t' : null;

                return new[] { Csv(text, Path.GetFileNameWithoutExtension(path), delimiter, cut) };
            }
            if (stream.Length > MaxFileSize) {
                return null;
            }

            return Path.GetExtension(path).Equals(".ods", StringComparison.OrdinalIgnoreCase) ? Ods(stream) : Xlsx(stream);
        } catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException or InvalidDataException or XmlException
                or NotSupportedException or FormatException or OverflowException or ArgumentException) {
            return null;
        }
    }


    // --- CSV ----------------------------------------------------------------


    /// <summary>
    /// A delimited text as one sheet: RFC 4180 quoting (a quoted cell holds
    /// delimiters, line breaks and doubled quotes), an Excel <c>sep=</c>
    /// first line obeyed, otherwise the delimiter guessed - comma,
    /// semicolon (Excel in most of Europe), tab or bar, whichever splits
    /// the first lines most evenly. Rows past the last filled one - an
    /// export padded with lines of bare delimiters - are left out.
    /// </summary>
    /// <param name="cut">The text is the start of a longer file: the total is not known.</param>
    public static Sheet Csv(string text, string name, char? delimiter = null, bool cut = false) {
        int start = 0;
        if (text.StartsWith("sep=", StringComparison.OrdinalIgnoreCase) && text.Length > 4 && text[4] is not ('\r' or '\n')) {
            delimiter = text[4];
            int lineEnd = text.IndexOf('\n');
            start = lineEnd < 0 ? text.Length : lineEnd + 1;
        }
        char sep = delimiter ?? Sniff(text, start);

        var rows = new List<string[]>();
        var row = new List<string>();
        var cell = new StringBuilder();
        int count = 0;
        int filled = 0;
        bool quoted = false;
        bool rowOpen = false;
        for (int i = start; i < text.Length; i++) {
            char c = text[i];
            if (quoted) {
                if (c != '"') {
                    cell.Append(c);
                } else if (i + 1 < text.Length && text[i + 1] == '"') {
                    cell.Append('"');
                    i++;
                } else {
                    quoted = false;
                }

                continue;
            }

            if (c == '"' && cell.Length == 0) {
                quoted = true;
                rowOpen = true;
            } else if (c == sep) {
                row.Add(CellText(cell));
                cell.Clear();
                rowOpen = true;
            } else if (c is '\r' or '\n') {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') {
                    i++;
                }
                row.Add(CellText(cell));
                cell.Clear();
                AddRow(rows, Trimmed(row), ref count, ref filled);
                row.Clear();
                rowOpen = false;
            } else {
                cell.Append(c);
                rowOpen = true;
            }
        }
        if (rowOpen || cell.Length > 0) {
            row.Add(CellText(cell));
            AddRow(rows, Trimmed(row), ref count, ref filled);
        }
        DropEmptyTail(rows);

        return new Sheet(name, rows, cut ? null : filled, cut || filled > MaxRows);
    }


    /// <summary>A row in its place: kept while under the limit, counted past it; <paramref name="filled"/> - rows up to the last with a value.</summary>
    private static void AddRow(List<string[]> rows, string[] row, ref int count, ref int filled) {
        if (count < MaxRows) {
            rows.Add(row);
        }
        count++;
        if (row.Length > 0) {
            filled = count;
        }
    }


    private static void DropEmptyTail(List<string[]> rows) {
        int last = rows.Count;
        while (last > 0 && rows[last - 1].Length == 0) {
            last--;
        }
        rows.RemoveRange(last, rows.Count - last);
    }


    /// <summary>The delimiter that splits the first lines into the most equal, most numerous cells.</summary>
    private static char Sniff(string text, int start) {
        var counts = new int[_delimiters.Length][];
        for (int d = 0; d < _delimiters.Length; d++) {
            counts[d] = new int[SniffLines];
        }

        int line = 0;
        bool quoted = false;
        for (int i = start; i < text.Length && line < SniffLines; i++) {
            char c = text[i];
            if (c == '"') {
                quoted = !quoted;
            } else if (!quoted && c == '\n') {
                line++;
            } else if (!quoted) {
                int d = Array.IndexOf(_delimiters, c);
                if (d >= 0) {
                    counts[d][line]++;
                }
            }
        }
        // The last line may be cut short by the sample, not by the file.
        int lines = Math.Max(1, Math.Min(line, SniffLines));

        char best = ',';
        int bestScore = 0;
        for (int d = 0; d < _delimiters.Length; d++) {
            var sample = counts[d].Take(lines).ToArray();
            int min = sample.Min();
            int score = min > 0 && sample.All(n => n == min) ? 1000 + min : min;
            if (score > bestScore) {
                best = _delimiters[d];
                bestScore = score;
            }
        }

        return best;
    }


    // --- XLSX ---------------------------------------------------------------


    /// <summary>An Office Open XML workbook: its visible sheets, in order.</summary>
    public static IReadOnlyList<Sheet>? Xlsx(Stream stream) {
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        if (zip.GetEntry("xl/workbook.xml") is not { } workbook) {
            return null;
        }

        var listed = new List<(string Name, string RelId)>();
        bool date1904 = false;
        using (var reader = XmlReader.Create(workbook.Open(), _xml)) {
            while (reader.Read()) {
                if (reader.NodeType != XmlNodeType.Element) {
                    continue;
                }
                if (reader.LocalName == "workbookPr") {
                    date1904 = reader.GetAttribute("date1904") is "1" or "true";
                } else if (reader.LocalName == "sheet" && reader.GetAttribute("state") is null or "visible") {
                    string? id = Attribute(reader, "id");
                    if (id is not null) {
                        listed.Add((reader.GetAttribute("name") ?? "", id));
                    }
                }
            }
        }

        var targets = Relationships(zip, "xl/_rels/workbook.xml.rels");
        var shared = SharedStrings(zip);
        var styles = CellStyles(zip);
        var sheets = new List<Sheet>();
        foreach (var (name, relId) in listed.Take(MaxSheets)) {
            if (!targets.TryGetValue(relId, out string? target)) {
                continue;
            }

            string entryName = target.StartsWith('/') ? target[1..] : "xl/" + target;
            if (zip.GetEntry(entryName) is { } entry) {
                sheets.Add(XlsxSheet(entry, name, shared, styles, date1904));
            }
        }

        return sheets.Count > 0 ? sheets : null;
    }


    /// <summary>
    /// One sheet. Excel writes a row for every formatted one, values or
    /// not, and a sheet formatted to the end has thousands of them: the
    /// total is the last row with a value, found by reading on past the
    /// shown ones - up to <see cref="MaxScanRows"/>, then it is not known.
    /// </summary>
    private static Sheet XlsxSheet(ZipArchiveEntry entry, string name, List<string> shared, List<CellStyle> styles, bool date1904) {
        var rows = new List<string[]>();
        int filled = 0;
        int scanned = 0;
        bool known = true;
        using var reader = XmlReader.Create(entry.Open(), _xml);
        while (reader.Read()) {
            if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "row") {
                continue;
            }

            int number = int.TryParse(reader.GetAttribute("r"), out int r) ? r : Math.Max(rows.Count, filled) + 1;
            string[] cells = reader.IsEmptyElement ? Array.Empty<string>() : XlsxRow(reader, shared, styles, date1904);
            if (cells.Length > 0) {
                filled = Math.Max(filled, number);
            }
            if (number > MaxRows) {
                if (++scanned > MaxScanRows) {
                    known = false;

                    break;
                }

                continue;
            }

            while (rows.Count < number - 1) {
                rows.Add(Array.Empty<string>());
            }
            rows.Add(cells);
        }
        DropEmptyTail(rows);
        bool clipped = !known || filled > MaxRows;

        return new Sheet(name, rows, known ? filled : null, clipped);
    }


    /// <summary>
    /// One <c>row</c>, the reader on its start tag; it is left on the end
    /// tag. Node by node rather than by <c>ReadElementContentAsString</c>,
    /// which leaves the reader past the end tag it consumed - the next
    /// <c>Read</c> would step over the cell's own end.
    /// </summary>
    private static string[] XlsxRow(XmlReader reader, List<string> shared, List<CellStyle> styles, bool date1904) {
        var cells = new List<string>();
        int depth = reader.Depth;
        int column = 0;
        string type = "n";
        int style = 0;
        var value = new StringBuilder();
        var inline = new StringBuilder();
        StringBuilder? into = null;
        while (reader.Read() && !(reader.NodeType == XmlNodeType.EndElement && reader.Depth == depth)) {
            switch (reader.NodeType) {
                case XmlNodeType.Element when reader.LocalName == "c":
                    column = reader.GetAttribute("r") is { } at ? ColumnOf(at) : cells.Count;
                    type = reader.GetAttribute("t") ?? "n";
                    style = int.TryParse(reader.GetAttribute("s"), out int s) ? s : 0;
                    value.Clear();
                    inline.Clear();
                    if (reader.IsEmptyElement) {
                        AddCell(cells, column, "");
                    }
                    break;

                case XmlNodeType.Element when !reader.IsEmptyElement:
                    into = reader.LocalName switch {
                        "v" => value,
                        "t" => inline,
                        _ => into,
                    };
                    break;

                case XmlNodeType.Text or XmlNodeType.CDATA or XmlNodeType.Whitespace or XmlNodeType.SignificantWhitespace:
                    into?.Append(reader.Value);
                    break;

                case XmlNodeType.EndElement when reader.LocalName is "v" or "t":
                    into = null;
                    break;

                case XmlNodeType.EndElement when reader.LocalName == "c": {
                        string raw = value.ToString();
                        string text = type switch {
                            "s" => int.TryParse(raw, out int index) && index >= 0 && index < shared.Count ? shared[index] : "",
                            "inlineStr" => inline.ToString(),
                            "b" => raw == "1" ? "TRUE" : "FALSE",
                            "d" => DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? DateText(date) : raw,
                            "n" => NumberText(raw, style < styles.Count ? styles[style] : CellStyle.Plain, date1904),
                            _ => raw,
                        };
                        AddCell(cells, column, Cut(text));
                        break;
                    }
            }
        }

        return Trimmed(cells);
    }


    /// <summary>A cell at its column, the gap before it filled with empty ones; past the last column, nothing.</summary>
    private static void AddCell(List<string> cells, int column, string text) {
        if (column >= MaxColumns || column < cells.Count) {
            return;
        }

        while (cells.Count < column) {
            cells.Add("");
        }
        cells.Add(text);
    }


    /// <summary>A number in its cell's style: a date, a time, a percentage, or as written.</summary>
    private static string NumberText(string? value, CellStyle style, bool date1904) {
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number)) {
            return value ?? "";
        }

        try {
            return style switch {
                CellStyle.Date => DateTime.FromOADate(number + (date1904 ? 1462 : 0)).ToString("d", CultureInfo.CurrentCulture),
                CellStyle.Time => DateTime.FromOADate(number).ToString("T", CultureInfo.CurrentCulture),
                CellStyle.DateTime => DateText(DateTime.FromOADate(number + (date1904 ? 1462 : 0))),
                CellStyle.Percent => (number * 100).ToString("G15", CultureInfo.CurrentCulture) + "%",
                _ => number.ToString("G15", CultureInfo.CurrentCulture),
            };
        } catch (ArgumentException) {
            // Out of the range of a date: shown as the number it is.
            return number.ToString("G15", CultureInfo.CurrentCulture);
        }
    }


    private static string DateText(DateTime date) {
        return date.TimeOfDay == TimeSpan.Zero
            ? date.ToString("d", CultureInfo.CurrentCulture)
            : date.ToString("g", CultureInfo.CurrentCulture);
    }


    /// <summary>The shared string table: rich strings flattened to their text.</summary>
    private static List<string> SharedStrings(ZipArchive zip) {
        var strings = new List<string>();
        if (zip.GetEntry("xl/sharedStrings.xml") is not { } entry || entry.Length > MaxSharedStrings) {
            return strings;
        }

        using var reader = XmlReader.Create(entry.Open(), _xml);
        var text = new StringBuilder();
        bool inText = false;
        bool inPhonetic = false;
        while (reader.Read()) {
            switch (reader.NodeType) {
                case XmlNodeType.Element:
                    if (reader.LocalName == "si") {
                        text.Clear();
                        if (reader.IsEmptyElement) {
                            strings.Add("");
                        }
                    } else if (reader.LocalName == "rPh") {
                        // A reading aid for Japanese, not part of the string.
                        inPhonetic = !reader.IsEmptyElement;
                    } else if (reader.LocalName == "t") {
                        inText = !reader.IsEmptyElement && !inPhonetic;
                    }
                    break;

                case XmlNodeType.Text or XmlNodeType.CDATA or XmlNodeType.Whitespace or XmlNodeType.SignificantWhitespace:
                    if (inText) {
                        text.Append(reader.Value);
                    }
                    break;

                case XmlNodeType.EndElement:
                    if (reader.LocalName == "si") {
                        strings.Add(Cut(text.ToString()));
                    } else if (reader.LocalName == "rPh") {
                        inPhonetic = false;
                    } else if (reader.LocalName == "t") {
                        inText = false;
                    }
                    break;
            }
        }

        return strings;
    }


    /// <summary>What each cell format (<c>cellXfs</c>) makes of a number.</summary>
    private static List<CellStyle> CellStyles(ZipArchive zip) {
        var styles = new List<CellStyle>();
        if (zip.GetEntry("xl/styles.xml") is not { } entry) {
            return styles;
        }

        var custom = new Dictionary<int, CellStyle>();
        bool inCellFormats = false;
        using var reader = XmlReader.Create(entry.Open(), _xml);
        while (reader.Read()) {
            if (reader.NodeType == XmlNodeType.Element) {
                if (reader.LocalName == "numFmt" && int.TryParse(reader.GetAttribute("numFmtId"), out int id)) {
                    custom[id] = StyleOfCode(reader.GetAttribute("formatCode") ?? "");
                } else if (reader.LocalName == "cellXfs") {
                    inCellFormats = !reader.IsEmptyElement;
                } else if (reader.LocalName == "xf" && inCellFormats) {
                    int format = int.TryParse(reader.GetAttribute("numFmtId"), out int f) ? f : 0;
                    styles.Add(custom.TryGetValue(format, out var style) ? style : BuiltInStyle(format));
                }
            } else if (reader.NodeType == XmlNodeType.EndElement && reader.LocalName == "cellXfs") {
                inCellFormats = false;
            }
        }

        return styles;
    }


    private static CellStyle BuiltInStyle(int id) {
        return id switch {
            9 or 10 => CellStyle.Percent,
            14 or 15 or 16 or 17 or (>= 27 and <= 36) or (>= 50 and <= 58) => CellStyle.Date,
            18 or 19 or 20 or 21 or 45 or 46 or 47 => CellStyle.Time,
            22 => CellStyle.DateTime,
            _ => CellStyle.Plain,
        };
    }


    /// <summary>
    /// A custom number format read for what it shows: its first section,
    /// quoted text and bracketed colours or locales left out. Days or years
    /// make a date, hours or seconds a time - both, a date with its time;
    /// a percent sign a percentage. A lone <c>m</c> is a month.
    /// </summary>
    internal static CellStyle StyleOfCode(string code) {
        var plain = new StringBuilder();
        bool quoted = false;
        for (int i = 0; i < code.Length; i++) {
            char c = code[i];
            if (c == '"') {
                quoted = !quoted;
            } else if (quoted) {
                continue;
            } else if (c == '\\' || c == '_' || c == '*') {
                i++;
            } else if (c == '[') {
                int close = code.IndexOf(']', i);
                string inside = close > i ? code[(i + 1)..close].ToLowerInvariant() : "";
                // [h] and [mm] are elapsed time; [Red], [$-419] are not shown.
                if (inside is "h" or "hh" or "m" or "mm" or "s" or "ss") {
                    plain.Append('h');
                }
                i = close > i ? close : code.Length;
            } else if (c == ';') {
                break;
            } else {
                plain.Append(char.ToLowerInvariant(c));
            }
        }

        string shown = plain.ToString();
        bool date = shown.Contains('d') || shown.Contains('y') || (shown.Contains('m') && !shown.Contains('h') && !shown.Contains('s'));
        bool time = shown.Contains('h') || shown.Contains('s');
        if (date && time) {
            return CellStyle.DateTime;
        }
        if (date) {
            return CellStyle.Date;
        }
        if (time) {
            return CellStyle.Time;
        }

        return shown.Contains('%') ? CellStyle.Percent : CellStyle.Plain;
    }


    private static Dictionary<string, string> Relationships(ZipArchive zip, string name) {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        if (zip.GetEntry(name) is not { } entry) {
            return map;
        }

        using var reader = XmlReader.Create(entry.Open(), _xml);
        while (reader.Read()) {
            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "Relationship"
                && reader.GetAttribute("Id") is { } id && reader.GetAttribute("Target") is { } target) {
                map[id] = target;
            }
        }

        return map;
    }


    // --- ODS ----------------------------------------------------------------


    /// <summary>
    /// An OpenDocument spreadsheet. Its cells carry the text as displayed
    /// (<c>text:p</c>), formatting already applied, so nothing is
    /// converted; repeats - <c>number-rows-repeated</c>,
    /// <c>number-columns-repeated</c>, which pad a sheet to a million
    /// empty rows - are expanded only up to the last filled cell.
    /// </summary>
    public static IReadOnlyList<Sheet>? Ods(Stream stream) {
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        if (zip.GetEntry("content.xml") is not { } content) {
            return null;
        }

        var sheets = new List<Sheet>();
        using var reader = XmlReader.Create(content.Open(), _xml);
        while (sheets.Count < MaxSheets && reader.Read()) {
            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "table") {
                string name = Attribute(reader, "name") ?? "";
                sheets.Add(reader.IsEmptyElement ? new Sheet(name, Array.Empty<string[]>(), 0, false) : OdsSheet(reader, name));
            }
        }

        return sheets.Count > 0 ? sheets : null;
    }


    private static Sheet OdsSheet(XmlReader reader, string name) {
        var rows = new List<string[]>();
        int pendingEmpty = 0;
        bool clipped = false;
        int depth = reader.Depth;
        while (reader.Read() && !(reader.NodeType == XmlNodeType.EndElement && reader.Depth == depth)) {
            if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "table-row") {
                continue;
            }

            int repeat = Repeat(reader, "number-rows-repeated");
            string[] row = reader.IsEmptyElement ? Array.Empty<string>() : OdsRow(reader);
            if (clipped) {
                continue;
            }
            if (row.Length == 0) {
                pendingEmpty += repeat;

                continue;
            }

            for (int i = 0; i < pendingEmpty + repeat; i++) {
                if (rows.Count >= MaxRows) {
                    clipped = true;

                    break;
                }
                rows.Add(i < pendingEmpty ? Array.Empty<string>() : row);
            }
            pendingEmpty = 0;
        }

        return new Sheet(name, rows, clipped ? null : rows.Count, clipped);
    }


    private static string[] OdsRow(XmlReader reader) {
        var cells = new List<string>();
        int pendingEmpty = 0;
        int depth = reader.Depth;
        while (reader.Read() && !(reader.NodeType == XmlNodeType.EndElement && reader.Depth == depth)) {
            if (reader.NodeType != XmlNodeType.Element || reader.LocalName is not ("table-cell" or "covered-table-cell")) {
                continue;
            }

            int repeat = Repeat(reader, "number-columns-repeated");
            string text = reader.IsEmptyElement ? "" : Cut(OdsCellText(reader));
            if (text.Length == 0) {
                pendingEmpty += repeat;

                continue;
            }
            for (int i = 0; i < pendingEmpty + repeat && cells.Count < MaxColumns; i++) {
                cells.Add(i < pendingEmpty ? "" : text);
            }
            pendingEmpty = 0;
        }

        return cells.ToArray();
    }


    /// <summary>
    /// A cell's paragraphs, a line each, with the spaces and tabs the
    /// format writes as elements. The reader is left on the cell's end tag.
    /// </summary>
    private static string OdsCellText(XmlReader reader) {
        var text = new StringBuilder();
        int depth = reader.Depth;
        int paragraphs = 0;
        // Depth of a comment on the cell - not its text - while inside one.
        int annotation = -1;
        while (reader.Read() && !(reader.NodeType == XmlNodeType.EndElement && reader.Depth == depth)) {
            if (annotation >= 0) {
                if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == annotation) {
                    annotation = -1;
                }

                continue;
            }

            switch (reader.NodeType) {
                case XmlNodeType.Element:
                    switch (reader.LocalName) {
                        case "p":
                            if (paragraphs++ > 0) {
                                text.Append('\n');
                            }
                            break;

                        case "s":
                            text.Append(' ', Math.Min(Repeat(reader, "c"), MaxCellChars));
                            break;

                        case "tab":
                            text.Append('\t');
                            break;

                        case "line-break":
                            text.Append('\n');
                            break;

                        case "annotation" when !reader.IsEmptyElement:
                            annotation = reader.Depth;
                            break;
                    }
                    break;

                case XmlNodeType.Text or XmlNodeType.CDATA or XmlNodeType.SignificantWhitespace:
                    text.Append(reader.Value);
                    break;
            }
        }

        return text.ToString();
    }


    // --- Shared -------------------------------------------------------------


    private static int Repeat(XmlReader reader, string attribute) {
        return int.TryParse(Attribute(reader, attribute), out int n) && n > 0 ? n : 1;
    }


    /// <summary>An attribute by its local name, whatever namespace prefix the writer used.</summary>
    private static string? Attribute(XmlReader reader, string localName) {
        if (!reader.HasAttributes) {
            return null;
        }

        for (int i = 0; i < reader.AttributeCount; i++) {
            reader.MoveToAttribute(i);
            if (reader.LocalName == localName) {
                string value = reader.Value;
                reader.MoveToElement();

                return value;
            }
        }
        reader.MoveToElement();

        return null;
    }



    /// <summary>The zero-based column of a cell reference: "A1" is 0, "AB7" is 27.</summary>
    private static int ColumnOf(string reference) {
        int column = 0;
        foreach (char c in reference) {
            if (c is >= 'A' and <= 'Z') {
                column = column * 26 + (c - 'A' + 1);
            } else if (c is >= 'a' and <= 'z') {
                column = column * 26 + (c - 'a' + 1);
            } else {
                break;
            }
            if (column > MaxColumns + 1) {
                return MaxColumns;
            }
        }

        return column - 1;
    }


    private static string CellText(StringBuilder cell) {
        return Cut(cell.ToString());
    }


    private static string Cut(string text) {
        return text.Length > MaxCellChars ? text[..MaxCellChars] + "…" : text;
    }


    /// <summary>The row up to its last filled cell, at most <see cref="MaxColumns"/> wide.</summary>
    private static string[] Trimmed(List<string> row) {
        int length = Math.Min(row.Count, MaxColumns);
        while (length > 0 && row[length - 1].Length == 0) {
            length--;
        }

        return row.Take(length).ToArray();
    }


    internal enum CellStyle {
        Plain,
        Date,
        Time,
        DateTime,
        Percent,
    }
}
