using System.IO.Compression;
using System.Text;
using System.Xml;
using Wander.Core.FileSystem;

namespace Wander.Core.Search;

/// <summary>
/// The office and book formats that are a zip of XML underneath:
/// <c>.docx</c>, <c>.xlsx</c>, <c>.pptx</c>, the OpenDocument three, and
/// <c>.epub</c>.
///
/// <para>
/// They get one extractor rather than six because the only thing that
/// differs is which entries in the zip hold the prose; once opened, all of
/// them answer the same way — every text node in the document is document
/// text. Nothing here knows what a <c>w:t</c> or an <c>a:t</c> is, and
/// that is deliberate: element-level knowledge would be six parsers to
/// keep, and it buys nothing a search needs.
/// </para>
///
/// <para>
/// This is the half of "search inside documents" that costs no
/// dependency. The other half — <c>.doc</c>, <c>.rtf</c>, <c>.pdf</c> — is
/// not a zip and not XML, and is answered by the system's own filters in
/// <c>Wander.Platform.Windows</c>.
/// </para>
/// </summary>
public sealed class ZipDocumentExtractor : IContentExtractor {
    /// <summary>
    /// Where the text stops being collected. A textbook as EPUB is a few
    /// megabytes of prose; a hundred megabytes of it is a generated
    /// spreadsheet, and searching the first eight million characters
    /// answers the question either way.
    /// </summary>
    private const int MaxChars = 8 * 1024 * 1024;

    private static readonly HashSet<string> _extensions = new(StringComparer.OrdinalIgnoreCase) {
        ".docx", ".xlsx", ".pptx",
        ".odt", ".ods", ".odp",
        ".epub",
    };

    private readonly IFileSystem _fs;


    public ZipDocumentExtractor(IFileSystem fs) {
        _fs = fs;
    }


    /// <summary>
    /// Expensive: inflating a document and running an XML reader over it is
    /// tens of milliseconds, and the same document is asked about again on
    /// every re-search of the same tree.
    /// </summary>
    public bool IsExpensive => true;


    public bool CanExtract(string path) {
        return _extensions.Contains(Path.GetExtension(path));
    }


    public string? Extract(string path, CancellationToken token) {
        byte[] bytes;
        try {
            bytes = _fs.ReadAllBytes(path);
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException) {
            return null;
        }

        try {
            using var stream = new MemoryStream(bytes, writable: false);
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read);

            var text = new StringBuilder();
            foreach (var entry in zip.Entries) {
                token.ThrowIfCancellationRequested();
                if (!CarriesText(entry.FullName)) {
                    continue;
                }
                ReadTextNodes(entry, text, token);
                if (text.Length >= MaxChars) {
                    break;
                }
            }

            return text.Length > 0 ? text.ToString() : null;
        } catch (Exception ex) when (ex is InvalidDataException or IOException or NotSupportedException) {
            return null;
        }
    }


    /// <summary>
    /// Which entries of the zip are prose rather than plumbing. Named
    /// rather than pattern-matched on extension because every one of these
    /// packages is full of <c>.xml</c> that is style tables, relationships
    /// and content types — searching those turns "find the word style" into
    /// a hit on every document in the folder.
    /// </summary>
    private static bool CarriesText(string entryPath) {
        // Word: the body, plus what the running head and footnotes hold.
        if (entryPath.StartsWith("word/", StringComparison.OrdinalIgnoreCase)) {
            string name = entryPath["word/".Length..];

            return name.StartsWith("document", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("header", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("footer", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("footnotes", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("endnotes", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("comments", StringComparison.OrdinalIgnoreCase);
        }

        // Excel: nearly every string in a workbook lives in the shared
        // table; the sheets themselves hold numbers and references to it.
        // The sheets are read anyway for the inline strings that formulas
        // and unshared cells leave behind.
        if (entryPath.StartsWith("xl/", StringComparison.OrdinalIgnoreCase)) {
            return entryPath.Equals("xl/sharedStrings.xml", StringComparison.OrdinalIgnoreCase)
                || entryPath.StartsWith("xl/worksheets/sheet", StringComparison.OrdinalIgnoreCase)
                || entryPath.StartsWith("xl/comments", StringComparison.OrdinalIgnoreCase);
        }

        // PowerPoint: the slides and what is said about them.
        if (entryPath.StartsWith("ppt/slides/slide", StringComparison.OrdinalIgnoreCase)
            || entryPath.StartsWith("ppt/notesSlides/", StringComparison.OrdinalIgnoreCase)) {
            return entryPath.EndsWith(".xml", StringComparison.OrdinalIgnoreCase);
        }

        // OpenDocument keeps the whole document in one part.
        if (entryPath.Equals("content.xml", StringComparison.OrdinalIgnoreCase)) {
            return true;
        }

        // EPUB: the chapters, wherever the publisher chose to put them.
        return entryPath.EndsWith(".xhtml", StringComparison.OrdinalIgnoreCase)
            || entryPath.EndsWith(".html", StringComparison.OrdinalIgnoreCase)
            || entryPath.EndsWith(".htm", StringComparison.OrdinalIgnoreCase);
    }


    /// <summary>
    /// Every text node in one part, separated by spaces. Spaces rather
    /// than nothing because a Word paragraph is split into runs at every
    /// formatting change: without a separator, a sentence with one bold
    /// word in it joins into a string no query would ever match.
    /// </summary>
    private static void ReadTextNodes(ZipArchiveEntry entry, StringBuilder text, CancellationToken token) {
        // Prohibit rather than ignore: these files come from wherever the
        // user got them, and an external entity in one of them is somebody
        // else's file read out over the network.
        var settings = new XmlReaderSettings {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
            CheckCharacters = false,
        };

        try {
            using var stream = entry.Open();
            using var reader = XmlReader.Create(stream, settings);

            while (reader.Read()) {
                token.ThrowIfCancellationRequested();
                if (reader.NodeType is not (XmlNodeType.Text or XmlNodeType.CDATA)) {
                    continue;
                }
                text.Append(reader.Value);
                text.Append(' ');
                if (text.Length >= MaxChars) {
                    return;
                }
            }
        } catch (Exception ex) when (ex is XmlException or InvalidDataException or IOException) {
            // A damaged part is not a damaged document: keep whatever the
            // other parts gave us.
        }
    }
}
