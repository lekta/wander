using System.Text;
using Wander.Core.Companions;

namespace Wander.Core.Tests;

public class XmpSidecarTests {
    // Attribute form — how Lightroom / Bridge write a sidecar.
    private const string Attributes =
        "<?xpacket begin='﻿' id='W5M0MpCehiHzreSzNTczkc9d'?>\n" +
        "<x:xmpmeta xmlns:x='adobe:ns:meta/' x:xmptk='Adobe XMP Core'>\n" +
        " <rdf:RDF xmlns:rdf='http://www.w3.org/1999/02/22-rdf-syntax-ns#'>\n" +
        "  <rdf:Description rdf:about=''\n" +
        "    xmlns:xmp='http://ns.adobe.com/xap/1.0/'\n" +
        "    xmlns:dc='http://purl.org/dc/elements/1.1/'\n" +
        "   xmp:CreatorTool=\"Wander\"\n" +
        "   xmp:Rating=\"2\"\n" +
        "   xmp:Label=\"Red\">\n" +
        "   <dc:subject><rdf:Bag><rdf:li>keyword</rdf:li></rdf:Bag></dc:subject>\n" +
        "  </rdf:Description>\n" +
        " </rdf:RDF>\n" +
        "</x:xmpmeta>\n" +
        "<?xpacket end='w'?>\n";

    // Element form — how darktable writes one.
    private const string Elements =
        "<x:xmpmeta xmlns:x='adobe:ns:meta/'>\n" +
        " <rdf:RDF xmlns:rdf='http://www.w3.org/1999/02/22-rdf-syntax-ns#'>\n" +
        "  <rdf:Description rdf:about='' xmlns:xmp='http://ns.adobe.com/xap/1.0/'>\n" +
        "   <xmp:Rating>4</xmp:Rating>\n" +
        "   <xmp:Label>Blue</xmp:Label>\n" +
        "  </rdf:Description>\n" +
        " </rdf:RDF>\n" +
        "</x:xmpmeta>\n";


    private static byte[] Utf8(string text) {
        return new UTF8Encoding(false).GetBytes(text);
    }

    private static string Text(byte[] bytes) {
        return new UTF8Encoding(false).GetString(bytes);
    }


    // --- Reading --------------------------------------------------------

    [Fact]
    public void Read_ReadsAttributeForm() {
        var rating = XmpSidecar.Read(Utf8(Attributes));

        Assert.Equal(2, rating.Rank);
        Assert.Equal(1, rating.ColorLabel);
        Assert.Equal("Red", rating.ColorLabelName);
    }

    [Fact]
    public void Read_ReadsElementForm() {
        var rating = XmpSidecar.Read(Utf8(Elements));

        Assert.Equal(4, rating.Rank);
        Assert.Equal(4, rating.ColorLabel);
    }

    [Fact]
    public void Read_KeepsALabelThatIsNotOneOfTheStandardFive() {
        // A custom label maps to no swatch, but it is still information the
        // footer should show rather than silently drop.
        string custom = Attributes.Replace("\"Red\"", "\"Client approved\"");

        var rating = XmpSidecar.Read(Utf8(custom));

        Assert.Equal(0, rating.ColorLabel);
        Assert.Equal("Client approved", rating.ColorLabelName);
    }

    [Fact]
    public void Read_IsNotFooledByAPropertyWhoseNameMerelyStartsTheSame() {
        string decoy = Attributes.Replace("xmp:Rating=\"2\"", "xmp:RatingPercent=\"88\"");

        Assert.Null(XmpSidecar.Read(Utf8(decoy)).Rank);
    }

    [Fact]
    public void Read_ReturnsNulls_ForAnEmptyPacket() {
        var rating = XmpSidecar.Read(Utf8("<x:xmpmeta/>"));

        Assert.Null(rating.Rank);
        Assert.Null(rating.ColorLabel);
    }


    // --- Writing --------------------------------------------------------

