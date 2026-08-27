using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;

namespace Wander.Core.Preview;

/// <summary>
/// The picture on the front of a book file, for the tile views.
///
/// <para>
/// A shelf of books drawn as identical grey document icons is a shelf you
/// have to read to navigate. Every format here already carries its cover
/// inside it — FB2 as a base64 blob, EPUB as an entry in its zip — so the
/// tile can show the thing the user recognises instead of the extension.
/// </para>
///
/// <para>
/// Formats whose cover we can't reach (DjVu, CHM, the older Word formats)
/// are simply not listed: <see cref="Supports"/> answering false leaves the
/// caller on the shell icon it would have drawn anyway.
/// </para>
/// </summary>
public static class BookCover {
    /// <summary>
    /// Books past this size are skipped. Not a parsing limit — both readers
    /// stream — but a floor under how long a listing may block on one row
    /// of a folder full of scanned volumes.
    /// </summary>
    private const long MaxFileSize = 64L * 1024 * 1024;

    private static readonly HashSet<string> _extensions = new(StringComparer.OrdinalIgnoreCase) {
        ".fb2", ".epub",
    };


    /// <summary>True when <paramref name="path"/> names a format whose cover this can read.</summary>
    public static bool Supports(string path) {
        return _extensions.Contains(Path.GetExtension(path));
    }


    /// <summary>
    /// The cover image bytes in whatever format the book stores them
    /// (usually JPEG or PNG), or null when there is no cover, the file is
    /// damaged, or it is too large to be worth opening for one picture.
    /// </summary>
    public static byte[]? TryRead(string path) {
        if (!Supports(path)) {
            return null;
        }

        try {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > MaxFileSize) {
                return null;
            }

            using var stream = File.OpenRead(path);

            return Path.GetExtension(path).Equals(".epub", StringComparison.OrdinalIgnoreCase)
                ? ReadEpubCover(stream)
                : Fb2Document.ReadCover(stream);
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException) {
            return null;
        }
    }


    // --- EPUB ----------------------------------------------------------
    //
    // An EPUB is a zip with a manifest. The path to the cover is three
    // hops away and each hop is optional, so every step falls through to
    // the next guess rather than giving up:
    //
    //   META-INF/container.xml  →  the OPF package document
    //   OPF <manifest>          →  the item marked as the cover
    //   the item's href         →  the entry holding the image
    //
    // The "marked as the cover" step has two spellings — EPUB 3's
    // properties="cover-image" and EPUB 2's <meta name="cover"> pointing at
    // an item id — and plenty of files use neither, which is what the
    // name-shaped fallback is for.

    internal static byte[]? ReadEpubCover(Stream stream) {
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);

        string? opfPath = FindOpfPath(zip);
        if (opfPath is null) {
            return null;
        }

        var opf = LoadXml(zip.GetEntry(opfPath));
        if (opf is null) {
            return null;
        }

        string? href = FindCoverHref(opf);
        if (href is null) {
            return null;
        }

        // Hrefs in the manifest are relative to the OPF, which usually
        // lives in a subfolder ("OEBPS/content.opf").
        string? folder = Path.GetDirectoryName(opfPath)?.Replace('\\', '/');
        string full = string.IsNullOrEmpty(folder) ? href : folder + "/" + href;

        var entry = zip.GetEntry(Normalize(full)) ?? zip.GetEntry(Normalize(href));
        if (entry is null) {
            return null;
        }

        using var content = entry.Open();
        using var buffer = new MemoryStream();
        content.CopyTo(buffer);

        return buffer.Length == 0 ? null : buffer.ToArray();
    }

    private static string? FindOpfPath(ZipArchive zip) {
        var container = LoadXml(zip.GetEntry("META-INF/container.xml"));
        string? declared = container?
            .Descendants().FirstOrDefault(e => e.Name.LocalName == "rootfile")?
            .Attributes().FirstOrDefault(a => a.Name.LocalName == "full-path")?.Value;

        if (!string.IsNullOrEmpty(declared)) {
            return declared;
        }

        // No container, or one that doesn't say — find the package document
        // by extension. Malformed EPUBs are common enough that this is worth
        // the one extra pass over the entry list.
        return zip.Entries
            .FirstOrDefault(e => e.FullName.EndsWith(".opf", StringComparison.OrdinalIgnoreCase))?
            .FullName;
    }

    private static string? FindCoverHref(XDocument opf) {
        var items = opf.Descendants()
            .Where(e => e.Name.LocalName == "item")
            .ToList();

        // EPUB 3.
        var cover = items.FirstOrDefault(e =>
            Attr(e, "properties")?.Contains("cover-image", StringComparison.OrdinalIgnoreCase) == true);

        // EPUB 2: <meta name="cover" content="<item id>">.
        if (cover is null) {
            var meta = opf.Descendants().FirstOrDefault(e =>
                e.Name.LocalName == "meta"
                && string.Equals(Attr(e, "name"), "cover", StringComparison.OrdinalIgnoreCase));
            string? coverId = meta is null ? null : Attr(meta, "content");
            if (coverId is not null) {
                cover = items.FirstOrDefault(e => Attr(e, "id") == coverId);
            }
        }

        // Neither marking: the image that calls itself a cover is the best
        // remaining guess.
        cover ??= items.FirstOrDefault(e =>
            Attr(e, "media-type")?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) == true
            && Attr(e, "href")?.Contains("cover", StringComparison.OrdinalIgnoreCase) == true);

        return cover is null ? null : Attr(cover, "href");
    }

    private static XDocument? LoadXml(ZipArchiveEntry? entry) {
        if (entry is null) {
            return null;
        }

        try {
            using var content = entry.Open();
            using var reader = XmlReader.Create(content, new XmlReaderSettings {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
            });

            return XDocument.Load(reader);
        } catch (Exception ex) when (ex is XmlException or IOException or InvalidDataException) {
            return null;
        }
    }

    private static string Normalize(string path) {
        return Uri.UnescapeDataString(path.Replace('\\', '/').TrimStart('/'));
    }

    private static string? Attr(XElement element, string name) {
        return element.Attributes().FirstOrDefault(a => a.Name.LocalName == name)?.Value;
    }
}
