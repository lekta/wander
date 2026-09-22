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


    /// <summary>
    /// Crispness per pixel, times <see cref="Unit"/>. Zero where there is no
    /// edge to judge: a gradient or a local contrast under
    /// <paramref name="minContrast"/> is noise or a flat patch, and the
    /// one-pixel border has no full neighbourhood.
    /// </summary>
    /// <param name="radius">Half the side of the window the local contrast is taken over.</param>
    public static byte[] Crispness(byte[] luma, int w, int h, int radius = 3, int minContrast = 24) {
        var crisp = new byte[w * h];
        if (w < 3 || h < 3) {
            return crisp;
        }

        var (low, high) = LocalRange(luma, w, h, radius);
        Parallel.For(1, h - 1, y => {
            int up = (y - 1) * w;
            int row = y * w;
            int down = (y + 1) * w;
            for (int x = 1; x < w - 1; x++) {
                int range = high[row + x] - low[row + x];
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

                crisp[row + x] = (byte)Math.Clamp(Math.Round(Unit * gradient / range), 1, 255);
            }
        });

        return crisp;
    }


    /// <summary>
    /// The sharpness of <paramref name="area"/> as 0..100: the crispness of
    /// its crispest edges (the <paramref name="quantile"/> of the edge
    /// pixels) placed between <paramref name="soft"/> and
    /// <paramref name="crisp"/>. Null when the area has fewer than
    /// <paramref name="minEdges"/> edge pixels - a clear sky has nothing to
    /// be sharp about.
    /// </summary>
    public static double? Score(
        byte[] crispness, int w, int h, RectI? area = null,
        double quantile = 0.98, double soft = 1.5, double crisp = 3.0, int minEdges = 200) {
        var r = area ?? new RectI(0, 0, w, h);
        var histogram = new int[256];
        int edges = 0;
        for (int y = Math.Max(0, r.Y); y < Math.Min(h, r.Y + r.Height); y++) {
            for (int x = Math.Max(0, r.X); x < Math.Min(w, r.X + r.Width); x++) {
                byte c = crispness[y * w + x];
                if (c != 0) {
                    histogram[c]++;
                    edges++;
                }
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

        double value = (level / (double)Unit - soft) / (crisp - soft);

        return Math.Round(100 * Math.Clamp(value, 0, 1));
    }


    /// <summary>
    /// Where a frame's sharpness is judged: around the autofocus area the
    /// camera reports in focus (the first one, when none says so), twice its
    /// size each way so the subject's own edges are in; without one, the
    /// middle half of the frame each way.
    /// </summary>
    /// <param name="af">Autofocus areas over the same picture as <paramref name="w"/> by <paramref name="h"/>.</param>
    public static RectI Area(int w, int h, IReadOnlyList<AfPoint>? af) {
        var point = af?.FirstOrDefault(p => p.InFocus) ?? af?.FirstOrDefault();
        if (point is null) {
            return new RectI(w / 4, h / 4, w / 2, h / 2);
        }

        int left = (int)Math.Round((point.X - point.W) * w);
        int top = (int)Math.Round((point.Y - point.H) * h);
        int right = (int)Math.Round((point.X + point.W) * w);
        int bottom = (int)Math.Round((point.Y + point.H) * h);
        left = Math.Clamp(left, 0, w);
        top = Math.Clamp(top, 0, h);
        right = Math.Clamp(right, left, w);
        bottom = Math.Clamp(bottom, top, h);

        return new RectI(left, top, right - left, bottom - top);
    }


    /// <summary>Darkest and brightest luma in the square of side 2r + 1 around each pixel, clipped at the frame.</summary>
    private static (byte[] Low, byte[] High) LocalRange(byte[] luma, int w, int h, int r) {
        var rowLow = new byte[w * h];
        var rowHigh = new byte[w * h];
        Parallel.For(0, h, y => {
            int row = y * w;
            for (int x = 0; x < w; x++) {
                byte low = 255;
                byte high = 0;
                for (int k = Math.Max(0, x - r); k <= Math.Min(w - 1, x + r); k++) {
                    byte v = luma[row + k];
                    low = Math.Min(low, v);
                    high = Math.Max(high, v);
                }
                rowLow[row + x] = low;
                rowHigh[row + x] = high;
            }
        });

        var low = new byte[w * h];
        var high = new byte[w * h];
        Parallel.For(0, h, y => {
            int from = Math.Max(0, y - r);
            int to = Math.Min(h - 1, y + r);
            for (int x = 0; x < w; x++) {
                byte lo = 255;
                byte hi = 0;
                for (int k = from; k <= to; k++) {
                    lo = Math.Min(lo, rowLow[k * w + x]);
                    hi = Math.Max(hi, rowHigh[k * w + x]);
                }
                low[y * w + x] = lo;
                high[y * w + x] = hi;
            }
        });

        return (low, high);
    }
}
