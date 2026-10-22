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

    /// <summary>"Disk full" on FAT with room for the file: the folder's entries ran out.</summary>
    [Theory]
    [InlineData("FAT32", 25_000_000_000, 30_000_000, true)]
    [InlineData("FAT", 100_000_000, 1_000, true)]
    [InlineData("FAT32", 30_000_000, 29_000_000, false)]
    [InlineData("exFAT", 25_000_000_000, 30_000_000, false)]
    [InlineData("NTFS", 25_000_000_000, 30_000_000, false)]
    public void OutOfEntries_OnlyFatWithRoomForTheFile(string fileSystem, long free, long needed, bool expected) {
        var volume = new VolumeInfo(@"E:\", "", fileSystem, VolumeKind.Removable, free * 2, free, IsReady: true);

        Assert.Equal(expected, volume.OutOfEntries(needed));
    }

    [Fact]
    public void DisplayName_IsTheLabel_OrWhatTheDriveIs_WithItsLetter() {
        var labelled = new VolumeInfo(@"D:\", "Data", "NTFS", VolumeKind.Fixed, 100, 50, IsReady: true);
        var unlabelled = new VolumeInfo(@"C:\", "", "NTFS", VolumeKind.Fixed, 100, 50, IsReady: true);

        Assert.Equal("Data (D:)", labelled.DisplayName("Local Disk"));
        Assert.Equal("Local Disk (C:)", unlabelled.DisplayName("Local Disk"));
    }
}
