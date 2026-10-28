using System.IO.Compression;
using System.Text;
using Wander.Core.Shell;

namespace Wander.Core.Tests;

/// <summary>The census of a zip's folders, from its directory alone.</summary>
public class ZipCatalogTests {
    [Fact]
    public void TheWholeArchive_CountsItsFilesFoldersAndUnpackedSize() {
        using var zip = Zip(("CBCT/autorun.inf", 10), ("CBCT/icons/a.ico", 100), ("CBCT/icons/b.ico", 200), ("CBCT/data/x.dcm", 1000));

        var stats = ZipCatalog.Read(zip, 866).Census("")!;

        Assert.Equal(4, stats.Files);
        // CBCT, CBCT/icons, CBCT/data - none of them listed as an entry.
        Assert.Equal(3, stats.Folders);
        Assert.Equal(1310, stats.TotalSize);
        Assert.Equal(new[] { "dcm", "ico", "inf" }, stats.Types.Select(t => t.Extension));
    }


    [Fact]
    public void AFolderInside_CountsWhatIsUnderIt_ByTheShellsPath() {
        using var zip = Zip(("CBCT/autorun.inf", 10), ("CBCT/icons/a.ico", 100), ("CBCT/icons/b.ico", 200), ("CBCT/iconsx/c.ico", 5));
        var catalog = ZipCatalog.Read(zip, 866);

        var stats = catalog.Census(@"cbct\ICONS")!;

        Assert.Equal(2, stats.Files);
        Assert.Equal(0, stats.Folders);
        Assert.Equal(300, stats.TotalSize);
        Assert.Null(catalog.Census(@"CBCT\missing"));
    }


    [Fact]
    public void AListedEmptyFolder_IsAFolder() {
        using var zip = Zip(("docs/", 0), ("docs/empty/", 0), ("docs/a.txt", 3));

        var stats = ZipCatalog.Read(zip, 866).Census("docs")!;

        Assert.Equal(1, stats.Files);
        Assert.Equal(1, stats.Folders);
    }


    [Fact]
    public void ANameNotFlaggedUtf8_IsReadInTheOemCodePage() {
        // "Фото/1.jpg" as DOS wrote it: 866 bytes, no UTF-8 flag. Latin-1
        // on the way in puts exactly those bytes into the entry.
        string dosBytes = Encoding.Latin1.GetString(new byte[] { 0x94, 0xAE, 0xE2, 0xAE }) + "/1.jpg";
        using var zip = Zip(Encoding.Latin1, (dosBytes, 42));

        var stats = ZipCatalog.Read(zip, 866).Census("Фото")!;

        Assert.Equal(1, stats.Files);
        Assert.Equal(42, stats.TotalSize);
    }


    [Fact]
    public void NotAZip_Throws() {
        using var stream = new MemoryStream(Encoding.ASCII.GetBytes("7z is not a zip, nor is this"));

        Assert.Throws<InvalidDataException>(() => ZipCatalog.Read(stream, 866));
    }


    private static MemoryStream Zip(params (string Name, int Size)[] entries) => Zip(null, entries);

    private static MemoryStream Zip(Encoding? names, params (string Name, int Size)[] entries) {
        var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true, entryNameEncoding: names)) {
            foreach (var (name, size) in entries) {
                using var body = zip.CreateEntry(name).Open();
                body.Write(new byte[size]);
            }
        }
        stream.Position = 0;

        return stream;
    }
}
