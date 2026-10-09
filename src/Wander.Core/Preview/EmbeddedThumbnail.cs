using System.IO.Compression;
using System.Xml;
using Wander.Core.FileSystem;

namespace Wander.Core.Preview;

/// <summary>
/// The picture a zip-based file keeps of itself, for its tile: what the
/// program that saved it drew when it saved it. Every format here is a
/// zip, so the picture is one entry away and nothing is rendered:
///
/// <list type="bullet">
///   <item>OpenDocument - <c>Thumbnails/thumbnail.png</c>, always written;</item>
///   <item>Office Open XML and 3MF - the package's thumbnail relationship
///     (<c>docProps/thumbnail.jpeg</c> in Office, the plate in a slicer),
///     written when the program was asked to;</item>
///   <item>Krita and OpenRaster - their preview, else the flattened image;</item>
///   <item>a comic book zip - its first page by name.</item>
/// </list>
///
/// <para>
/// Null when the file has none, and the caller draws what it drew before.
/// </para>
/// </summary>
public static class EmbeddedThumbnail {
    /// <summary>A picture entry past this is not a thumbnail.</summary>
    private const long MaxPictureBytes = 32L * 1024 * 1024;

    private const string ThumbnailRelationship = "/metadata/thumbnail";

    private static readonly HashSet<string> _openDocument = new(StringComparer.OrdinalIgnoreCase) {
        ".odt", ".ods", ".odp", ".odg",
    };

    private static readonly HashSet<string> _package = new(StringComparer.OrdinalIgnoreCase) {
        ".docx", ".xlsx", ".pptx", ".docm", ".xlsm", ".pptm", ".3mf",
    };

    private static readonly string[] _pages = { ".jpg", ".jpeg", ".png", ".gif", ".bmp" };

    private static readonly XmlReaderSettings _xml = new() {
        DtdProcessing = DtdProcessing.Ignore,
        XmlResolver = null,
        CloseInput = true,
    };


    /// <summary>Extensions whose files can carry a picture of themselves.</summary>
    public static readonly IReadOnlySet<string> Extensions = new HashSet<string>(
        _openDocument.Concat(_package).Concat(new[] { ".kra", ".ora", ".cbz" }),
        StringComparer.OrdinalIgnoreCase);


    public static bool Supports(string path) {
        return Extensions.Contains(Path.GetExtension(path));
    }


    /// <summary>The picture's bytes - PNG or JPEG mostly, a metafile from old Word - or null.</summary>
    public static byte[]? TryRead(string path) {
        if (!Supports(path)) {
            return null;
        }

        try {
            using var stream = SharedRead.Open(path);

            return Read(stream, Path.GetExtension(path));
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
            return null;
        }
    }


    /// <summary>Same, from an open stream - for the tests.</summary>
    public static byte[]? Read(Stream stream, string extension) {
        try {
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
            string ext = extension.ToLowerInvariant();
            ZipArchiveEntry? entry = ext switch {
                _ when _openDocument.Contains(ext) => zip.GetEntry("Thumbnails/thumbnail.png"),
                _ when _package.Contains(ext) => Related(zip) ?? zip.GetEntry("docProps/thumbnail.jpeg"),
                ".kra" => zip.GetEntry("preview.png") ?? zip.GetEntry("mergedimage.png"),
                ".ora" => zip.GetEntry("Thumbnails/thumbnail.png") ?? zip.GetEntry("mergedimage.png"),
                ".cbz" => FirstPage(zip),
                _ => null,
            };
            if (entry is null || entry.Length == 0 || entry.Length > MaxPictureBytes) {
                return null;
            }

            using var input = entry.Open();
            var bytes = new byte[entry.Length];
            input.ReadExactly(bytes);

            return bytes;
        } catch (Exception ex) when (ex is InvalidDataException or IOException or XmlException or NotSupportedException) {
            return null;
        }
    }


    /// <summary>The entry the package's root relationships name as its thumbnail.</summary>
    private static ZipArchiveEntry? Related(ZipArchive zip) {
        if (zip.GetEntry("_rels/.rels") is not { } rels) {
            return null;
        }

        using var reader = XmlReader.Create(rels.Open(), _xml);
        while (reader.Read()) {
            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "Relationship"
                && reader.GetAttribute("Type") is { } type && type.EndsWith(ThumbnailRelationship, StringComparison.Ordinal)
                && reader.GetAttribute("Target") is { } target) {
                return zip.GetEntry(target.TrimStart('/'));
            }
        }

        return null;
    }


    /// <summary>A comic's cover: the first picture by name, outside the macOS resource folder.</summary>
    private static ZipArchiveEntry? FirstPage(ZipArchive zip) {
        return zip.Entries
            .Where(e => !e.FullName.StartsWith("__MACOSX/", StringComparison.OrdinalIgnoreCase)
                && _pages.Contains(Path.GetExtension(e.FullName), StringComparer.OrdinalIgnoreCase))
            .OrderBy(e => e.FullName, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }
}
