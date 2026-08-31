using System.Text;

namespace Wander.Core.Companions;

/// <summary>
/// Reads and edits <c>xmp:Rating</c> / <c>xmp:Label</c> in an XMP sidecar —
/// the format Adobe Bridge and Lightroom, darktable and exiftool all agree
/// on, and by far the widest-reaching place a photo's rating lives.
///
/// <para>
/// <b>Why this is string surgery and not an XML parser.</b> Round-tripping
/// through <c>XDocument</c> would reформat the packet: attribute order,
/// namespace prefixes, whitespace, the <c>&lt;?xpacket?&gt;</c> trailer with
/// its padding — all of it would come back different. An XMP sidecar is a
/// file other programs both read and write, and handing them back a
/// rewritten packet is asking for trouble that we would never see and the
/// user always would. So: find the one property, change its value, leave
/// every other byte where it was.
/// </para>
///
/// <para>
/// XMP allows a property to be written either as an attribute of
/// <c>rdf:Description</c> or as a child element, and real files in the wild
/// use both — Lightroom writes attributes, darktable writes elements. Both
/// are handled; whichever form the file already uses is the form it keeps.
/// </para>
/// </summary>
internal static class XmpSidecar {
    private const string RatingProperty = "xmp:Rating";
    private const string LabelProperty = "xmp:Label";
    private const string DescriptionElement = "<rdf:Description";
    private const string XmpNamespace = "xmlns:xmp=";

    /// <summary>Star range XMP defines. (It also allows -1 for "rejected"; Wander doesn't set that.)</summary>
    public const int MaxRating = 5;

    /// <summary>
    /// The byte-order mark the <c>&lt;?xpacket?&gt;</c> header carries in its
    /// <c>begin</c> attribute. It is there so a reader scanning a binary file
    /// for an embedded packet can tell the encoding from the first bytes; in
    /// a standalone sidecar it is pure convention, and the convention is what
    /// other tools look for.
    /// </summary>
    private const string PacketBom = "\ufeff";


    /// <summary>
    /// A brand-new sidecar packet carrying nothing but the two rating
    /// properties. Deliberately minimal: every property written here is one
    /// more thing another program has to agree with us about, and the point
    /// of the file is the star.
    ///
    /// <para>
    /// This is the format Wander creates by default (see
    /// <see cref="SidecarFormat"/>). Unlike a <c>.pp3</c>, an XMP sidecar
    /// makes no claim about how a raw file should be developed — it is
    /// metadata beside the photo, so bringing one into existence cannot
    /// change how the photo looks in anybody's editor.
    /// </para>
    ///
    /// <para>
    /// <c>xmp:Label</c> is written even when the colour is "none", as an
    /// empty value: that is how Adobe spells no-colour, and having both
    /// properties present means every later edit takes
    /// <see cref="SetProperty"/>'s in-place path instead of its insert path.
    /// </para>
    /// </summary>
    public static byte[] Create(int rating, int colorLabel) {
        if (rating < 0 || rating > MaxRating) {
            throw new ArgumentOutOfRangeException(nameof(rating), rating, $"Rating must be 0..{MaxRating}.");
        }
        if (colorLabel < 0 || colorLabel > ColorLabels.Max) {
            throw new ArgumentOutOfRangeException(nameof(colorLabel), colorLabel, $"Label must be 0..{ColorLabels.Max}.");
        }

        string label = colorLabel == 0 ? "" : ColorLabels.Name(colorLabel);
        string text = $"""
            <?xpacket begin="{PacketBom}" id="W5M0MpCehiHzreSzNTczkc9d"?>
            <x:xmpmeta xmlns:x="adobe:ns:meta/" x:xmptk="Wander">
             <rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#">
              <rdf:Description rdf:about=""
                xmlns:xmp="http://ns.adobe.com/xap/1.0/"
                xmp:Rating="{rating}"
                xmp:Label="{label}"/>
             </rdf:RDF>
            </x:xmpmeta>
            <?xpacket end="w"?>

            """;

        return SidecarText.Encode(text, new UTF8Encoding(false), hasBom: false);
    }


    public static SidecarRating Read(byte[] content) {
        string text = SidecarText.Decode(content, out _, out _);
        string? rating = FindValue(text, RatingProperty);
        string? label = FindValue(text, LabelProperty);

        int? rank = rating is null ? null : ParseRating(rating);
        int index = ColorLabels.IndexOf(label);

        return new SidecarRating(
            rank,
            label is null ? null : index,
            string.IsNullOrWhiteSpace(label) ? null : label.Trim());
    }


    /// <summary>
    /// The same packet with <c>xmp:Rating</c> set. Throws
    /// <see cref="NotSupportedException"/> when the property isn't there and
    /// there is no safe place to put it — see <see cref="SetProperty"/>.
    /// </summary>
    public static byte[] WithRating(byte[] content, int rating) {
        if (rating < 0 || rating > MaxRating) {
            throw new ArgumentOutOfRangeException(nameof(rating), rating, $"Rating must be 0..{MaxRating}.");
        }

        return SidecarText.Edit(content, text => SetProperty(text, RatingProperty, rating.ToString()));
    }


