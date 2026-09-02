using System.IO;
using System.IO.Compression;
using System.Text;

namespace Wander.Harness.Sandbox;

/// <summary>
/// The documents the <c>docs</c> profile is made of. Every one of them is
/// assembled here rather than copied from a fixture, which is the point:
/// they are the smallest file a reader will still accept, so what they
/// prove is that the reader works, not that one particular file from one
/// particular editor does.
///
/// <para>
/// Each file carries the token <see cref="Needle"/> somewhere in its prose
/// - and only there, never in a name or a style table. A content search
/// for it is then a count: every format whose extractor works answers,
/// every format whose extractor is broken is missing from the results, and
/// the difference is visible without opening anything.
/// </para>
/// </summary>
public static class DocumentFactory {
    /// <summary>The word every generated document hides in its text, and nothing else in the sandbox contains.</summary>
    public const string Needle = "needle42";

    private const string Prose =
        "The quick brown fox jumps over the lazy dog. " + Needle +
        " lives in this paragraph and nowhere else in the file.";

    private const string Cyrillic = "Проверка кодировки: съешь ещё этих мягких французских булок.";


    /// <summary>Writes the whole set into <paramref name="dir"/>.</summary>
    public static void WriteAll(string dir) {
        Docx(Path.Combine(dir, "report.docx"));
        Xlsx(Path.Combine(dir, "budget.xlsx"));
        Pptx(Path.Combine(dir, "deck.pptx"));
        Odt(Path.Combine(dir, "notes.odt"));
        Epub(Path.Combine(dir, "book.epub"));
        Fb2(Path.Combine(dir, "story.fb2"));
        Markdown(Path.Combine(dir, "readme.md"));
        Rtf(Path.Combine(dir, "letter.rtf"));
        Pdf(Path.Combine(dir, "manual.pdf"));
        Html(Path.Combine(dir, "page.html"));
        TextEncodings(dir);
    }


    // --- Zip-and-XML formats -------------------------------------------

    /// <summary>
    /// Word. <c>ZipDocumentExtractor</c> reads <c>word/document*</c>, so
    /// that entry is the whole test; the content types part is there
    /// because Word itself refuses a package without one and the file is
    /// meant to open.
    /// </summary>
    private static void Docx(string path) {
        Zip(path,
            ("[Content_Types].xml", ContentTypes(
                "<Override PartName=\"/word/document.xml\" " +
                "ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/>")),
            ("_rels/.rels", Rels("http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument", "word/document.xml")),
            ("word/document.xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
                "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body>" +
                $"<w:p><w:r><w:t>{Prose}</w:t></w:r></w:p>" +
                $"<w:p><w:r><w:t>{Cyrillic}</w:t></w:r></w:p>" +
                "</w:body></w:document>"));
    }

    /// <summary>Excel: the prose of a workbook lives in the shared string table.</summary>
    private static void Xlsx(string path) {
        Zip(path,
            ("[Content_Types].xml", ContentTypes(
                "<Override PartName=\"/xl/workbook.xml\" " +
                "ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>")),
            ("_rels/.rels", Rels("http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument", "xl/workbook.xml")),
            ("xl/workbook.xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
                "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" +
                "<sheets><sheet name=\"Sheet1\" sheetId=\"1\"/></sheets></workbook>"),
            ("xl/sharedStrings.xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
                "<sst xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" count=\"2\" uniqueCount=\"2\">" +
                $"<si><t>{Prose}</t></si><si><t>{Cyrillic}</t></si></sst>"),
            ("xl/worksheets/sheet1.xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
                "<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>" +
                "<row r=\"1\"><c r=\"A1\" t=\"s\"><v>0</v></c><c r=\"B1\" t=\"s\"><v>1</v></c></row>" +
                "</sheetData></worksheet>"));
    }

