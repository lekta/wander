using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Wander.Core.Preview;

/// <summary>What a FictionBook file has to show: a header block and the text.</summary>
/// <param name="Title">Book title, empty when the file doesn't name one.</param>
/// <param name="Author">Authors, comma-separated. Empty when absent.</param>
/// <param name="BodyHtml">The whole preview as an HTML fragment, header included.</param>
/// <param name="Truncated">The text was cut at the character budget.</param>
public sealed record Fb2Preview(string Title, string Author, string BodyHtml, bool Truncated);


/// <summary>
/// FictionBook (<c>.fb2</c>) — an XML book format, which means the preview
/// can be built from the file itself instead of shelling out to a reader.
/// The document is turned into HTML for the same WebView2 the PDF and
/// Markdown previews use.
///
/// <para>
/// Namespaces are matched by local name throughout. FB2 in the wild is
/// produced by a dozen converters, several of which get the namespace URI
/// wrong or drop it entirely; refusing those files over a namespace would
/// be pedantry the reader pays for.
/// </para>
///
/// <para>
/// Lives in Core with no UI or platform types in sight: an FB2 file is the
/// same document whoever is drawing it, and this way the parsing is
/// testable without a window.
/// </para>
/// </summary>
public static class Fb2Document {
    /// <summary>
    /// Characters of generated HTML to stop at. A novel is a few megabytes
    /// of markup; the pane is a preview, and past this point nobody is
    /// reading, they are scrolling.
    /// </summary>
    public const int DefaultHtmlBudget = 400_000;

    /// <summary>
    /// Total bytes of embedded images to inline. Illustrated books can
    /// carry hundreds; past the budget the remaining ones are dropped
    /// rather than turning the preview into a memory event.
    /// </summary>
    private const int ImageBudget = 6 * 1024 * 1024;


    /// <summary>
    /// Reads a book. Returns null when the stream isn't well-formed XML or
    /// carries no FictionBook body — the caller then falls back to whatever
    /// it shows for a file it cannot preview.
    /// </summary>
    public static Fb2Preview? Read(Stream stream, int htmlBudget = DefaultHtmlBudget) {
        XDocument doc;
        try {
            using var reader = XmlReader.Create(stream, SafeSettings());
            doc = XDocument.Load(reader);
        } catch (Exception ex) when (ex is XmlException or IOException) {
            return null;
        }

        var root = doc.Root;
        if (root is null || !Is(root, "FictionBook")) {
            return null;
        }

        var binaries = CollectBinaries(root);
        var titleInfo = Descendant(root, "description")?.Elements().FirstOrDefault(e => Is(e, "title-info"));

        string title = Text(Child(titleInfo, "book-title"));
        string author = string.Join(", ", (titleInfo?.Elements() ?? Enumerable.Empty<XElement>())
            .Where(e => Is(e, "author"))
            .Select(FormatAuthor)
            .Where(s => s.Length > 0));

        var html = new StringBuilder();
        WriteHeader(html, title, author, titleInfo, binaries);

        // Only the main body. FB2 puts footnotes in a second <body
        // name="notes">, and a preview that opens on the footnotes would
        // be answering a question nobody asked.
        var body = root.Elements().FirstOrDefault(e => Is(e, "body") && Attr(e, "name") is null)
            ?? root.Elements().FirstOrDefault(e => Is(e, "body"));

        if (body is not null) {
            WriteChildren(html, body, binaries, htmlBudget);
        }

        // The budget is checked as the tree is walked, not between
        // top-level children: plenty of books are a single <section>, and a
        // limit that only applies between sections would never fire on one.
        return new Fb2Preview(title, author, html.ToString(), html.Length >= htmlBudget);
    }


