using System.IO.Compression;
using System.Text;
using Wander.Core.Search;
using Wander.Core.Tests.Fakes;

namespace Wander.Core.Tests;

public class ContentExtractorTests {
    // --- Plain text ----------------------------------------------------

    [Fact]
    public void PlainText_ReadsUtf8() {
        var fs = new FakeFileSystem();
        fs.Files[@"C:\note.txt"] = Encoding.UTF8.GetBytes("hello world");

        Assert.Equal("hello world", new PlainTextExtractor(fs).Extract(@"C:\note.txt", default));
    }


    [Fact]
    public void PlainText_DecodesWindows1251() {
        // The reason search does not match raw bytes: the same words in
        // two codepages have to become the same string before the query
        // sees them.
        var fs = new FakeFileSystem();
        var cp1251 = new byte[] {
            0xCF, 0xF0, 0xE8, 0xE2, 0xE5, 0xF2, 0x2C, 0x20,     // Привет,
            0xEC, 0xE8, 0xF0, 0x21,                             // мир!
            0x20, 0x54, 0x68, 0x65, 0x20, 0x71, 0x75, 0x69, 0x63, 0x6B,
            0x20, 0x62, 0x72, 0x6F, 0x77, 0x6E, 0x20, 0x66, 0x6F, 0x78,
        };
        fs.Files[@"C:\note.txt"] = cp1251;

        string? text = new PlainTextExtractor(fs).Extract(@"C:\note.txt", default);

        Assert.NotNull(text);
        Assert.Contains("Привет, мир!", text);
    }


    [Fact]
    public void PlainText_RejectsBinary() {
        var fs = new FakeFileSystem();
        fs.Files[@"C:\blob.asset"] = new byte[] { 0x00, 0x01, 0x02, 0x00, 0xFF, 0x00 };

        Assert.Null(new PlainTextExtractor(fs).Extract(@"C:\blob.asset", default));
    }


    [Fact]
    public void PlainText_MissingFile_ReturnsNull() {
        // Extractors answer null rather than throwing: one unreadable file
        // must not end a search over ten thousand of them.
        var fs = new FakeFileSystem();

        Assert.Null(new PlainTextExtractor(fs).Extract(@"C:\gone.txt", default));
    }


    [Fact]
    public void PlainText_ClaimsEverything_AndIsCheap() {
        var extractor = new PlainTextExtractor(new FakeFileSystem());

        Assert.True(extractor.CanExtract(@"C:\anything.whatever"));
        Assert.False(extractor.IsExpensive);
    }


    // --- Zip documents -------------------------------------------------

    [Fact]
    public void ZipDocument_ClaimsOnlyItsFormats() {
        var extractor = new ZipDocumentExtractor(new FakeFileSystem());

        Assert.True(extractor.CanExtract(@"C:\a.docx"));
        Assert.True(extractor.CanExtract(@"C:\a.EPUB"));
        Assert.False(extractor.CanExtract(@"C:\a.doc"));
        Assert.False(extractor.CanExtract(@"C:\a.txt"));
        Assert.True(extractor.IsExpensive);
    }


    [Fact]
    public void ZipDocument_ReadsWordBody_AcrossRuns() {
        // Word splits a sentence into a run per formatting change; without
        // a separator between text nodes the words would fuse into
        // something no query matches.
        var fs = new FakeFileSystem();
        fs.Files[@"C:\report.docx"] = Zip(
            ("word/document.xml",
                "<w:document xmlns:w='x'><w:body><w:p><w:r><w:t>Квартальный</w:t></w:r>" +
                "<w:r><w:t>отчёт</w:t></w:r></w:p></w:body></w:document>"),
            ("word/styles.xml", "<w:styles xmlns:w='x'><w:style><w:name>Heading</w:name></w:style></w:styles>"));

        string? text = new ZipDocumentExtractor(fs).Extract(@"C:\report.docx", default);

        Assert.NotNull(text);
        Assert.Contains("Квартальный отчёт", text);
        // Style names are plumbing: searching them turns "find Heading"
        // into a hit on every document in the folder.
        Assert.DoesNotContain("Heading", text);
    }


    [Fact]
    public void ZipDocument_ReadsExcelSharedStrings() {
        var fs = new FakeFileSystem();
        fs.Files[@"C:\book.xlsx"] = Zip(
            ("xl/sharedStrings.xml", "<sst xmlns='x'><si><t>Итого за год</t></si></sst>"),
            ("xl/theme/theme1.xml", "<theme xmlns='x'><name>Office</name></theme>"));

        string? text = new ZipDocumentExtractor(fs).Extract(@"C:\book.xlsx", default);

        Assert.NotNull(text);
        Assert.Contains("Итого за год", text);
        Assert.DoesNotContain("Office", text);
    }


    [Fact]
    public void ZipDocument_ReadsEpubChapters() {
        var fs = new FakeFileSystem();
        fs.Files[@"C:\book.epub"] = Zip(
            ("OEBPS/chapter1.xhtml", "<html xmlns='http://www.w3.org/1999/xhtml'><body><p>Call me Ishmael</p></body></html>"),
            ("META-INF/container.xml", "<container><rootfiles/></container>"));

        string? text = new ZipDocumentExtractor(fs).Extract(@"C:\book.epub", default);

        Assert.NotNull(text);
        Assert.Contains("Call me Ishmael", text);
    }


    [Fact]
    public void ZipDocument_DamagedFile_ReturnsNull() {
        var fs = new FakeFileSystem();
        fs.Files[@"C:\broken.docx"] = Encoding.UTF8.GetBytes("this is not a zip at all");

        Assert.Null(new ZipDocumentExtractor(fs).Extract(@"C:\broken.docx", default));
    }


    [Fact]
    public void ZipDocument_NoTextParts_ReturnsNull() {
        var fs = new FakeFileSystem();
        fs.Files[@"C:\empty.docx"] = Zip(("[Content_Types].xml", "<Types xmlns='x'/>"));

        Assert.Null(new ZipDocumentExtractor(fs).Extract(@"C:\empty.docx", default));
    }


    private static byte[] Zip(params (string Path, string Content)[] entries) {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true)) {
            foreach (var (path, content) in entries) {
                using var writer = new StreamWriter(zip.CreateEntry(path).Open(), Encoding.UTF8);
                writer.Write(content);
            }
        }

        return buffer.ToArray();
    }
}
