using System.IO.Compression;
using System.Text;
using Wander.Core.Preview;

namespace Wander.Core.Tests;

public class BookCoverTests {
    private static readonly byte[] _cover = { 0x89, 0x50, 0x4E, 0x47 };


    /// <summary>
    /// Builds an EPUB in memory. Nothing here touches the disk: an EPUB is
    /// a zip and a couple of XML files, and the code under test walks the
    /// links between them — which is exactly what a hand-built archive
    /// exercises.
    /// </summary>
    private static Stream Epub(
        string opf,
        string opfPath = "OEBPS/content.opf",
        string coverPath = "OEBPS/images/cover.png",
        bool withContainer = true) {

        var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true)) {
            if (withContainer) {
                Write(zip, "META-INF/container.xml", Encoding.UTF8.GetBytes(
                    $@"<container xmlns='urn:oasis:names:tc:opendocument:xmlns:container'>
                        <rootfiles><rootfile full-path='{opfPath}' media-type='application/oebps-package+xml'/></rootfiles>
                    </container>"));
            }
            Write(zip, opfPath, Encoding.UTF8.GetBytes(opf));
            Write(zip, coverPath, _cover);
        }

        buffer.Position = 0;

        return buffer;
    }

    private static void Write(ZipArchive zip, string name, byte[] content) {
        using var stream = zip.CreateEntry(name).Open();
        stream.Write(content);
    }


    [Fact]
    public void Supports_KnowsTheBookExtensions() {
        Assert.True(BookCover.Supports(@"C:\books\novel.fb2"));
        Assert.True(BookCover.Supports(@"C:\books\NOVEL.EPUB"));
        Assert.False(BookCover.Supports(@"C:\books\scan.djvu"));
        Assert.False(BookCover.Supports(@"C:\books\notes.txt"));
    }


    [Fact]
    public void Epub3_ReadsTheItemMarkedCoverImage() {
        string opf = @"<package xmlns='http://www.idpf.org/2007/opf'><manifest>
            <item id='t' href='text.xhtml' media-type='application/xhtml+xml'/>
            <item id='c' href='images/cover.png' media-type='image/png' properties='cover-image'/>
        </manifest></package>";

        Assert.Equal(_cover, BookCover.ReadEpubCover(Epub(opf)));
    }


    [Fact]
    public void Epub2_FollowsTheCoverMetaToItsItem() {
        string opf = @"<package xmlns='http://www.idpf.org/2007/opf'>
            <metadata><meta name='cover' content='c'/></metadata>
            <manifest>
                <item id='c' href='images/cover.png' media-type='image/png'/>
                <item id='t' href='text.xhtml' media-type='application/xhtml+xml'/>
            </manifest>
        </package>";

        Assert.Equal(_cover, BookCover.ReadEpubCover(Epub(opf)));
    }


    /// <summary>
    /// Neither marking present — the guess is the image whose name says
    /// what it is.
    /// </summary>
    [Fact]
    public void UnmarkedCover_IsFoundByName() {
        string opf = @"<package xmlns='http://www.idpf.org/2007/opf'><manifest>
            <item id='t' href='text.xhtml' media-type='application/xhtml+xml'/>
            <item id='c' href='images/cover.png' media-type='image/png'/>
        </manifest></package>";

        Assert.Equal(_cover, BookCover.ReadEpubCover(Epub(opf)));
    }


    /// <summary>
    /// A manifest with no cover of any kind: nothing to draw, and guessing
    /// at an arbitrary illustration would put the wrong picture on the tile.
    /// </summary>
    [Fact]
    public void NoCover_ReturnsNull() {
        string opf = @"<package xmlns='http://www.idpf.org/2007/opf'><manifest>
            <item id='t' href='text.xhtml' media-type='application/xhtml+xml'/>
        </manifest></package>";

        Assert.Null(BookCover.ReadEpubCover(Epub(opf)));
    }


    /// <summary>
    /// Hrefs are relative to the OPF, so a package document at the root
    /// resolves its cover differently from one in OEBPS/.
    /// </summary>
    [Fact]
    public void HrefsResolveAgainstTheOpfFolder() {
        string opf = @"<package xmlns='http://www.idpf.org/2007/opf'><manifest>
            <item id='c' href='cover.png' media-type='image/png' properties='cover-image'/>
        </manifest></package>";

        Assert.Equal(_cover, BookCover.ReadEpubCover(
            Epub(opf, opfPath: "content.opf", coverPath: "cover.png")));
    }


    /// <summary>
    /// EPUBs with a missing or unreadable container.xml turn up regularly;
    /// the package document can still be found by its extension.
    /// </summary>
    [Fact]
    public void MissingContainer_FindsTheOpfByExtension() {
        string opf = @"<package xmlns='http://www.idpf.org/2007/opf'><manifest>
            <item id='c' href='images/cover.png' media-type='image/png' properties='cover-image'/>
        </manifest></package>";

        Assert.Equal(_cover, BookCover.ReadEpubCover(Epub(opf, withContainer: false)));
    }


    [Fact]
    public void PercentEncodedHref_ResolvesToItsEntry() {
        string opf = @"<package xmlns='http://www.idpf.org/2007/opf'><manifest>
            <item id='c' href='images/my%20cover.png' media-type='image/png' properties='cover-image'/>
        </manifest></package>";

        Assert.Equal(_cover, BookCover.ReadEpubCover(
            Epub(opf, coverPath: "OEBPS/images/my cover.png")));
    }
}