    /// <summary>
    /// The same packet with <c>xmp:Label</c> set to the standard name for
    /// <paramref name="label"/>; index 0 clears it to an empty label, which
    /// is how Adobe spells "no colour".
    /// </summary>
    public static byte[] WithColorLabel(byte[] content, int label) {
        if (label < 0 || label > ColorLabels.Max) {
            throw new ArgumentOutOfRangeException(nameof(label), label, $"Label must be 0..{ColorLabels.Max}.");
        }
        string name = label == 0 ? "" : ColorLabels.Name(label);

        return SidecarText.Edit(content, text => SetProperty(text, LabelProperty, name));
    }


    // --- Finding ---------------------------------------------------------

    /// <summary>
    /// Value of a property in whichever form the file uses, or null when the
    /// property isn't present at all (which is different from present-but-empty).
    /// </summary>
    private static string? FindValue(string text, string property) {
        int attr = FindAttribute(text, property, out int valueStart, out int valueEnd);
        if (attr >= 0) {
            return text[valueStart..valueEnd];
        }

        int element = FindElement(text, property, out valueStart, out valueEnd);

        return element >= 0 ? text[valueStart..valueEnd] : null;
    }

    /// <summary>
    /// Locates <c>property="value"</c>. Returns the index of the property
    /// name, with the value's bounds in the out parameters, or -1.
    /// </summary>
    private static int FindAttribute(string text, string property, out int valueStart, out int valueEnd) {
        valueStart = valueEnd = -1;

        int from = 0;
        while (true) {
            int at = text.IndexOf(property, from, StringComparison.Ordinal);
            if (at < 0) {
                return -1;
            }
            from = at + property.Length;

            // Must be a whole attribute name (not the tail of xmp:RatingFoo)
            // preceded by whitespace, and followed by = and a quote.
            if (at > 0 && !char.IsWhiteSpace(text[at - 1])) {
                continue;
            }
            int eq = SkipSpace(text, from);
            if (eq >= text.Length || text[eq] != '=') {
                continue;
            }
            int quote = SkipSpace(text, eq + 1);
            if (quote >= text.Length || (text[quote] != '"' && text[quote] != '\'')) {
                continue;
            }
            int close = text.IndexOf(text[quote], quote + 1);
            if (close < 0) {
                continue;
            }

            valueStart = quote + 1;
            valueEnd = close;

            return at;
        }
    }

    /// <summary>Locates <c>&lt;property&gt;value&lt;/property&gt;</c>. Same contract as <see cref="FindAttribute"/>.</summary>
    private static int FindElement(string text, string property, out int valueStart, out int valueEnd) {
        valueStart = valueEnd = -1;

        string open = "<" + property;
        int at = text.IndexOf(open, StringComparison.Ordinal);
        if (at < 0) {
            return -1;
        }
        int tagEnd = text.IndexOf('>', at);
        if (tagEnd < 0 || text[tagEnd - 1] == '/') {
            return -1;
        }
        int close = text.IndexOf("</" + property, tagEnd, StringComparison.Ordinal);
        if (close < 0) {
            return -1;
        }

        valueStart = tagEnd + 1;
        valueEnd = close;

        return at;
    }

    private static int SkipSpace(string text, int i) {
        while (i < text.Length && char.IsWhiteSpace(text[i])) {
            i++;
        }

        return i;
    }


    // --- Writing ----------------------------------------------------------

    /// <summary>
    /// Replaces the property's value in place, or adds it as an attribute of
    /// the first <c>rdf:Description</c>.
    ///
    /// <para>
    /// The insert only happens when the packet already declares the
    /// <c>xmp:</c> namespace prefix. Declaring it ourselves would mean
    /// editing the element's namespace list, and an XMP packet whose
    /// namespaces we got subtly wrong is worse than one we refused to touch
    /// — hence the explicit refusal instead of a guess.
    /// </para>
    /// </summary>
    private static string SetProperty(string text, string property, string value) {
        string escaped = Escape(value);

        if (FindAttribute(text, property, out int start, out int end) >= 0) {
            return text[..start] + escaped + text[end..];
        }
        if (FindElement(text, property, out start, out end) >= 0) {
            return text[..start] + escaped + text[end..];
        }

        int description = text.IndexOf(DescriptionElement, StringComparison.Ordinal);
        if (description < 0) {
            throw new NotSupportedException("This XMP file has no rdf:Description to write into.");
        }
        int tagEnd = text.IndexOf('>', description);
        if (tagEnd < 0) {
            throw new NotSupportedException("This XMP file's rdf:Description is malformed.");
        }
        if (text.IndexOf(XmpNamespace, description, tagEnd - description, StringComparison.Ordinal) < 0) {
            throw new NotSupportedException("This XMP file does not declare the xmp: namespace.");
        }

        // Self-closing elements keep their slash after the inserted attribute.
        int insertAt = text[tagEnd - 1] == '/' ? tagEnd - 1 : tagEnd;

        return text[..insertAt] + $" {property}=\"{escaped}\"" + text[insertAt..];
    }

    private static string Escape(string value) {
        return value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal);
    }

    private static int? ParseRating(string value) {
        return int.TryParse(value.Trim(), out int parsed) ? Math.Clamp(parsed, 0, MaxRating) : null;
    }
}
