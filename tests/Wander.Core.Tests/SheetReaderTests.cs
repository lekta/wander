using System.Globalization;
using System.IO.Compression;
using System.Text;
using Wander.Core.Preview;

namespace Wander.Core.Tests;

/// <summary>
/// Workbooks are zipped here from a few lines of XML each, as a spreadsheet
/// program writes them in miniature: what is tested is where a cell lands,
/// how a number is shown in its style, and how repeats are expanded.
/// </summary>
public class SheetReaderTests {
    // --- CSV ----------------------------------------------------------------

    [Fact]
    public void Csv_QuotedCellsHoldDelimitersBreaksAndQuotes() {
        var sheet = SheetReader.Csv("a,\"b,c\",\"say \"\"hi\"\"\"\r\n\"two\nlines\",x\r\n", "t");

        Assert.Equal(2, sheet.Rows.Count);
        Assert.Equal(new[] { "a", "b,c", "say \"hi\"" }, sheet.Rows[0]);
        Assert.Equal(new[] { "two\nlines", "x" }, sheet.Rows[1]);
        Assert.Equal(2, sheet.TotalRows);
        Assert.False(sheet.Clipped);
    }

    [Theory]
    [InlineData("name;price;qty\nчай;1,5;3\nкофе;2,75;1\n", ';')]
    [InlineData("name\tprice\nчай\t1.5\n", '\t')]
    [InlineData("name,price\n\"a;b\",1\n", ',')]
    [InlineData("a|b|c\n1|2|3\n", '|')]
    public void Csv_TheDelimiterIsGuessed(string text, char expected) {
        var sheet = SheetReader.Csv(text, "t");

        Assert.Equal(text.Split('\n')[0].Split(expected), sheet.Rows[0]);
    }

    [Fact]
    public void Csv_ExcelSepLineIsObeyedAndNotShown() {
        var sheet = SheetReader.Csv("sep=,\na;b,c\n", "t");

        Assert.Equal(new[] { "a;b", "c" }, Assert.Single(sheet.Rows));
    }

    [Fact]
    public void Csv_BlankLinesStayAsEmptyRows_TrailingEmptyCellsDrop() {
        var sheet = SheetReader.Csv("a,b,,\n\nc\n", "t");

        Assert.Equal(new[] { "a", "b" }, sheet.Rows[0]);
        Assert.Empty(sheet.Rows[1]);
        Assert.Equal(new[] { "c" }, sheet.Rows[2]);
    }

    [Fact]
    public void Csv_LinesOfBareDelimitersAtTheEnd_AreLeftOut() {
        var text = "a;b\n1;2\n" + string.Concat(Enumerable.Range(0, SheetReader.MaxRows + 10).Select(_ => ";;\n"));

        var sheet = SheetReader.Csv(text, "t");

        Assert.Equal(2, sheet.Rows.Count);
        Assert.False(sheet.Clipped);
        Assert.Equal(2, sheet.TotalRows);
    }

    [Fact]
    public void Csv_PastTheRowLimit_IsClippedWithItsTotal() {
        var text = string.Concat(Enumerable.Range(0, SheetReader.MaxRows + 5).Select(i => $"{i},x\n"));

        var sheet = SheetReader.Csv(text, "t");

        Assert.Equal(SheetReader.MaxRows, sheet.Rows.Count);
        Assert.True(sheet.Clipped);
        Assert.Equal(SheetReader.MaxRows + 5, sheet.TotalRows);
    }

    [Fact]
    public void Csv_StartOfALongerFile_HasNoTotal() {
        var sheet = SheetReader.Csv("a\nb\n", "t", cut: true);

        Assert.True(sheet.Clipped);
        Assert.Null(sheet.TotalRows);
    }

    // --- XLSX ---------------------------------------------------------------

    [Fact]
    public void Xlsx_CellsLandInTheirColumnsAndRows() {
        var book = Xlsx(
            sheets: new[] { ("Данные", "<dimension ref=\"A1:C3\"/><sheetData>" +
                "<row r=\"1\"><c r=\"A1\" t=\"s\"><v>0</v></c><c r=\"C1\" t=\"s\"><v>1</v></c></row>" +
                "<row r=\"3\"><c r=\"B3\"><v>42</v></c><c r=\"C3\" t=\"inlineStr\"><is><t>inline</t></is></c></row>" +
                "</sheetData>") },
            shared: new[] { "<si><t>Имя</t></si>", "<si><r><t>Цена </t></r><r><t>за шт.</t></r></si>" });

        var sheet = Assert.Single(SheetReader.Xlsx(book)!);

        Assert.Equal("Данные", sheet.Name);
        Assert.Equal(new[] { "Имя", "", "Цена за шт." }, sheet.Rows[0]);
        Assert.Empty(sheet.Rows[1]);
        Assert.Equal(new[] { "", "42", "inline" }, sheet.Rows[2]);
    }

