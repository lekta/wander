namespace Wander.Core.Imaging;

/// <summary>
/// How crisp the edges of a frame are - the measure behind focus peaking
/// and the sharpness score.
///
/// <para>
/// Crispness of an edge pixel is its Sobel gradient over the local contrast
/// (brightest minus darkest in a window around it). A step from one pixel
/// to the next scores about 4, an edge smeared over w pixels about 4 / w,
/// whatever the contrast of the edge and the light of the scene - which is
/// why it tells a sharp frame from a soft one where the gradient alone
/// does not: a soft frame of a contrasty scene has strong gradients too.
/// Measured on two R8 frames and a copy of each blurred by one and two
/// pixels (stand 2026-09-22): the 98th percentile of crispness at the AF
/// point was 2.7 sharp, 2.3-2.4 and 1.8-1.9 blurred.
/// </para>
///
/// <para>
/// Only meaningful at the picture's own resolution: shrinking a frame
/// narrows its edges, and a camera's 1620-px preview of a missed frame
/// looks as crisp as the hit.
/// </para>
/// </summary>
public static class Sharpness {
    /// <summary>Crispness is stored in a byte as crispness times this; 255 is about 4.</summary>
    public const int Unit = 64;

    /// <summary>Rows of the map one worker takes at a time. Keeps the window buffers small and warm.</summary>
    private const int BandRows = 64;


    /// <summary>
    /// The crispness of every point of the frame - see <see cref="CrispMap"/>.
    /// </summary>
    /// <param name="step">
    /// Measure one point per this many pixels each way. The neighbourhood
    /// is still read at full resolution; only the number of answers goes
    /// down, which is what makes a mark for a thumbnail affordable.
    /// </param>
    /// <param name="radius">Half the side of the window the local contrast is taken over.</param>
    /// <param name="minContrast">Below this a gradient, or the contrast around it, is noise or a flat patch.</param>
    public static CrispMap Measure(byte[] luma, int w, int h, int step = 1, int radius = 3, int minContrast = 24) {
        int mw = (w + step - 1) / step;
        int mh = (h + step - 1) / step;
        var values = new byte[mw * mh];
        if (w < 3 || h < 3) {
            return new CrispMap(values, mw, mh);
        }

        int bands = (mh + BandRows - 1) / BandRows;
        Parallel.For(0, bands, band => {
            int first = band * BandRows;
            int last = Math.Min(mh, first + BandRows) - 1;
            int top = Math.Max(0, first * step - radius);
            int bottom = Math.Min(h - 1, last * step + radius);

            // The window's darkest and brightest across each row, at the
            // sampled columns only - the half of the work that both the
            // rows above and the rows below share.
            var low = new byte[(bottom - top + 1) * mw];
            var high = new byte[(bottom - top + 1) * mw];
            for (int y = top; y <= bottom; y++) {
                int src = y * w;
                int dst = (y - top) * mw;
                for (int mx = 0; mx < mw; mx++) {
                    int x = mx * step;
                    byte lo = 255;
                    byte hi = 0;
                    for (int k = Math.Max(0, x - radius); k <= Math.Min(w - 1, x + radius); k++) {
                        byte v = luma[src + k];
                        lo = Math.Min(lo, v);
                        hi = Math.Max(hi, v);
                    }
                    low[dst + mx] = lo;
                    high[dst + mx] = hi;
                }
            }

            for (int my = first; my <= last; my++) {
                int y = my * step;
                if (y < 1 || y > h - 2) {
                    continue;
                }

                int up = (y - 1) * w;
                int row = y * w;
                int down = (y + 1) * w;
                int from = Math.Max(top, y - radius) - top;
                int to = Math.Min(bottom, y + radius) - top;
                for (int mx = 0; mx < mw; mx++) {
                    int x = mx * step;
                    if (x < 1 || x > w - 2) {
                        continue;
                    }

                    byte lo = 255;
                    byte hi = 0;
                    for (int k = from; k <= to; k++) {
                        lo = Math.Min(lo, low[k * mw + mx]);
                        hi = Math.Max(hi, high[k * mw + mx]);
                    }
                    int range = hi - lo;
                    if (range < minContrast) {
                        continue;
                    }

                    int gx = luma[up + x + 1] + 2 * luma[row + x + 1] + luma[down + x + 1]
                        - luma[up + x - 1] - 2 * luma[row + x - 1] - luma[down + x - 1];
                    int gy = luma[down + x - 1] + 2 * luma[down + x] + luma[down + x + 1]
                        - luma[up + x - 1] - 2 * luma[up + x] - luma[up + x + 1];
                    double gradient = Math.Sqrt(gx * gx + gy * gy);
                    if (gradient < minContrast) {
                        continue;
                    }

                    values[my * mw + mx] = (byte)Math.Clamp(Math.Round(Unit * gradient / range), 1, 255);
                }
            }
        });

        return new CrispMap(values, mw, mh);
    }


