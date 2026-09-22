namespace Wander.Core.Imaging;

/// <summary>
/// How big a picture is decoded for the pane (PLAN AK, step 3). A JPEG asked
/// for fewer pixels scales in the DCT - 1/2 to 1/8 of the work and none of
/// the hundred megabytes a 24-megapixel bitmap takes - so the pane gets the
/// frame at the size it is drawn at, and the full one is decoded later and
/// only for the 1:1 zoom.
/// </summary>
public static class PictureFit {
    /// <summary>
    /// Within this share of the box the picture is decoded whole: a 5 %
    /// smaller copy saves nothing and costs the zoom a second decode.
    /// </summary>
    private const double WholeAbove = 0.95;


    /// <summary>
    /// The width to decode the stored frame at, or null to decode it whole
    /// (it fits the box already, or the box is not known yet).
    /// </summary>
    /// <param name="storedWidth">Pixels as the file stores them, before the EXIF turn.</param>
    /// <param name="storedHeight">Same.</param>
    /// <param name="orientation">EXIF orientation, 1..8; 5-8 stand the frame on its side.</param>
    /// <param name="boxWidth">Device pixels the pane has for the picture.</param>
    /// <param name="boxHeight">Same.</param>
    public static int? DecodeWidth(int storedWidth, int storedHeight, int? orientation, double boxWidth, double boxHeight) {
        if (storedWidth <= 0 || storedHeight <= 0 || boxWidth < 1 || boxHeight < 1) {
            return null;
        }

        bool turned = orientation is >= 5 and <= 8;
        double shownWidth = turned ? storedHeight : storedWidth;
        double shownHeight = turned ? storedWidth : storedHeight;
        double scale = Math.Min(boxWidth / shownWidth, boxHeight / shownHeight);
        if (scale >= WholeAbove) {
            return null;
        }

        return Math.Max(1, (int)Math.Ceiling(storedWidth * scale));
    }


    /// <summary>
    /// Whether a picture of this many pixels is too small for the box - the
    /// box grew past it (the pane widened, a portrait frame met a tall
    /// pane) and a bigger copy would be drawn sharper.
    /// </summary>
    /// <param name="width">Pixels of the copy on screen, as shown.</param>
    /// <param name="height">Same.</param>
    /// <param name="naturalWidth">Pixels of the whole frame, as shown.</param>
    /// <param name="naturalHeight">Same.</param>
    /// <param name="boxWidth">Device pixels the pane has for the picture.</param>
    /// <param name="boxHeight">Same.</param>
    public static bool TooSmall(int width, int height, int naturalWidth, int naturalHeight, double boxWidth, double boxHeight) {
        if (width <= 0 || height <= 0 || width >= naturalWidth) {
            return false;
        }

        // What the box would draw of the whole frame, against what the copy has.
        double wanted = Math.Min(naturalWidth, naturalWidth * Math.Min(boxWidth / naturalWidth, boxHeight / naturalHeight));

        return wanted > width / WholeAbove;
    }
}