    [Fact]
    public void Xlsx_NumbersAreShownInTheirStyle() {
        var culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        try {
            NumbersInTheirStyle();
        } finally {
            CultureInfo.CurrentCulture = culture;
        }
    }

    private static void NumbersInTheirStyle() {
        var book = Xlsx(
            sheets: new[] { ("S", "<sheetData><row r=\"1\">" +
                "<c r=\"A1\" s=\"1\"><v>45292</v></c>" +             // built-in date
                "<c r=\"B1\" s=\"2\"><v>0.25</v></c>" +              // built-in percent
                "<c r=\"C1\" s=\"3\"><v>45292.5</v></c>" +           // custom date and time
                "<c r=\"D1\"><v>0.1</v></c>" +                       // general
                "<c r=\"E1\" t=\"b\"><v>1</v></c>" +
                "<c r=\"F1\" t=\"str\"><f>A1&amp;\"x\"</f><v>cached</v></c>" +
                "</row></sheetData>") },
            styles: "<numFmts><numFmt numFmtId=\"164\" formatCode=\"dd/mm/yyyy\\ hh:mm\"/></numFmts>" +
                "<cellStyleXfs><xf numFmtId=\"9\"/></cellStyleXfs>" +
                "<cellXfs><xf numFmtId=\"0\"/><xf numFmtId=\"14\"/><xf numFmtId=\"9\"/><xf numFmtId=\"164\"/></cellXfs>");

        var row = SheetReader.Xlsx(book)![0].Rows[0];

        Assert.Equal(new DateTime(2024, 1, 1).ToString("d", CultureInfo.InvariantCulture), row[0]);
        Assert.Equal("25%", row[1]);
        Assert.Equal(new DateTime(2024, 1, 1, 12, 0, 0).ToString("g", CultureInfo.InvariantCulture), row[2]);
        Assert.Equal("0.1", row[3]);
        Assert.Equal("TRUE", row[4]);
        Assert.Equal("cached", row[5]);
    }

    [Fact]
    public void Xlsx_HiddenSheetsAreLeftOut() {
        var book = Xlsx(sheets: new[] {
            ("Видимый", "<sheetData/>"),
            ("Скрытый", "<sheetData/>"),
        }, hidden: "Скрытый");

        var sheet = Assert.Single(SheetReader.Xlsx(book)!);

        Assert.Equal("Видимый", sheet.Name);
    }

    [Fact]
    public void Xlsx_PastTheRowLimit_TotalIsTheLastFilledRow() {
        // The dimension claims 5000, formatting does; the values end at 1500.
        var rows = string.Concat(Enumerable.Range(1, 1500).Select(r => $"<row r=\"{r}\"><c r=\"A{r}\"><v>{r}</v></c></row>"))
            + string.Concat(Enumerable.Range(1501, 3500).Select(r => $"<row r=\"{r}\"><c r=\"A{r}\" s=\"1\"/></row>"));
        var book = Xlsx(sheets: new[] { ("S", $"<dimension ref=\"A1:A5000\"/><sheetData>{rows}</sheetData>") });

        var sheet = SheetReader.Xlsx(book)![0];

        Assert.Equal(SheetReader.MaxRows, sheet.Rows.Count);
        Assert.True(sheet.Clipped);
        Assert.Equal(1500, sheet.TotalRows);
    }

    [Fact]
    public void Xlsx_FormattedEmptyRowsAtTheEnd_AreLeftOut() {
        var rows = string.Concat(Enumerable.Range(1, 3).Select(r => $"<row r=\"{r}\"><c r=\"A{r}\"><v>{r}</v></c></row>"))
            + string.Concat(Enumerable.Range(4, SheetReader.MaxRows + 500).Select(r => $"<row r=\"{r}\"><c r=\"A{r}\" s=\"1\"/><c r=\"B{r}\" t=\"s\"><v>0</v></c></row>"));
        var book = Xlsx(sheets: new[] { ("S", $"<sheetData>{rows}</sheetData>") }, shared: new[] { "<si><t></t></si>" });

        var sheet = SheetReader.Xlsx(book)![0];

        Assert.Equal(3, sheet.Rows.Count);
        Assert.False(sheet.Clipped);
        Assert.Equal(3, sheet.TotalRows);
    }

    [Theory]
    [InlineData("General", "Plain")]
    [InlineData("0.00", "Plain")]
    [InlineData("#,##0 \"дней\"", "Plain")]
    [InlineData("[$-419]d mmmm yyyy", "Date")]
    [InlineData("mmm-yy", "Date")]
    [InlineData("[h]:mm:ss", "Time")]
    [InlineData("h:mm AM/PM", "Time")]
    [InlineData("yyyy-mm-dd hh:mm", "DateTime")]
    [InlineData("0.0%", "Percent")]
    [InlineData("[Red]0.00;[Blue]-0.00", "Plain")]
    public void Xlsx_CustomFormatsAreReadForWhatTheyShow(string code, string expected) {
        Assert.Equal(expected, SheetReader.StyleOfCode(code).ToString());
    }

