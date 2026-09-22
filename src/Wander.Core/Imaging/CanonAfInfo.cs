namespace Wander.Core.Imaging;

/// <summary>
/// Canon's record of autofocus - maker note tag 0x0026, "AFInfo2" in
/// ExifTool's names - read into <see cref="AfPoint"/>s. MetadataExtractor
/// hands the record over as the array of 16-bit words it is and does not
/// take it apart; the layout is ExifTool's.
///
/// <para>
/// Words: 0 size in bytes, 1 area mode, 2 number of areas N, 3 how many
/// are valid, 4-5 image size, 6-7 the size of the frame the areas are
/// measured on, then four runs of N signed words - widths, heights, X and
/// Y of the centres from the middle of that frame - then the in-focus
/// bits, sixteen areas to a word. An EOS R8 writes N = 1053 and fills one
/// area; a frame focused by hand has none valid.
/// </para>
///
/// <para>
/// Y grows downwards, like the picture's rows: on the stand (2026-09-22,
/// two R8 frames) the other sign put the area on a featureless patch, and
/// this one on the edge the camera focused on.
/// </para>
/// </summary>
public static class CanonAfInfo {
    private const int Header = 8;


    /// <summary>The areas over the frame as the sensor stored it; empty when there are none or the record is not one.</summary>
    public static IReadOnlyList<AfPoint> Parse(IReadOnlyList<ushort> words) {
        if (words.Count < Header) {
            return Array.Empty<AfPoint>();
        }

        int n = words[2];
        int valid = words[3];
        int frameW = words[6];
        int frameH = words[7];
        int bits = Header + 4 * n;
        if (n == 0 || valid == 0 || frameW == 0 || frameH == 0 || words.Count < bits + (n + 15) / 16) {
            return Array.Empty<AfPoint>();
        }

        var points = new List<AfPoint>();
        for (int i = 0; i < n; i++) {
            int w = (short)words[Header + i];
            int h = (short)words[Header + n + i];
            if (w <= 0 || h <= 0) {
                continue;
            }

            int x = (short)words[Header + 2 * n + i];
            int y = (short)words[Header + 3 * n + i];
            bool inFocus = ((words[bits + i / 16] >> (i % 16)) & 1) != 0;
            points.Add(new AfPoint(
                0.5 + x / (double)frameW, 0.5 + y / (double)frameH,
                w / (double)frameW, h / (double)frameH, inFocus));
        }

        return points;
    }
}
