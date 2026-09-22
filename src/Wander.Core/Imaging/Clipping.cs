namespace Wander.Core.Imaging;

/// <summary>
/// Where the frame has nothing left to pull back: channels at the top of
/// their range, and brightness at the bottom of it.
///
/// <para>
/// Highlights by channel, because that is how they clip - a sunset burns
/// out in red long before it goes white, and the hue shift is the damage.
/// Shadows by brightness: a pixel is lost there when all of it is black,
/// not when its blue is.
/// </para>
/// </summary>
public static class Clipping {
    public const byte Red = 1;
    public const byte Green = 2;
    public const byte Blue = 4;
    public const byte Dark = 8;


    /// <summary>
    /// Flags per pixel: <see cref="Red"/>, <see cref="Green"/>,
    /// <see cref="Blue"/> for a channel at or above <paramref name="high"/>,
    /// <see cref="Dark"/> for brightness at or below <paramref name="low"/>.
    /// </summary>
    public static byte[] Mask(BgraImage image, byte high = 250, byte low = 5) {
        var flags = new byte[image.Width * image.Height];
        Parallel.For(0, image.Height, y => {
            int row = y * image.Stride;
            int at = y * image.Width;
            for (int x = 0; x < image.Width; x++) {
                int p = row + x * 4;
                byte b = image.Pixels[p];
                byte g = image.Pixels[p + 1];
                byte r = image.Pixels[p + 2];
                int f = 0;
                if (r >= high) {
                    f |= Red;
                }
                if (g >= high) {
                    f |= Green;
                }
                if (b >= high) {
                    f |= Blue;
                }
                if (Luma.Of(r, g, b) <= low) {
                    f |= Dark;
                }
                flags[at + x] = (byte)f;
            }
        });

        return flags;
    }
}
