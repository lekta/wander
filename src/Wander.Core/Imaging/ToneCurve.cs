namespace Wander.Core.Imaging;

/// <summary>
/// Two curves for looking into a frame without developing it: one that
/// lifts the shadows, one that pulls the highlights down. Each works at
/// its own end of the range and leaves the other alone - the middle moves
/// by a few levels, black stays black, white stays white.
///
/// <para>
/// A plain gamma was the first try and it was wrong: gamma 0.6 lifts the
/// midtones by a third, so the frame read as a different exposure rather
/// than as the same frame with its shadows opened (2026-09-22).
/// </para>
/// </summary>
public static class ToneCurve {
    /// <summary>How far the end is pulled by default: 0 is nothing, 1 would be the whole way.</summary>
    public const double Amount = 0.55;


    /// <summary>The shadows lifted, as a table: level in, level out.</summary>
    public static byte[] Shadows(double amount = Amount) {
        return Build(amount, lift: true);
    }


    /// <summary>The highlights pulled down, so what is in them can be seen.</summary>
    public static byte[] Highlights(double amount = Amount) {
        return Build(amount, lift: false);
    }


    /// <summary>A copy of the picture with each colour channel put through <paramref name="lut"/>; alpha as it was.</summary>
    public static BgraImage Apply(BgraImage image, byte[] lut) {
        var pixels = new byte[image.Pixels.Length];
        Parallel.For(0, image.Height, y => {
            int row = y * image.Stride;
            for (int x = 0; x < image.Width; x++) {
                int p = row + x * 4;
                pixels[p] = lut[image.Pixels[p]];
                pixels[p + 1] = lut[image.Pixels[p + 1]];
                pixels[p + 2] = lut[image.Pixels[p + 2]];
                pixels[p + 3] = image.Pixels[p + 3];
            }
        });

        return image with { Pixels = pixels };
    }


    /// <summary>
    /// The curve: a gamma of <c>1 - amount</c> applied through a weight that
    /// is one at the end being worked on and falls away as a cube, so by the
    /// middle almost nothing of it is left. <paramref name="lift"/> works the
    /// dark end; the bright end is the same curve on the upside-down range.
    /// </summary>
    private static byte[] Build(double amount, bool lift) {
        double gamma = 1 - Math.Clamp(amount, 0, 0.9);
        var lut = new byte[256];
        for (int i = 0; i < 256; i++) {
            double value = lift ? i / 255.0 : 1 - i / 255.0;
            double weight = (1 - value) * (1 - value) * (1 - value);
            double moved = weight * Math.Pow(value, gamma) + (1 - weight) * value;
            lut[i] = (byte)Math.Round(255 * (lift ? moved : 1 - moved));
        }

        return lut;
    }
}
