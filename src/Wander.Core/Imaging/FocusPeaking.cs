namespace Wander.Core.Imaging;

/// <summary>
/// Focus peaking: the edges that are crisp, marked the way a camera's
/// viewfinder marks what is in focus.
///
/// <para>
/// By crispness (<see cref="Sharpness.Crispness"/>), not by the strength
/// of the gradient. The first version marked the strongest three percent
/// of gradients, and a frame and the same frame blurred came out with the
/// same three percent marked (3.04 % against 3.17 %, stand 2026-09-22) -
/// in the blurred one, on its contrasty foreground. By crispness the
/// same pair gives 1.1 % against 0.06 %, and the marks lie on the plane
/// of focus.
/// </para>
/// </summary>
public static class FocusPeaking {
    /// <summary>
    /// One byte per pixel, 255 where the edge is at least
    /// <paramref name="threshold"/> crisp - an edge about two pixels wide or
    /// narrower - and 0 elsewhere.
    /// </summary>
    public static byte[] Mask(byte[] crispness, double threshold = 2.0) {
        var mask = new byte[crispness.Length];
        int level = (int)Math.Ceiling(threshold * Sharpness.Unit);
        Parallel.For(0, crispness.Length / 4096 + 1, chunk => {
            int end = Math.Min(crispness.Length, (chunk + 1) * 4096);
            for (int i = chunk * 4096; i < end; i++) {
                if (crispness[i] >= level) {
                    mask[i] = 255;
                }
            }
        });

        return mask;
    }


    /// <summary>
    /// The mask made smaller - <paramref name="tw"/> by <paramref name="th"/>
    /// - keeping every mark: a pixel of the result is set when any pixel it
    /// covers is. Averaged down instead, a one-pixel edge of a 6000-px frame
    /// fades to nothing in a pane a third that wide.
    /// </summary>
    public static byte[] Shrink(byte[] mask, int w, int h, int tw, int th) {
        var small = new byte[tw * th];
        Parallel.For(0, th, ty => {
            int y0 = (int)((long)ty * h / th);
            int y1 = Math.Max(y0 + 1, (int)((long)(ty + 1) * h / th));
            for (int tx = 0; tx < tw; tx++) {
                int x0 = (int)((long)tx * w / tw);
                int x1 = Math.Max(x0 + 1, (int)((long)(tx + 1) * w / tw));
                bool marked = false;
                for (int y = y0; y < y1 && !marked; y++) {
                    for (int x = x0; x < x1; x++) {
                        if (mask[y * w + x] != 0) {
                            marked = true;
                            break;
                        }
                    }
                }
                if (marked) {
                    small[ty * tw + tx] = 255;
                }
            }
        });

        return small;
    }
}