    // --- ODS ----------------------------------------------------------------

    [Fact]
    public void Ods_RepeatsExpandUpToTheLastFilledCell() {
        var book = Ods(
            "<table:table table:name=\"Лист1\">" +
            "<table:table-column table:number-columns-repeated=\"3\"/>" +
            "<table:table-row><table:table-cell office:value-type=\"string\"><text:p>a<text:s text:c=\"2\"/>b</text:p></table:table-cell>" +
            "<table:table-cell table:number-columns-repeated=\"2\"/>" +
            "<table:table-cell office:value-type=\"float\" office:value=\"1.5\"><text:p>1,50</text:p>" +
            "<office:annotation><text:p>комментарий</text:p></office:annotation></table:table-cell>" +
            "<table:table-cell table:number-columns-repeated=\"1020\"/></table:table-row>" +
            "<table:table-row table:number-rows-repeated=\"2\"><table:table-cell table:number-columns-repeated=\"1024\"/></table:table-row>" +
            "<table:table-row table:number-rows-repeated=\"2\"><table:table-cell><text:p>x</text:p><text:p>y</text:p></table:table-cell></table:table-row>" +
            "<table:table-row table:number-rows-repeated=\"1048000\"><table:table-cell table:number-columns-repeated=\"1024\"/></table:table-row>" +
            "</table:table>" +
            "<table:table table:name=\"Лист2\"/>");

        var sheets = SheetReader.Ods(book)!;

        Assert.Equal(new[] { "Лист1", "Лист2" }, sheets.Select(s => s.Name));
        var rows = sheets[0].Rows;
        Assert.Equal(5, rows.Count);
        Assert.Equal(new[] { "a  b", "", "", "1,50" }, rows[0]);
        Assert.Empty(rows[1]);
        Assert.Empty(rows[2]);
        Assert.Equal(new[] { "x\ny" }, rows[3]);
        Assert.Equal(new[] { "x\ny" }, rows[4]);
        Assert.Equal(5, sheets[0].TotalRows);
        Assert.Empty(sheets[1].Rows);
    }

    // --- Anything else ------------------------------------------------------

    [Fact]
    public void NotAWorkbook_IsNull() {
        Assert.Null(SheetReader.Xlsx(Zip(("readme.txt", "hi"))));
        Assert.Null(SheetReader.Ods(Zip(("readme.txt", "hi"))));
    }


    // --- Builders -----------------------------------------------------------

    private static MemoryStream Xlsx((string Name, string Body)[] sheets, string[]? shared = null, string? styles = null, string? hidden = null) {
        const string main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        const string rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        var entries = new List<(string, string)>();
        var list = new StringBuilder();
        var rels = new StringBuilder();
        for (int i = 0; i < sheets.Length; i++) {
            string state = sheets[i].Name == hidden ? " state=\"hidden\"" : "";
            list.Append($"<sheet name=\"{sheets[i].Name}\" sheetId=\"{i + 1}\"{state} r:id=\"rId{i + 1}\"/>");
            rels.Append($"<Relationship Id=\"rId{i + 1}\" Type=\"{rel}/worksheet\" Target=\"worksheets/sheet{i + 1}.xml\"/>");
            entries.Add(($"xl/worksheets/sheet{i + 1}.xml", $"<worksheet xmlns=\"{main}\">{sheets[i].Body}</worksheet>"));
        }
        entries.Add(("xl/workbook.xml", $"<workbook xmlns=\"{main}\" xmlns:r=\"{rel}\"><sheets>{list}</sheets></workbook>"));
        entries.Add(("xl/_rels/workbook.xml.rels",
            $"<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">{rels}</Relationships>"));
        if (shared is not null) {
            entries.Add(("xl/sharedStrings.xml", $"<sst xmlns=\"{main}\">{string.Concat(shared)}</sst>"));
        }
        if (styles is not null) {
            entries.Add(("xl/styles.xml", $"<styleSheet xmlns=\"{main}\">{styles}</styleSheet>"));
        }

        return Zip(entries.ToArray());
    }

    private static MemoryStream Ods(string tables) {
        string content = "<office:document-content " +
            "xmlns:office=\"urn:oasis:names:tc:opendocument:xmlns:office:1.0\" " +
            "xmlns:table=\"urn:oasis:names:tc:opendocument:xmlns:table:1.0\" " +
            "xmlns:text=\"urn:oasis:names:tc:opendocument:xmlns:text:1.0\">" +
            $"<office:body><office:spreadsheet>{tables}</office:spreadsheet></office:body></office:document-content>";

        return Zip(("mimetype", "application/vnd.oasis.opendocument.spreadsheet"), ("content.xml", content));
    }

    private static MemoryStream Zip(params (string Name, string Text)[] entries) {
        var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true)) {
            foreach (var (name, text) in entries) {
                using var writer = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false));
                writer.Write(text);
            }
        }
        stream.Position = 0;

        return stream;
    }
}