    /// <summary>
    /// The sharpness of <paramref name="area"/> as 0..100, from two things:
    /// how crisp its crispest edges are (the <paramref name="quantile"/> of
    /// the edge points, placed between <paramref name="soft"/> and
    /// <paramref name="crisp"/>), and how much of the area's edges are crisp
    /// and continuous at all - <paramref name="crispEdges"/>, the peaking
    /// mask after <see cref="FocusPeaking.Continuous"/>.
    ///
    /// <para>
    /// The second half is what keeps a frame of falling snow, or of sensor
    /// noise, from reading as sharp: every speck of it is a crisp edge, and
    /// the crispest of them says nothing about focus. Below
    /// <paramref name="fullShare"/> of the edges being crisp and continuous
    /// the score is scaled down in proportion.
    /// </para>
    ///
    /// <para>
    /// Null when the area has fewer than <paramref name="minEdges"/> edge
    /// points - a clear sky has nothing to be sharp about.
    /// </para>
    /// </summary>
    public static double? Score(
        CrispMap map, byte[] crispEdges, RectI? area = null,
        double quantile = 0.98, double soft = 1.5, double crisp = 3.0,
        int minEdges = 200, double fullShare = 0.03) {
        var r = area ?? new RectI(0, 0, map.Width, map.Height);
        var histogram = new int[256];
        int edges = 0;
        long continuous = 0;
        for (int y = Math.Max(0, r.Y); y < Math.Min(map.Height, r.Y + r.Height); y++) {
            for (int x = Math.Max(0, r.X); x < Math.Min(map.Width, r.X + r.Width); x++) {
                int at = y * map.Width + x;
                byte c = map.Values[at];
                if (c == 0) {
                    continue;
                }

                histogram[c]++;
                edges++;
                continuous += crispEdges[at];
            }
        }
        if (edges < minEdges) {
            return null;
        }

        long wanted = (long)Math.Ceiling(edges * quantile);
        long taken = 0;
        int level = 255;
        for (int c = 1; c < 256; c++) {
            taken += histogram[c];
            if (taken >= wanted) {
                level = c;
                break;
            }
        }

        double howCrisp = (level / (double)Unit - soft) / (crisp - soft);
        double howMuch = fullShare <= 0 ? 1 : continuous / 255.0 / edges / fullShare;

        return Math.Round(100 * Math.Clamp(howCrisp, 0, 1) * Math.Clamp(howMuch, 0, 1));
    }


    /// <summary>
    /// Where a frame's sharpness is judged: the middle half of it each way.
    ///
    /// <para>
    /// Not the autofocus area, although the camera records one and the pane
    /// draws it. Two frames of the same group of people (2026-09-22) carry
    /// an area in the top corner of the frame, over the ceiling, while the
    /// faces are what the lens was focused on at three metres - and scored
    /// by that corner the missed frame came out ahead of the hit. The area
    /// is worth drawing, because the eye can see whether it makes sense; it
    /// is not worth trusting with the number.
    /// </para>
    /// </summary>
    public static RectI Area(int w, int h) {
        return new RectI(w / 4, h / 4, w / 2, h / 2);
    }
}
