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
    public static byte[] Mask(CrispMap map, double threshold = 2.0) {
        var values = map.Values;
        var mask = new byte[values.Length];
        int level = (int)Math.Ceiling(threshold * Sharpness.Unit);
        Parallel.For(0, values.Length / 4096 + 1, chunk => {
            int end = Math.Min(values.Length, (chunk + 1) * 4096);
            for (int i = chunk * 4096; i < end; i++) {
                if (values[i] >= level) {
                    mask[i] = 255;
                }
            }
        });

        return mask;
    }


    /// <summary>
    /// The marks that belong to something, weighted by what it is: 255 for
    /// a patch that runs like an edge - <paramref name="minStretch"/> times
    /// longer than it is wide, or simply large (<paramref name="structure"/>
    /// marks, a textured surface) - and <paramref name="speckWeight"/> for a
    /// small round one. Anything under <paramref name="minSize"/> marks is
    /// dropped altogether.
    ///
    /// <para>
    /// A crisp edge is a line. Dust, sensor noise, falling snow and
    /// out-of-focus points of light are crisp too, pixel by pixel, and they
    /// mark as specks; a frame covered in them has no focus to show (idea of
    /// 2026-09-22). This is what keeps a sensor decode, which is not
    /// denoised, from marking as a frame in focus, and what the sharpness
    /// score weighs the frame's edges by.
    /// </para>
    /// </summary>
    public static byte[] Continuous(
        byte[] mask, int w, int h, int minSize = 6, double minStretch = 2.5,
        int structure = 200, byte speckWeight = 64) {
        var kept = new byte[mask.Length];
        var seen = new bool[mask.Length];
        var patch = new List<int>();
        var stack = new Stack<int>();
        for (int start = 0; start < mask.Length; start++) {
            if (mask[start] == 0 || seen[start]) {
                continue;
            }

            patch.Clear();
            stack.Push(start);
            seen[start] = true;
            int left = w;
            int right = 0;
            int top = h;
            int bottom = 0;
            while (stack.Count > 0) {
                int at = stack.Pop();
                patch.Add(at);
                int x = at % w;
                int y = at / w;
                left = Math.Min(left, x);
                right = Math.Max(right, x);
                top = Math.Min(top, y);
                bottom = Math.Max(bottom, y);
                for (int ny = Math.Max(0, y - 1); ny <= Math.Min(h - 1, y + 1); ny++) {
                    for (int nx = Math.Max(0, x - 1); nx <= Math.Min(w - 1, x + 1); nx++) {
                        int next = ny * w + nx;
                        if (mask[next] != 0 && !seen[next]) {
                            seen[next] = true;
                            stack.Push(next);
                        }
                    }
                }
            }

            if (patch.Count < minSize) {
                continue;
            }

            int side = Math.Max(right - left, bottom - top) + 1;
            int across = Math.Max(1, Math.Min(right - left, bottom - top) + 1);
            byte weight = side >= minStretch * across || patch.Count >= structure ? (byte)255 : speckWeight;
            foreach (int at in patch) {
                kept[at] = weight;
            }
        }

        return kept;
    }


    /// <summary>
    /// The mask made smaller - <paramref name="tw"/> by <paramref name="th"/>.
    /// A pixel of the result is set when enough of what it covers is:
    /// <paramref name="density"/> of nothing keeps every mark, which is what
    /// the preview pane wants (averaged down instead, a one-pixel edge of a
    /// 6000-px frame fades to nothing in a pane a third that wide).
    ///
    /// <para>
    /// A gallery cell is the other case: there one pixel covers a block
    /// fifteen across, "anything at all" is true of nearly every block, and
    /// a sharp frame and a soft one come out looking alike. A share instead
    /// keeps the difference (2026-09-22).
    /// </para>
    /// </summary>
    public static byte[] Shrink(byte[] mask, int w, int h, int tw, int th, double density = 0) {
        var small = new byte[tw * th];
        Parallel.For(0, th, ty => {
            int y0 = (int)((long)ty * h / th);
            int y1 = Math.Max(y0 + 1, (int)((long)(ty + 1) * h / th));
            for (int tx = 0; tx < tw; tx++) {
                int x0 = (int)((long)tx * w / tw);
                int x1 = Math.Max(x0 + 1, (int)((long)(tx + 1) * w / tw));
                int marked = 0;
                for (int y = y0; y < y1; y++) {
                    for (int x = x0; x < x1; x++) {
                        if (mask[y * w + x] != 0) {
                            marked++;
                        }
                    }
                }
                if (marked > 0 && marked >= density * (y1 - y0) * (x1 - x0)) {
                    small[ty * tw + tx] = 255;
                }
            }
        });

        return small;
    }
}
