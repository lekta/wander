namespace Wander.Core.Imaging;

/// <summary>
/// Where in a frame its crisp edges are thickest - the place a zoom goes to
/// when nothing points at one (2026-10-08): the full screen's Z with the
/// pointer hidden.
///
/// <para>
/// Not the crispest single edge: one sharp twig in a soft frame is not
/// where the frame is in focus. The frame is cut into square cells, each
/// counts its marks, and the zone is the cell whose block of three by three
/// counts most - about the size of a subject, not of an edge. The answer is
/// the middle of the marks in that block, not of the block itself.
/// </para>
/// </summary>
public static class SharpZone {
    /// <summary>
    /// The middle of the thickest crisp edges, in shares of the frame (0..1
    /// from the top left); null when there are fewer than
    /// <paramref name="minMarks"/> marks in the whole frame.
    /// </summary>
    /// <param name="marks">One byte per pixel, any non-zero is a crisp edge - <see cref="FocusPeaking.Continuous"/>.</param>
    /// <param name="cells">How many cells along the frame's longer side.</param>
    public static (double X, double Y)? Centre(byte[] marks, int w, int h, int cells = 24, int minMarks = 50) {
        if (w <= 0 || h <= 0) {
            return null;
        }

        int side = Math.Max(1, (Math.Max(w, h) + cells - 1) / cells);
        int gw = (w + side - 1) / side;
        int gh = (h + side - 1) / side;
        var count = new long[gw * gh];
        var sumX = new long[gw * gh];
        var sumY = new long[gw * gh];
        Parallel.For(0, gh, gy => {
            int y1 = Math.Min(h, (gy + 1) * side);
            for (int y = gy * side; y < y1; y++) {
                int row = y * w;
                for (int x = 0; x < w; x++) {
                    if (marks[row + x] == 0) {
                        continue;
                    }

                    int cell = gy * gw + x / side;
                    count[cell]++;
                    sumX[cell] += x;
                    sumY[cell] += y;
                }
            }
        });
        if (count.Sum() < minMarks) {
            return null;
        }

        // First best wins a tie: reading order, so the same marks always
        // give the same place.
        int bestX = 0;
        int bestY = 0;
        long best = -1;
        for (int gy = 0; gy < gh; gy++) {
            for (int gx = 0; gx < gw; gx++) {
                long block = Block(count, gw, gh, gx, gy);
                if (block > best) {
                    best = block;
                    bestX = gx;
                    bestY = gy;
                }
            }
        }

        long n = Block(count, gw, gh, bestX, bestY);
        double cx = (double)Block(sumX, gw, gh, bestX, bestY) / n;
        double cy = (double)Block(sumY, gw, gh, bestX, bestY) / n;

        return ((cx + 0.5) / w, (cy + 0.5) / h);
    }


    /// <summary>The sum over the cell and its neighbours, cut at the frame's edges.</summary>
    private static long Block(long[] values, int gw, int gh, int gx, int gy) {
        long sum = 0;
        for (int y = Math.Max(0, gy - 1); y <= Math.Min(gh - 1, gy + 1); y++) {
            for (int x = Math.Max(0, gx - 1); x <= Math.Min(gw - 1, gx + 1); x++) {
                sum += values[y * gw + x];
            }
        }

        return sum;
    }
}
