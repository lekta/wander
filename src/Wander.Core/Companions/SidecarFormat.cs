namespace Wander.Core.Companions;

/// <summary>
/// Which sidecar Wander writes when a photo has none yet and the user rates
/// it. The choice is a real one, not a preference about file extensions —
/// see <see cref="Suffix"/> and the note on <c>Pp3</c>.
/// </summary>
public enum SidecarFormat {
    /// <summary>
    /// <c>xmp:Rating</c> / <c>xmp:Label</c> in an <c>.xmp</c> next to the
    /// photo. The default, and the safe one: Adobe, darktable and exiftool
    /// all read it, RawTherapee has read ratings from XMP since 5.7 and
    /// synchronises them since 5.11 — and, crucially, an XMP sidecar says
    /// nothing about how the raw should be developed.
    /// </summary>
    Xmp,

    /// <summary>
    /// <c>[General] Rank</c> / <c>ColorLabel</c> in a RawTherapee
    /// <c>.pp3</c>.
    ///
    /// <para>
    /// <b>This one is not free.</b> RawTherapee applies its default
    /// processing profile (Auto-Matched Curve, or whatever the user set)
    /// only to photos that have <em>no</em> sidecar; the moment a
    /// <c>.pp3</c> exists it is loaded instead, and keys it doesn't mention
    /// come from hard-coded neutral defaults rather than from that profile.
    /// So creating a <c>.pp3</c> just to hold a star changes how the photo
    /// opens: flat instead of auto-matched. That is why the format is a
    /// setting with the other value as its default, and why creation asks
    /// first.
    /// </para>
    /// </summary>
    Pp3,
}


public static class SidecarFormats {
    /// <summary>File extension, including the dot.</summary>
    public static string Suffix(this SidecarFormat format) {
        return format == SidecarFormat.Pp3 ? ".pp3" : ".xmp";
    }
}
