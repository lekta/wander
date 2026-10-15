using Wander.Core.FileSystem;

namespace Wander.Core.Tests;

public class DeviceClutterTests {
    [Theory]
    [InlineData("LOST.DIR")]
    [InlineData(".thumbnails")]
    [InlineData("Android/data")]
    [InlineData("Android/OBB")]
    [InlineData("DCIM/.thumbnails")]
    [InlineData("Pictures/.trashed-1700000000-IMG_1.jpg")]
    [InlineData(".face")]
    public void TheSystemsAndTheAppsOwn_AreClutter(string path) {
        Assert.True(DeviceClutter.IsClutter(path.Split('/'), isEmpty: false));
    }

    [Theory]
    [InlineData("Android")]
    [InlineData("Android/media")]
    [InlineData("DCIM")]
    [InlineData("Pictures")]
    [InlineData("Download")]
    [InlineData("Documents")]
    [InlineData("Movies")]
    [InlineData("Music")]
    [InlineData("Oculus")]
    [InlineData("Oculus/Screenshots")]
    [InlineData("DCIM/data")]
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
        Assert.True(DeviceClutter.IsClutter(new[] { name }, isEmpty: true));
        Assert.False(DeviceClutter.IsClutter(new[] { name }, isEmpty: false));
        // Not asked - a request over the cable each: left showing.
        Assert.False(DeviceClutter.IsClutter(new[] { name }, isEmpty: null));
    }

    [Fact]
    public void AStandardNameDeeper_IsNotTheStandardFolder() {
        Assert.False(DeviceClutter.IsClutter(new[] { "Music", "Ringtones" }, isEmpty: true));
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
}
