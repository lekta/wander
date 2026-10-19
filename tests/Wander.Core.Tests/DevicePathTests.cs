using Wander.Core.FileSystem;
using Wander.Core.Navigation;
using Wander.Core.Operations;
using Wander.Core.Shell;

namespace Wander.Core.Tests;

public class DevicePathTests {
    [Fact]
    public void APathOnAPhone_SplitsIntoDeviceStorageAndFolders() {
        var path = DevicePath.Parse(@"device:Pixel 6\Internal shared storage\DCIM\Camera")!;

        Assert.Equal("Pixel 6", path.Device);
        Assert.Equal("Internal shared storage", path.Storage);
        Assert.Equal(new[] { "DCIM", "Camera" }, path.InStorage);
        Assert.False(path.IsRoot);
        Assert.Equal("device:Pixel 6", path.Root);
    }

    [Fact]
    public void TheDeviceItself_IsTheRoot_AndNothingInside() {
        var path = DevicePath.Parse("device:Canon EOS R8")!;

        Assert.True(path.IsRoot);
        Assert.Null(path.Storage);
        Assert.Empty(path.InStorage);
        Assert.False(DevicePath.IsInside("device:Canon EOS R8"));
        Assert.True(DevicePath.IsInside(@"device:Canon EOS R8\SD1"));
    }

    [Theory]
    [InlineData(@"C:\device\x")]
    [InlineData("shell:MyComputerFolder")]
    [InlineData("device:")]
    [InlineData(null)]
    public void AnythingElse_IsNoDevicePath(string? path) {
        Assert.Null(DevicePath.Parse(path));
        Assert.False(DevicePath.IsInside(path));
    }

    [Fact]
    public void TheOrdinaryPathCalls_WalkItTheRightWay() {
        // What the rest of Wander does to a path - parent, leaf, combine,
        // crumbs - works on these without a word about devices.
        const string camera = @"device:Pixel 6\Storage\DCIM\Camera";

        Assert.Equal(@"device:Pixel 6\Storage\DCIM", Path.GetDirectoryName(camera));
        Assert.Equal("device:Pixel 6", Path.GetDirectoryName(@"device:Pixel 6\Storage"));
        Assert.Equal("Camera", Path.GetFileName(camera));
        Assert.Equal(@"device:Pixel 6\Storage\DCIM\Camera\PXL_1.jpg", Path.Combine(camera, "PXL_1.jpg"));

        var crumbs = PathCrumbs.Split(camera);
        Assert.Equal(new[] { "device:Pixel 6", "Storage", "DCIM", "Camera" }, crumbs.Select(c => c.Label));
        Assert.Equal("device:Pixel 6", crumbs[0].Path);
    }

    [Fact]
    public void ASeparatorInAName_DoesNotMakeTwoLevels() {
        Assert.Equal("AC_DC", DevicePath.SafeName(@"AC\DC"));
        Assert.Equal("a_b", DevicePath.SafeName("a/b"));
    }

    [Fact]
    public void AContainer_IsAnArchiveOrADevice() {
        // No shell namespace in a test host: archives answer "no", devices
        // are told by the string alone.
        Assert.True(Containers.Contains("device:Pixel 6"));
        Assert.False(Containers.Inside("device:Pixel 6"));
        Assert.True(Containers.Inside(@"device:Pixel 6\Storage\DCIM"));
        Assert.False(Containers.Contains(@"C:\Photos"));
    }
}


public class OperationPauseTests {
    [Fact]
    public void APause_IsInTheSnapshot_UntilItEnds() {
        var tracker = new OperationTracker(TimeSpan.Zero);
        int changes = 0;
        tracker.Changed += (_, _) => changes++;
        using var op = tracker.Begin(OperationVerbs.Copy, 2);

        op.SetPaused("ProgressWaitingForDevice");
        Assert.Equal("ProgressWaitingForDevice", Assert.Single(tracker.Snapshot()).PausedReason);
        int afterPause = changes;

        // Said again, nothing new to tell.
        op.SetPaused("ProgressWaitingForDevice");
        Assert.Equal(afterPause, changes);

        op.SetPaused(null);
        Assert.Null(Assert.Single(tracker.Snapshot()).PausedReason);
        Assert.Equal(afterPause + 1, changes);
    }
}