    /// <summary>PowerPoint: one slide, one text box.</summary>
    private static void Pptx(string path) {
        Zip(path,
            ("[Content_Types].xml", ContentTypes(
                "<Override PartName=\"/ppt/presentation.xml\" " +
                "ContentType=\"application/vnd.openxmlformats-officedocument.presentationml.presentation.main+xml\"/>")),
            ("_rels/.rels", Rels("http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument", "ppt/presentation.xml")),
            ("ppt/slides/slide1.xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
                "<p:sld xmlns:p=\"http://schemas.openxmlformats.org/presentationml/2006/main\" " +
                "xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\">" +
                $"<p:cSld><p:spTree><p:sp><p:txBody><a:p><a:r><a:t>{Prose}</a:t></a:r></a:p>" +
                "</p:txBody></p:sp></p:spTree></p:cSld></p:sld>"));
    }

    /// <summary>OpenDocument keeps the whole document in one part, which makes it the shortest of the five.</summary>
    private static void Odt(string path) {
        Zip(path,
            ("mimetype", "application/vnd.oasis.opendocument.text"),
            ("content.xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
                "<office:document-content xmlns:office=\"urn:oasis:names:tc:opendocument:xmlns:office:1.0\" " +
                "xmlns:text=\"urn:oasis:names:tc:opendocument:xmlns:text:1.0\"><office:body><office:text>" +
                $"<text:p>{Prose}</text:p><text:p>{Cyrillic}</text:p>" +
                "</office:text></office:body></office:document-content>"));
    }

    /// <summary>EPUB: the chapters are XHTML, wherever the publisher put them.</summary>
    private static void Epub(string path) {
        Zip(path,
            ("mimetype", "application/epub+zip"),
            ("META-INF/container.xml",
                "<?xml version=\"1.0\"?>" +
                "<container version=\"1.0\" xmlns=\"urn:oasis:names:tc:opendocument:xmlns:container\">" +
                "<rootfiles><rootfile full-path=\"OEBPS/content.opf\" " +
                "media-type=\"application/oebps-package+xml\"/></rootfiles></container>"),
            ("OEBPS/content.opf",
                "<?xml version=\"1.0\"?>" +
                "<package xmlns=\"http://www.idpf.org/2007/opf\" version=\"2.0\" unique-identifier=\"id\">" +
                "<metadata xmlns:dc=\"http://purl.org/dc/elements/1.1/\">" +
                "<dc:title>Harness sample</dc:title><dc:identifier id=\"id\">harness-1</dc:identifier></metadata>" +
                "<manifest><item id=\"c1\" href=\"chapter1.xhtml\" media-type=\"application/xhtml+xml\"/></manifest>" +
                "<spine><itemref idref=\"c1\"/></spine></package>"),
            ("OEBPS/chapter1.xhtml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
                "<html xmlns=\"http://www.w3.org/1999/xhtml\"><head><title>Chapter one</title></head>" +
                $"<body><h1>Chapter one</h1><p>{Prose}</p><p>{Cyrillic}</p></body></html>"));
    }


    // --- Single-file formats -------------------------------------------

    /// <summary>
    /// FictionBook, with a cover binary: the pane parses this into HTML
    /// itself, and the binary element is the part that proves the data-URI
    /// path rather than just the text one.
    /// </summary>
    private static void Fb2(string path) {
        string cover = Convert.ToBase64String(PictureFactory.Jpeg(320, 480, 1, "FB2", seed: 7));
        var xml = new StringBuilder();
        xml.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\r\n");
        xml.Append("<FictionBook xmlns=\"http://www.gribuser.ru/xml/fictionbook/2.0\" ");
        xml.Append("xmlns:l=\"http://www.w3.org/1999/xlink\">\r\n");
        xml.Append("<description><title-info>");
        xml.Append("<book-title>Проверочная книга</book-title>");
        xml.Append("<author><first-name>Тест</first-name><last-name>Харнесс</last-name></author>");
        xml.Append("<coverpage><image l:href=\"#cover.jpg\"/></coverpage>");
        xml.Append($"<annotation><p>{Prose}</p></annotation>");
        xml.Append("</title-info></description>\r\n");
        xml.Append("<body><section><title><p>Глава первая</p></title>");
        xml.Append($"<p>{Cyrillic}</p><p>{Prose}</p></section></body>\r\n");
        xml.Append($"<binary id=\"cover.jpg\" content-type=\"image/jpeg\">{cover}</binary>\r\n");
        xml.Append("</FictionBook>\r\n");
        File.WriteAllText(path, xml.ToString(), new UTF8Encoding(false));
    }