    /// <summary>
    /// The cover picture alone, as the bytes the file stores it in — what a
    /// thumbnail needs and the only part of a book it needs. Reads forward
    /// once and skips the text wholesale, so a shelf of novels costs a scan
    /// of their XML skeletons rather than a parse of their contents.
    /// </summary>
    public static byte[]? ReadCover(Stream stream) {
        try {
            using var reader = XmlReader.Create(stream, SafeSettings());
            string? coverId = null;

            // Advancing is explicit rather than a `while (Read())` header,
            // because Skip already leaves the reader on the next node —
            // reading again on top of it would step over the element after
            // the skipped one, which is how the cover binary sitting right
            // behind another one gets missed.
            while (!reader.EOF) {
                if (reader.NodeType != XmlNodeType.Element) {
                    reader.Read();

                    continue;
                }

                switch (reader.LocalName) {
                    case "coverpage":
                        coverId ??= ReadCoverHref(reader);
                        reader.Read();
                        break;

                    // The text is the bulk of the file and holds nothing a
                    // cover needs; skipping it whole is what keeps this
                    // cheap enough to run per file in a listing.
                    case "body":
                        reader.Skip();
                        break;

                    case "binary":
                        // Binaries sit at the end of the file, after the
                        // cover reference — so by the time one shows up we
                        // already know which id to want. A file that has no
                        // <coverpage> still gets a cover if it carries an
                        // image: the first one is the frontispiece often
                        // enough to be worth showing.
                        string? id = reader.GetAttribute("id");
                        if (coverId is null || string.Equals(id, coverId, StringComparison.OrdinalIgnoreCase)) {
                            return DecodeBinary(reader);
                        }
                        reader.Skip();
                        break;

                    default:
                        reader.Read();
                        break;
                }
            }
        } catch (Exception ex) when (ex is XmlException or IOException or FormatException) {
            return null;
        }

        return null;
    }


    // --- Reading -------------------------------------------------------

    private static XmlReaderSettings SafeSettings() {
        // DTDs in a file the user merely clicked on are an attack surface
        // (entity expansion, external references) and buy an FB2 preview
        // nothing.
        return new XmlReaderSettings {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
        };
    }

    private static string? ReadCoverHref(XmlReader reader) {
        using var sub = reader.ReadSubtree();
        while (sub.Read()) {
            if (sub.NodeType == XmlNodeType.Element && sub.LocalName == "image") {
                return TrimHref(HrefOf(sub));
            }
        }

        return null;
    }

    private static string? HrefOf(XmlReader reader) {
        if (!reader.HasAttributes) {
            return null;
        }

        // The attribute is xlink:href, and the prefix is whatever the
        // producing tool felt like declaring — match on the local name.
        for (int i = 0; i < reader.AttributeCount; i++) {
            reader.MoveToAttribute(i);
            if (reader.LocalName == "href") {
                string value = reader.Value;
                reader.MoveToElement();

                return value;
            }
        }
        reader.MoveToElement();

        return null;
    }

    private static byte[]? DecodeBinary(XmlReader reader) {
        try {
            string base64 = reader.ReadElementContentAsString();

            return base64.Length == 0 ? null : Convert.FromBase64String(base64.Trim());
        } catch (Exception ex) when (ex is FormatException or XmlException) {
            return null;
        }
    }

    /// <summary>Every <c>&lt;binary&gt;</c> in the file, by id, as a data URI.</summary>
    private static Dictionary<string, string> CollectBinaries(XElement root) {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        int budget = ImageBudget;

        foreach (var binary in root.Elements().Where(e => Is(e, "binary"))) {
            string? id = Attr(binary, "id");
            if (id is null) {
                continue;
            }

            string payload = binary.Value.Trim();
            // Base64 is four characters per three bytes — close enough to
            // charge the budget without decoding first.
            int cost = payload.Length / 4 * 3;
            if (cost > budget) {
                break;
            }
            budget -= cost;

            string type = Attr(binary, "content-type") ?? "image/jpeg";
            map[id] = $"data:{type};base64,{payload}";
        }

        return map;
    }


    // --- Rendering -----------------------------------------------------

    private static void WriteHeader(
        StringBuilder html, string title, string author,
        XElement? titleInfo, Dictionary<string, string> binaries) {

        html.Append("<div class='fb2-head'>");

        string? cover = CoverUri(titleInfo, binaries);
        if (cover is not null) {
            html.Append($"<img class='fb2-cover' src='{cover}' alt=''>");
        }
        if (title.Length > 0) {
            html.Append($"<h1>{Escape(title)}</h1>");
        }
        if (author.Length > 0) {
            html.Append($"<p class='fb2-author'>{Escape(author)}</p>");
        }

        var annotation = Child(titleInfo, "annotation");
        if (annotation is not null) {
            html.Append("<div class='fb2-annotation'>");
            WriteChildren(html, annotation, binaries, int.MaxValue);
            html.Append("</div>");
        }

        html.Append("</div>");
    }

