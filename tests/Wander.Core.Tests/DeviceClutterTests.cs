using Wander.Core.FileSystem;

namespace Wander.Core.Tests;

public class DeviceClutterTests {
    [Theory]
    [InlineData("LOST.DIR")]
    [InlineData(".thumbnails")]
    [InlineData("Android/data")]
    [InlineData("Android/OBB")]
    [InlineData("Android/obj")]
    [InlineData("DCIM/.thumbnails")]
    [InlineData("Pictures/.trashed-1700000000-IMG_1.jpg")]
    [InlineData(".face")]
    public void TheSystemsAndTheAppsOwn_AreClutter(string path) {
        Assert.True(DeviceClutter.IsClutter(path.Split('/'), isEmpty: false));
    }

    [Theory]
    [InlineData("Android")]
    [InlineData("Android/media")]
    [InlineData("Android/Media")]
    [InlineData("DCIM")]
    [InlineData("Pictures")]
    [InlineData("Download")]
    [InlineData("Documents")]
    [InlineData("Movies")]
    [InlineData("Music")]
    [InlineData("Oculus")]
    [InlineData("Oculus/Screenshots")]
    [InlineData("DCIM/data")]
    [InlineData("Android/media/com.whatsapp")]
    public void WhatPeopleKeepThere_IsNot(string path) {
        Assert.False(DeviceClutter.IsClutter(path.Split('/'), isEmpty: false));
    }

    [Theory]
    [InlineData("Alarms")]
    [InlineData("Notifications")]
    [InlineData("Ringtones")]
    [InlineData("Podcasts")]
    [InlineData("Audiobooks")]
    [InlineData("Recordings")]
    public void AStandardFolder_IsClutterOnlyWhenEmpty(string name) {
        Assert.True(DeviceClutter.IsEmptyStandard(name));
        Assert.True(DeviceClutter.IsClutter(new[] { name }, isEmpty: true));
        Assert.False(DeviceClutter.IsClutter(new[] { name }, isEmpty: false));
        // Not asked - a request over the cable each: left showing.
        Assert.False(DeviceClutter.IsClutter(new[] { name }, isEmpty: null));
    }

    [Fact]
    public void AStandardNameDeeper_IsNotTheStandardFolder() {
        Assert.False(DeviceClutter.IsClutter(new[] { "Music", "Ringtones" }, isEmpty: true));
    }

    [Theory]
    [InlineData("Subtitles")]
    [InlineData("bluetooth")]
    [InlineData("imgcache")]
    public void AnEmptyFolderOfAnyOtherName_Stays(string name) {
        // By its name it cannot be told from one of the owner's.
        Assert.False(DeviceClutter.IsEmptyStandard(name));
        Assert.False(DeviceClutter.IsClutter(new[] { name }, isEmpty: true));
    }

    [Fact]
    public void AFolderWithOnlyDotNames_LooksEmpty() {
        // Music and Movies on a Pixel: a lone .thumbnails.
        Assert.True(DeviceClutter.LooksEmpty(new[] { ".thumbnails" }));
        Assert.True(DeviceClutter.LooksEmpty(Array.Empty<string>()));
        Assert.False(DeviceClutter.LooksEmpty(new[] { ".thumbnails", "viber.mp3" }));
    }
}


public class DeviceErrorsTests {
    [Theory]
    [InlineData(unchecked((int)0x802A0002), true)]
    [InlineData(unchecked((int)0x802A0006), true)]
    [InlineData(unchecked((int)0x80070015), false)]
    public void ACameraThatStoppedAnswering_IsToldApart(int hresult, bool asleep) {
        Assert.Equal(asleep, DeviceErrors.IsAsleep(new IOException("x", hresult)));
    }

    [Theory]
    [InlineData(unchecked((int)0x8007001F), true)]   // the cable, the switch (stand Y4a)
    [InlineData(unchecked((int)0x80070015), true)]   // not ready
    [InlineData(unchecked((int)0x8007048F), true)]   // not connected
    [InlineData(unchecked((int)0x802A0002), true)]   // not open
    [InlineData(unchecked((int)0x80070070), false)]  // the disk is full - the target's problem
    [InlineData(unchecked((int)0x80070005), false)]  // access denied
    public void ADeviceThatWentAway_IsToldApart(int hresult, bool gone) {
        Assert.Equal(gone, DeviceErrors.IsGone(new IOException("x", hresult)));
    }
}
