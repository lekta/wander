namespace Wander.Core.Imaging;

/// <summary>
/// How much memory decoded pictures may hold (2026-09-24). The thumbnails
/// the lists draw and the frames the preview decodes - the one on show and
/// the ones around it - share one budget: the number of megabytes the user
/// set, or a sixteenth of the machine's memory. Without it three whole
/// frames of 16-bit TIFF were near a gigabyte, and 256 large thumbnails a
/// quarter of one at 200 %. The caches count what they hold in bytes, and a
/// frame decoded ahead that would not fit is not decoded.
/// </summary>
public static class PictureMemory {
    /// <summary>The budget is this part of the machine's memory unless set: 16 GB gives 1 GB, 32 GB gives 2 GB.</summary>
    public const int MachinePart = 16;

    /// <summary>
    /// The thumbnails get this part of the budget, the frames the rest: a
    /// frame is a whole photograph, and three of them are what the preview
    /// decodes ahead; a thumbnail is a tile.
    /// </summary>
    public const int ThumbnailPart = 4;

    private const long Megabyte = 1024 * 1024;


    /// <summary>
    /// The budget in bytes: <paramref name="settingMb"/> when it is set
    /// (above zero), otherwise a sixteenth of <paramref name="machineBytes"/> -
    /// the memory the machine has, not what is free this moment, which comes
    /// and goes with everything else running.
    /// </summary>
    public static long Budget(int settingMb, long machineBytes) {
        return settingMb > 0 ? settingMb * Megabyte : Math.Max(0, machineBytes) / MachinePart;
    }

    /// <summary>What the thumbnails may hold of <paramref name="budget"/>.</summary>
    public static long Thumbnails(long budget) {
        return budget / ThumbnailPart;
    }

    /// <summary>What the frames of every preview together may hold of <paramref name="budget"/>.</summary>
    public static long Frames(long budget) {
        return budget - Thumbnails(budget);
    }

    /// <summary>Whole megabytes of <paramref name="bytes"/>, for saying the budget in words.</summary>
    public static long Megabytes(long bytes) {
        return bytes / Megabyte;
    }

    /// <summary>A bitmap's pixels in memory.</summary>
    public static long BytesOf(int width, int height, int bitsPerPixel = 32) {
        if (width <= 0 || height <= 0 || bitsPerPixel <= 0) {
            return 0;
        }

        return (long)width * height * bitsPerPixel / 8;
    }

    /// <summary>
    /// What a picture of <paramref name="width"/> x <paramref name="height"/>
    /// will take decoded for a box, four bytes a pixel: fitted into the box
    /// when it is decoded to it (a JPEG, a RAW's embedded preview), whole
    /// otherwise. 0 when the size is not known.
    /// </summary>
    public static long DecodedBytes(int width, int height, bool fitted, double boxWidth, double boxHeight) {
        if (width <= 0 || height <= 0) {
            return 0;
        }
        if (!fitted || boxWidth < 1 || boxHeight < 1) {
            return BytesOf(width, height);
        }

        double scale = Math.Min(1, Math.Min(boxWidth / width, boxHeight / height));

        return BytesOf((int)Math.Ceiling(width * scale), (int)Math.Ceiling(height * scale));
    }
}
