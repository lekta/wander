using System.IO.Compression;
using System.Text;
using Wander.Core.Preview;

namespace Wander.Core.Tests;

/// <summary>Where each zip-based format keeps the picture of itself.</summary>
public class EmbeddedThumbnailTests {
    private static readonly byte[] _picture = { 0x89, (byte)'P', (byte)'N', (byte)'G', 1, 2, 3 };


    [Fact]
    public void OpenDocument_HasItsThumbnailsFolder() {
        var file = Zip(("mimetype", Encoding.ASCII.GetBytes("application/vnd.oasis.opendocument.text")), ("Thumbnails/thumbnail.png", _picture));

        Assert.Equal(_picture, EmbeddedThumbnail.Read(file, ".odt"));
    }

    [Fact]
    public void Package_FollowsItsThumbnailRelationship() {
        const string rels = "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
            "<Relationship Id=\"r1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/>" +
            "<Relationship Id=\"r2\" Type=\"http://schemas.openxmlformats.org/package/2006/relationships/metadata/thumbnail\" Target=\"/Metadata/plate_1.png\"/>" +
            "</Relationships>";
        var file = Zip(("_rels/.rels", Encoding.UTF8.GetBytes(rels)), ("Metadata/plate_1.png", _picture));

        Assert.Equal(_picture, EmbeddedThumbnail.Read(file, ".3mf"));
    }

    [Fact]
    public void Comic_IsItsFirstPageByName() {
        var file = Zip(
            ("__MACOSX/._001.jpg", new byte[] { 9 }),
            ("pages/010.jpg", new byte[] { 2 }),
            ("pages/001.jpg", _picture),
            ("info.txt", new byte[] { 3 }));

        Assert.Equal(_picture, EmbeddedThumbnail.Read(file, ".cbz"));
    }

    [Fact]
    public void Krita_TakesThePreview_ElseTheFlattenedImage() {
        Assert.Equal(_picture, EmbeddedThumbnail.Read(Zip(("mergedimage.png", new byte[] { 1 }), ("preview.png", _picture)), ".kra"));
        Assert.Equal(_picture, EmbeddedThumbnail.Read(Zip(("mergedimage.png", _picture)), ".kra"));
    }

    [Fact]
    public void NoPicture_OrNoZip_IsNull() {
        Assert.Null(EmbeddedThumbnail.Read(Zip(("content.xml", new byte[] { 1 })), ".ods"));
        Assert.Null(EmbeddedThumbnail.Read(new MemoryStream(new byte[] { 1, 2, 3 }), ".docx"));
    }


    private static MemoryStream Zip(params (string Name, byte[] Bytes)[] entries) {
        var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true)) {
            foreach (var (name, bytes) in entries) {
                using var entry = zip.CreateEntry(name).Open();
                entry.Write(bytes);
            }
        }
        stream.Position = 0;

        return stream;
    }
}
