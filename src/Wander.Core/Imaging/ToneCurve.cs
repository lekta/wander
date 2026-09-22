namespace Wander.Core.Imaging;

/// <summary>
/// A gamma curve over the levels, to look into the shadows or the
/// highlights of a frame without developing it. Below 1 lifts the
/// shadows; above 1 spreads the top of the range apart. Black and white
/// stay where they are either way.
/// </summary>
public static class ToneCurve {
    /// <summary>Lifts the shadows.</summary>
    public const double Shadows = 0.6;

    /// <summary>Opens up the highlights.</summary>
    public const double Highlights = 1.7;


    /// <summary>The curve as a table: level in, level out.</summary>
    public static byte[] Lut(double gamma) {
        var lut = new byte[256];
        for (int i = 0; i < 256; i++) {
            lut[i] = (byte)Math.Round(255 * Math.Pow(i / 255.0, gamma));
        }

        return lut;
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
}
