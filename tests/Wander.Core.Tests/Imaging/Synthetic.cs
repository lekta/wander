using Wander.Core.Imaging;

namespace Wander.Core.Tests.Imaging;

/// <summary>Pictures made to order for the imaging tests: flat fields, steps, squares.</summary>
internal static class Synthetic {
    public static byte[] Flat(int w, int h, byte value) {
        return Enumerable.Repeat(value, w * h).ToArray();
    }


    /// <summary>Dark left of column <paramref name="at"/>, light from it on.</summary>
    public static byte[] Step(int w, int h, int at, byte dark = 40, byte light = 200) {
        var luma = new byte[w * h];
        for (int y = 0; y < h; y++) {
            for (int x = 0; x < w; x++) {
                luma[y * w + x] = x < at ? dark : light;
            }
        }

        return luma;
    }


    /// <summary>Each row averaged over a window <paramref name="radius"/> pixels either side - a soft edge.</summary>
    public static byte[] BlurRows(byte[] luma, int w, int h, int radius) {
        var blurred = new byte[luma.Length];
        for (int y = 0; y < h; y++) {
            for (int x = 0; x < w; x++) {
                int sum = 0;
                int n = 0;
                for (int k = Math.Max(0, x - radius); k <= Math.Min(w - 1, x + radius); k++) {
                    sum += luma[y * w + k];
                    n++;
                }
                blurred[y * w + x] = (byte)(sum / n);
            }
        }

        return blurred;
    }


    /// <summary>
    /// A step the way a lens misses it: blurred twice over, so it fades in and
    /// out rather than starting and stopping as a box blur does - nine pixels
    /// from dark to light.
    /// </summary>
    public static byte[] Soft(byte[] luma, int w, int h) {
        return BlurRows(BlurRows(luma, w, h, 2), w, h, 2);
    }


    /// <summary>
    /// Crisp specks on a flat field, every <paramref name="spacing"/> pixels:
    /// stars, falling snow, the noise of a sensor decode.
    /// </summary>
    public static byte[] Dots(int w, int h, int spacing, byte field = 40, byte dot = 220) {
        var luma = Flat(w, h, field);
        for (int y = spacing; y < h - spacing; y += spacing) {
            for (int x = spacing; x < w - spacing; x += spacing) {
                luma[y * w + x] = dot;
            }
        }

        return luma;
    }


    /// <summary>A grey frame with a square of another colour in it.</summary>
    public static BgraImage Square(int w, int h, RectI square, byte inside, byte outside = 128) {
        int stride = w * 4;
        var pixels = new byte[stride * h];
        for (int y = 0; y < h; y++) {
            for (int x = 0; x < w; x++) {
                bool within = x >= square.X && x < square.X + square.Width && y >= square.Y && y < square.Y + square.Height;
                byte v = within ? inside : outside;
                int p = y * stride + x * 4;
                pixels[p] = v;
                pixels[p + 1] = v;
                pixels[p + 2] = v;
                pixels[p + 3] = 255;
            }
        }

        return new BgraImage(pixels, w, h, stride);
    }
}
