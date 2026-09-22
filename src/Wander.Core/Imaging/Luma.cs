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
}