    /// <summary>Markdown, with the table that is the reason the pane renders it rather than showing it as text.</summary>
    private static void Markdown(string path) {
        File.WriteAllText(path,
            "# Harness readme\r\n\r\n" +
            Prose + "\r\n\r\n" +
            "| Format | Reader | Note |\r\n" +
            "|---|---|---|\r\n" +
            "| docx | ZipDocumentExtractor | zip of XML |\r\n" +
            "| doc | IFilter | OffFilt.dll |\r\n" +
            "| pdf | WebView2 | first page in the thumbnail |\r\n\r\n" +
            "- " + Cyrillic + "\r\n" +
            "- `inline code`, **bold**, [a link](https://example.invalid/)\r\n",
            new UTF8Encoding(false));
    }

    /// <summary>
    /// RTF, read natively into a flow document. Cyrillic goes in as
    /// backslash-escaped bytes under ansicpg1251, which is what Word writes
    /// and what the reader has to survive.
    /// </summary>
    private static void Rtf(string path) {
        var body = new StringBuilder();
        body.Append("{\\rtf1\\ansi\\ansicpg1251\\deff0{\\fonttbl{\\f0 Times New Roman;}}\r\n");
        body.Append("\\f0\\fs24 ").Append(Prose).Append("\\par\r\n");
        foreach (char c in Cyrillic) {
            body.Append(c < 128 ? c.ToString() : $"\\'{CyrillicEncoding.Windows1251(c):x2}");
        }
        body.Append("\\par\r\n}\r\n");
        File.WriteAllBytes(path, Encoding.ASCII.GetBytes(body.ToString()));
    }

    /// <summary>
    /// A PDF with one page of text, written out object by object. Hand
    /// assembly is the only way to get a file this small, and the byte
    /// offsets in the cross-reference table are why it is worth doing once:
    /// they have to be exact or no reader will open it.
    /// </summary>
    private static void Pdf(string path) {
        string text =
            "BT /F1 14 Tf 60 760 Td (Harness sample PDF) Tj ET\n" +
            $"BT /F1 11 Tf 60 730 Td ({Prose}) Tj ET\n";

        var objects = new[] {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] " +
            "/Resources << /Font << /F1 5 0 R >> >> /Contents 4 0 R >>",
            $"<< /Length {text.Length} >>\nstream\n{text}endstream",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
        };

        var pdf = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        for (int i = 0; i < objects.Length; i++) {
            offsets.Add(pdf.Length);
            pdf.Append(i + 1).Append(" 0 obj\n").Append(objects[i]).Append("\nendobj\n");
        }

        int xref = pdf.Length;
        pdf.Append("xref\n0 ").Append(objects.Length + 1).Append('\n');
        pdf.Append("0000000000 65535 f \n");
        foreach (int offset in offsets) {
            pdf.Append(offset.ToString("0000000000")).Append(" 00000 n \n");
        }
        pdf.Append("trailer\n<< /Size ").Append(objects.Length + 1).Append(" /Root 1 0 R >>\n");
        pdf.Append("startxref\n").Append(xref).Append("\n%%EOF\n");