    private static string? CoverUri(XElement? titleInfo, Dictionary<string, string> binaries) {
        var image = Child(titleInfo, "coverpage")?.Elements().FirstOrDefault(e => Is(e, "image"));
        string? href = TrimHref(image is null ? null : Attr(image, "href"));

        return href is not null && binaries.TryGetValue(href, out string? uri) ? uri : null;
    }

    /// <summary>
    /// One FB2 element as HTML. Anything unrecognised has its children
    /// written out anyway: an unknown wrapper should cost its own tag, not
    /// the text inside it.
    /// </summary>
    private static void WriteNode(
        StringBuilder html, XNode node, Dictionary<string, string> binaries, int budget) {

        if (node is XText text) {
            html.Append(Escape(text.Value));

            return;
        }

        if (node is not XElement element) {
            return;
        }

        switch (element.Name.LocalName) {
            case "empty-line":
                html.Append("<div class='fb2-empty'></div>");

                return;

            case "image":
                string? href = TrimHref(Attr(element, "href"));
                if (href is not null && binaries.TryGetValue(href, out string? uri)) {
                    html.Append($"<img class='fb2-image' src='{uri}' alt=''>");
                }

                return;

            case "binary":
                // Already collected; the payload must not reach the page as text.
                return;
        }

        string? tag = element.Name.LocalName switch {
            "section" => "div class='fb2-section'",
            "title" => "div class='fb2-title'",
            "subtitle" => "h3",
            "p" => "p",
            "epigraph" or "cite" => "blockquote",
            "text-author" => "p class='fb2-text-author'",
            "poem" => "div class='fb2-poem'",
            "stanza" => "div class='fb2-stanza'",
            "v" => "div",
            "strong" => "strong",
            "emphasis" => "em",
            "strikethrough" => "s",
            "sub" => "sub",
            "sup" => "sup",
            "code" => "code",
            // Links are dropped to plain text on purpose: the preview must
            // not offer navigation it cannot follow.
            "style" or "a" => "span",
            "table" => "table",
            "tr" => "tr",
            "td" => "td",
            "th" => "th",
            _ => null,
        };

        if (tag is null) {
            WriteChildren(html, element, binaries, budget);

            return;
        }

        string close = tag.Split(' ')[0];
        html.Append('<').Append(tag).Append('>');
        WriteChildren(html, element, binaries, budget);
        html.Append("</").Append(close).Append('>');
    }

    /// <summary>
    /// Writes an element's children, stopping once the budget is spent.
    /// Only the descent stops: the tags already opened are still closed on
    /// the way out, so what comes back is a whole fragment rather than one
    /// cut mid-tag.
    /// </summary>
    private static void WriteChildren(
        StringBuilder html, XElement element, Dictionary<string, string> binaries, int budget) {

        foreach (var child in element.Nodes()) {
            if (html.Length >= budget) {
                return;
            }
            WriteNode(html, child, binaries, budget);
        }
    }

    private static string FormatAuthor(XElement author) {
        var parts = new[] { "first-name", "middle-name", "last-name", "nickname" }
            .Select(n => Text(Child(author, n)))
            .Where(s => s.Length > 0);

        return string.Join(" ", parts);
    }


    // --- Small helpers -------------------------------------------------

    private static bool Is(XElement element, string name) {
        return element.Name.LocalName == name;
    }

    private static XElement? Child(XElement? parent, string name) {
        return parent?.Elements().FirstOrDefault(e => Is(e, name));
    }

    private static XElement? Descendant(XElement root, string name) {
        return root.Elements().FirstOrDefault(e => Is(e, name));
    }

    private static string? Attr(XElement element, string name) {
        return element.Attributes().FirstOrDefault(a => a.Name.LocalName == name)?.Value;
    }

    private static string Text(XElement? element) {
        return element?.Value.Trim() ?? "";
    }

    /// <summary>An FB2 image reference is <c>#id</c>; the binary is filed under the bare id.</summary>
    private static string? TrimHref(string? href) {
        if (string.IsNullOrEmpty(href)) {
            return null;
        }

        return href.StartsWith('#') ? href[1..] : href;
    }

    private static string Escape(string value) {
        return value
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;");
    }
}
