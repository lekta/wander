namespace Wander.Core.Imaging;

/// <summary>
/// Brightness of each pixel, the one channel the edge and sharpness
/// measures look at. Rec.601 weights in integers: 77 + 150 + 29 is 256, so
/// the shift divides exactly and white stays 255.
/// </summary>
public static class Luma {
    public static byte[] Of(BgraImage image) {
        var luma = new byte[image.Width * image.Height];
        Parallel.For(0, image.Height, y => {
            int row = y * image.Stride;
            int at = y * image.Width;
            for (int x = 0; x < image.Width; x++) {
                int p = row + x * 4;
                luma[at + x] = Of(image.Pixels[p + 2], image.Pixels[p + 1], image.Pixels[p]);
            }
        });

        return luma;
    }


    public static byte Of(byte r, byte g, byte b) {
        return (byte)((77 * r + 150 * g + 29 * b) >> 8);
    }


    /// <summary>
    /// The brightness with its single-pixel noise taken out: the middle of
    /// each three-by-three neighbourhood. A step stays a step - that is what
    /// a median does and a blur does not - so edges keep their width and
    /// their crispness with them.
    ///
    /// <para>
    /// For what comes off a sensor rather than out of a camera's JPEG: the
    /// decode is not denoised and not sharpened, and every grain of noise in
    /// it is a crisp little edge. Measured on two R8 frames (2026-09-22):
    /// peaking marked 4.6 % and 6.4 % of the frame before this, 0.9 % and
    /// 1.1 % after - the same share as the camera's own JPEG of the same
    /// frame, and on the same detail.
    /// </para>
    /// </summary>
    public static byte[] Denoise(byte[] luma, int w, int h) {
        var quiet = new byte[luma.Length];
        if (w < 3 || h < 3) {
            Array.Copy(luma, quiet, luma.Length);

            return quiet;
        }

        Parallel.For(1, h - 1, y => {
            var window = new byte[9];
            int row = y * w;
            for (int x = 1; x < w - 1; x++) {
                int k = 0;
                for (int dy = -1; dy <= 1; dy++) {
                    for (int dx = -1; dx <= 1; dx++) {
                        window[k++] = luma[row + dy * w + x + dx];
                    }
                }
                Array.Sort(window);
                quiet[row + x] = window[4];
            }
        });

        return quiet;
    }
}
