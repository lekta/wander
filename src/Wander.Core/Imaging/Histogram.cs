namespace Wander.Core.Imaging;

/// <summary>The levels of a frame by channel - see <see cref="HistogramData"/>.</summary>
public static class Histogram {
    public static HistogramData Compute(BgraImage image) {
        var red = new int[256];
        var green = new int[256];
        var blue = new int[256];
        var luma = new int[256];
        int over = 0;
        for (int y = 0; y < image.Height; y++) {
            int row = y * image.Stride;
            for (int x = 0; x < image.Width; x++) {
                int p = row + x * 4;
                byte b = image.Pixels[p];
                byte g = image.Pixels[p + 1];
                byte r = image.Pixels[p + 2];
                blue[b]++;
                green[g]++;
                red[r]++;
                luma[Luma.Of(r, g, b)]++;
                if (r == 255 || g == 255 || b == 255) {
                    over++;
                }
            }
        }

        int pixels = image.Width * image.Height;
        if (pixels == 0) {
            return new HistogramData(red, green, blue, luma, 0, 0, 0);
        }

        return new HistogramData(red, green, blue, luma, pixels, (double)over / pixels, (double)luma[0] / pixels);
    }
}
