using Wander.Core.FileSystem;

namespace Wander.Core.Tests;

/// <summary>A drive's row in the computer's listing: its name and how full its bar is.</summary>
public class VolumeInfoTests {
    [Theory]
    [InlineData(0, 100, VolumeFill.Normal)]
    [InlineData(84, 100, VolumeFill.Normal)]
    [InlineData(85, 100, VolumeFill.Filling)]
    [InlineData(95, 100, VolumeFill.Full)]
    [InlineData(100, 100, VolumeFill.Full)]
    // Not ready: no size, no colour.
    [InlineData(0, 0, VolumeFill.Normal)]
    public void Fill_FollowsTheShareInUse(long used, long total, VolumeFill expected) {
        var volume = new VolumeInfo(@"C:\", "", "NTFS", VolumeKind.Fixed, total, total - used, IsReady: true);

        Assert.Equal(expected, volume.Fill);
    }

    [Fact]
    public void DisplayName_IsTheLabel_OrWhatTheDriveIs_WithItsLetter() {
        var labelled = new VolumeInfo(@"D:\", "Data", "NTFS", VolumeKind.Fixed, 100, 50, IsReady: true);
        var unlabelled = new VolumeInfo(@"C:\", "", "NTFS", VolumeKind.Fixed, 100, 50, IsReady: true);

        Assert.Equal("Data (D:)", labelled.DisplayName("Local Disk"));
        Assert.Equal("Local Disk (C:)", unlabelled.DisplayName("Local Disk"));
    }
}