        // Latin-1, so one character is one byte and the offsets above hold.
        File.WriteAllBytes(path, Encoding.Latin1.GetBytes(pdf.ToString()));
    }

    /// <summary>
    /// A page that reaches outside: an image by http and a fetch to a host
    /// that does not exist. Nothing here should ever load - the preview's
    /// web view is meant to be isolated, and a run where these resolve is
    /// the finding.
    /// </summary>
    private static void Html(string path) {
        File.WriteAllText(path,
            "<!doctype html>\r\n<html lang=\"en\"><head><meta charset=\"utf-8\">\r\n" +
            "<title>Harness page</title></head>\r\n<body>\r\n" +
            $"<h1>Harness page</h1>\r\n<p>{Prose}</p>\r\n<p>{Cyrillic}</p>\r\n" +
            "<img src=\"http://example.invalid/tracker.png\" alt=\"remote image\" width=\"64\" height=\"64\">\r\n" +
            "<script>\r\n" +
            "  fetch('http://example.invalid/beacon?from=preview')\r\n" +
            "    .then(r => { document.title = 'fetched'; })\r\n" +
            "    .catch(() => { document.title = 'blocked'; });\r\n" +
            "</script>\r\n</body></html>\r\n",
            new UTF8Encoding(false));
    }

    /// <summary>
    /// The same sentence in four encodings. Byte-order marks on the
    /// Unicode two, nothing to go on but the bytes for the other two -
    /// which is what <c>EncodingProbe</c> is for.
    /// </summary>
    private static void TextEncodings(string dir) {
        string text = Cyrillic + "\r\n" + Prose + "\r\n";
        // GetBytes does not include the preamble - the mark has to be
        // written in front by hand, and forgetting that is how a UTF-16
        // file ends up looking like something else to a detector.
        WithBom(Path.Combine(dir, "utf8.txt"), new UTF8Encoding(true), text);
        WithBom(Path.Combine(dir, "utf16.txt"), new UnicodeEncoding(false, true), text);
        File.WriteAllBytes(Path.Combine(dir, "cp1251.txt"), CyrillicEncoding.Encode(text, CyrillicEncoding.Windows1251));
        File.WriteAllBytes(Path.Combine(dir, "cp866.txt"), CyrillicEncoding.Encode(text, CyrillicEncoding.Dos866));
    }


    // --- Building blocks -----------------------------------------------

    private static void WithBom(string path, Encoding encoding, string text) {
        File.WriteAllBytes(path, encoding.GetPreamble().Concat(encoding.GetBytes(text)).ToArray());
    }

    private static void Zip(string path, params (string Name, string Text)[] entries) {
        using var file = File.Create(path);
        using var zip = new ZipArchive(file, ZipArchiveMode.Create);
        foreach (var (name, text) in entries) {
            // Stored, not deflated, for "mimetype": the EPUB specification
            // requires it, and one uncompressed entry in a package this
            // small costs nothing anywhere else.
            var entry = zip.CreateEntry(name, name == "mimetype" ? CompressionLevel.NoCompression : CompressionLevel.Optimal);
            using var stream = entry.Open();
            byte[] bytes = new UTF8Encoding(false).GetBytes(text);
            stream.Write(bytes, 0, bytes.Length);
        }
    }

    private static string ContentTypes(string overrides) {
        return
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
            "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
            "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
            "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
            overrides + "</Types>";
    }

    private static string Rels(string type, string target) {
        return
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
            "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
            $"<Relationship Id=\"rId1\" Type=\"{type}\" Target=\"{target}\"/></Relationships>";
    }
}


/// <summary>
/// Encodes Cyrillic into the two single-byte codepages the app has to
/// recognise. .NET ships neither: everything past Latin-1 needs the
/// <c>System.Text.Encoding.CodePages</c> package, and Core answered the
/// same question with its own tables rather than take the dependency
/// (see <c>EncodingProbe</c>). Three lines of arithmetic here keep the
/// harness on the same footing.
/// </summary>
public static class CyrillicEncoding {
    /// <summary>Windows-1251: the alphabet is one contiguous run from 0xC0.</summary>
    public static byte Windows1251(char c) {
        return c switch {
            'Ё' => 0xA8,
            'ё' => 0xB8,
            >= 'А' and <= 'я' => (byte)(0xC0 + (c - 'А')),
            _ => (byte)'?',
        };
    }

    /// <summary>
    /// Codepage 866, where IBM split the alphabet around the box-drawing
    /// characters: А-п runs from 0x80, then р-я resumes at 0xE0.
    /// </summary>
    public static byte Dos866(char c) {
        return c switch {
            'Ё' => 0xF0,
            'ё' => 0xF1,
            >= 'А' and <= 'п' => (byte)(0x80 + (c - 'А')),
            >= 'р' and <= 'я' => (byte)(0xE0 + (c - 'р')),
            _ => (byte)'?',
        };
    }

    public static byte[] Encode(string text, Func<char, byte> codepage) {
        var bytes = new byte[text.Length];
        for (int i = 0; i < text.Length; i++) {
            bytes[i] = text[i] < 128 ? (byte)text[i] : codepage(text[i]);
        }

        return bytes;
    }
}
