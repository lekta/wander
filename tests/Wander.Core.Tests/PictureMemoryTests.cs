using Wander.Core.Imaging;

namespace Wander.Core.Tests;

public class PictureMemoryTests {
    private const long Gigabyte = 1024L * 1024 * 1024;


    /// <summary>Decided 2026-09-24: a sixteenth of the machine unless set - 16 GB gives 1 GB, 32 GB gives 2 GB.</summary>
    [Fact]
    public void Budget_IsASixteenthOfTheMachine_UnlessSet() {
        Assert.Equal(Gigabyte, PictureMemory.Budget(0, 16 * Gigabyte));
        Assert.Equal(2 * Gigabyte, PictureMemory.Budget(0, 32 * Gigabyte));
        Assert.Equal(512L * 1024 * 1024, PictureMemory.Budget(512, 16 * Gigabyte));
    }

    [Fact]
    public void Budget_WithNothingKnown_IsNothing() {
        Assert.Equal(0, PictureMemory.Budget(0, 0));
        Assert.Equal(0, PictureMemory.Budget(-5, -1));
    }

    [Fact]
    public void Shares_ThumbnailsAQuarter_FramesTheRest() {
        Assert.Equal(256L * 1024 * 1024, PictureMemory.Thumbnails(Gigabyte));
        Assert.Equal(768L * 1024 * 1024, PictureMemory.Frames(Gigabyte));
        Assert.Equal(Gigabyte, PictureMemory.Thumbnails(Gigabyte) + PictureMemory.Frames(Gigabyte));
    }

    [Fact]
    public void BytesOf_CountsEveryPixel() {
        Assert.Equal(6000L * 4000 * 4, PictureMemory.BytesOf(6000, 4000));
        // A 16-bit TIFF decodes to eight bytes a pixel.
        Assert.Equal(6000L * 4000 * 8, PictureMemory.BytesOf(6000, 4000, 64));
        Assert.Equal(0, PictureMemory.BytesOf(0, 4000));
    }

    /// <summary>A JPEG is decoded to the pane; everything else whole (PLAN AK, step 3).</summary>
    [Fact]
    public void DecodedBytes_FittedIntoTheBox_OrWhole() {
        Assert.Equal(PictureMemory.BytesOf(1500, 1000), PictureMemory.DecodedBytes(6000, 4000, fitted: true, 1500, 1500));
        Assert.Equal(PictureMemory.BytesOf(6000, 4000), PictureMemory.DecodedBytes(6000, 4000, fitted: false, 1500, 1500));
        // Smaller than the box: never blown up.
        Assert.Equal(PictureMemory.BytesOf(800, 600), PictureMemory.DecodedBytes(800, 600, fitted: true, 1500, 1500));
        // No box yet: decoded whole.
        Assert.Equal(PictureMemory.BytesOf(6000, 4000), PictureMemory.DecodedBytes(6000, 4000, fitted: true, 0, 0));
        Assert.Equal(0, PictureMemory.DecodedBytes(0, 0, fitted: false, 1500, 1500));
    }
}