    [Fact]
    public void WithRating_ChangesOnlyThatAttribute() {
        string updated = Text(XmpSidecar.WithRating(Utf8(Attributes), 5));

        Assert.Equal(Attributes.Replace("xmp:Rating=\"2\"", "xmp:Rating=\"5\""), updated);
    }

    [Fact]
    public void WithRating_ChangesOnlyThatElement() {
        string updated = Text(XmpSidecar.WithRating(Utf8(Elements), 1));

        Assert.Equal(Elements.Replace("<xmp:Rating>4<", "<xmp:Rating>1<"), updated);
    }

    [Fact]
    public void WithColorLabel_WritesTheStandardName() {
        string updated = Text(XmpSidecar.WithColorLabel(Utf8(Attributes), 5));

        Assert.Equal(Attributes.Replace("xmp:Label=\"Red\"", "xmp:Label=\"Purple\""), updated);
    }

    [Fact]
    public void WithColorLabel_ClearsToAnEmptyLabel() {
        string updated = Text(XmpSidecar.WithColorLabel(Utf8(Attributes), 0));

        Assert.Equal(Attributes.Replace("xmp:Label=\"Red\"", "xmp:Label=\"\""), updated);
    }

    [Fact]
    public void WithRating_AddsTheAttribute_WhenThePacketHasNone() {
        string without = Attributes.Replace("   xmp:Rating=\"2\"\n", "");

        string updated = Text(XmpSidecar.WithRating(Utf8(without), 3));

        Assert.Contains("xmp:Rating=\"3\"", updated);
        Assert.Contains("xmp:Label=\"Red\"", updated);
        Assert.Contains("<rdf:li>keyword</rdf:li>", updated);
        Assert.Equal(3, XmpSidecar.Read(Utf8(updated)).Rank);
    }

    [Fact]
    public void WithRating_AddsTheAttribute_ToASelfClosingDescription() {
        string packet =
            "<rdf:RDF xmlns:rdf='x'>\n" +
            "  <rdf:Description rdf:about='' xmlns:xmp='http://ns.adobe.com/xap/1.0/'/>\n" +
            "</rdf:RDF>\n";

        string updated = Text(XmpSidecar.WithRating(Utf8(packet), 2));

        Assert.Contains("xmp:Rating=\"2\"/>", updated);
        Assert.Equal(2, XmpSidecar.Read(Utf8(updated)).Rank);
    }

    [Fact]
    public void WithRating_RefusesAPacketThatDoesNotDeclareTheXmpNamespace() {
        // Adding the namespace ourselves means rewriting somebody else's
        // declarations, and a packet whose namespaces we got subtly wrong is
        // worse than one we refused to touch.
        string packet = "<rdf:RDF><rdf:Description rdf:about=''/></rdf:RDF>";

        Assert.Throws<NotSupportedException>(() => XmpSidecar.WithRating(Utf8(packet), 3));
    }

    [Fact]
    public void WithRating_RefusesSomethingThatIsNotAnXmpPacket() {
        Assert.Throws<NotSupportedException>(() => XmpSidecar.WithRating(Utf8("hello"), 3));
    }

    [Fact]
    public void WithRating_PreservesBomAndCrLf() {
        string crlf = Attributes.Replace("\n", "\r\n");
        byte[] withBom = new UTF8Encoding(true).GetPreamble().Concat(Utf8(crlf)).ToArray();

        byte[] updated = XmpSidecar.WithRating(withBom, 4);

        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, updated.Take(3));
        Assert.Equal(crlf.Replace("xmp:Rating=\"2\"", "xmp:Rating=\"4\""), Text(updated.Skip(3).ToArray()));
    }

    [Fact]
    public void WithRating_RejectsValuesOutsideTheScale() {
        Assert.Throws<ArgumentOutOfRangeException>(() => XmpSidecar.WithRating(Utf8(Attributes), 6));
        Assert.Throws<ArgumentOutOfRangeException>(() => XmpSidecar.WithRating(Utf8(Attributes), -1));
    }
}
