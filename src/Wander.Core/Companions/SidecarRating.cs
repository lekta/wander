namespace Wander.Core.Companions;

/// <summary>
/// How a photo is marked up, whichever sidecar carries it. RawTherapee
/// stores this in a <c>.pp3</c>, Adobe / darktable / exiftool in an
/// <c>.xmp</c>; the fields are the same two, so the UI above only has to
/// know this shape.
/// </summary>
/// <param name="Rank">Stars, 0…5. Null when the file doesn't say.</param>
/// <param name="ColorLabel">Colour index, 0 (none) … 5. Null when the file doesn't say.</param>
/// <param name="ColorLabelName">
/// The label as the file spells it. Equals the standard name for a known
/// index; keeps whatever custom text an XMP carried when the index is 0 but
/// the label isn't empty — a "Client approved" label is information, and
/// showing nothing would be a lie.
/// </param>
public sealed record SidecarRating(int? Rank, int? ColorLabel, string? ColorLabelName = null);


/// <summary>
/// The five colour labels, by index. Both formats agree on the numbering
/// (XMP spells them out, <c>.pp3</c> stores the number), which is what lets
/// one row of swatches drive either file.
///
/// <para>
/// The names are Adobe's standard set, and that is also the order
/// RawTherapee's browser shows. If a build of RawTherapee ever disagrees,
/// this table is the single line to change — nothing else hard-codes a
/// colour meaning.
/// </para>
/// </summary>
public static class ColorLabels {
    public const int Max = 5;

    private static readonly string[] _names = { "None", "Red", "Yellow", "Green", "Blue", "Purple" };


    public static string Name(int index) {
        return index >= 0 && index <= Max ? _names[index] : "None";
    }


    /// <summary>Index for a label name, or 0 when it's empty or something we don't recognise.</summary>
    public static int IndexOf(string? name) {
        if (string.IsNullOrWhiteSpace(name)) {
            return 0;
        }
        for (int i = 1; i <= Max; i++) {
            if (string.Equals(_names[i], name.Trim(), StringComparison.OrdinalIgnoreCase)) {
                return i;
            }
        }

        return 0;
    }
}
