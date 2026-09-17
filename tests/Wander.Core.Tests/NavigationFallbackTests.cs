using Wander.Core.FileSystem;
using Wander.Core.Navigation;

namespace Wander.Core.Tests;

public class NavigationFallbackTests {
    [Fact]
    public void CurrentUntouched_StaysPut() {
        Assert.Null(NavigationFallback.AfterDelete(new[] { @"C:\work\old" }, @"C:\work\new"));
        Assert.Null(NavigationFallback.AfterDelete(new[] { @"C:\work\new\child" }, @"C:\work\new"));
    }

    [Fact]
    public void ASiblingWithTheSamePrefix_IsNotCovered() {
        Assert.Null(NavigationFallback.AfterDelete(new[] { @"C:\photos" }, @"C:\photos-old\2024"));
    }

    [Fact]
    public void CurrentDeleted_GoesToItsParent() {
        Assert.Equal(@"C:\work", NavigationFallback.AfterDelete(new[] { @"C:\work\new" }, @"C:\work\new"));
    }

    [Fact]
    public void AncestorDeleted_GoesToTheAncestorsParent() {
        Assert.Equal(@"C:\work", NavigationFallback.AfterDelete(new[] { @"C:\work\new" }, @"C:\work\new\a\b"));
    }

    [Fact]
    public void NestedDeletions_ClimbPastBoth() {
        Assert.Equal(@"C:\work",
            NavigationFallback.AfterDelete(new[] { @"C:\work\new\a", @"C:\work\new" }, @"C:\work\new\a\b"));
        Assert.Equal(@"C:\",
            NavigationFallback.AfterDelete(new[] { @"C:\work\new", @"C:\work" }, @"C:\work\new"));
    }

    [Fact]
    public void NoCurrentFolder_HasNowhereToGo() {
        Assert.Null(NavigationFallback.AfterDelete(new[] { @"C:\work" }, null));
    }

    [Fact]
    public void Restore_APlaceStillThere_OpensAsItWas() {
        int kindAsked = 0;

        string? start = NavigationFallback.AfterRestore(@"E:\photos", _ => true, _ => {
            kindAsked++;
            return VolumeKind.Removable;
        });

        Assert.Equal(@"E:\photos", start);
        Assert.Equal(0, kindAsked);
    }

    [Fact]
    public void Restore_GoneFromAnOwnDrive_OpensTheNearestSurvivor() {
        var there = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { @"D:\", @"D:\Raw" };

        string? start = NavigationFallback.AfterRestore(@"D:\Raw\2026\trip\", there.Contains, _ => VolumeKind.Fixed);

        Assert.Equal(@"D:\Raw", start);
    }

    [Theory]
    [InlineData(VolumeKind.Removable)]
    [InlineData(VolumeKind.Network)]
    [InlineData(VolumeKind.Optical)]
    [InlineData(VolumeKind.Unknown)]
    public void Restore_GoneFromAnythingElse_HasNowhereToGo(VolumeKind kind) {
        // A pulled flash drive, or another one in its letter: its root is
        // there or not, either way it is not where to reopen.
        string? start = NavigationFallback.AfterRestore(@"E:\photos\trip", p => p == @"E:\", _ => kind);

        Assert.Null(start);
    }

    [Fact]
    public void Restore_NothingLeftAtAll_HasNowhereToGo() {
        Assert.Null(NavigationFallback.AfterRestore(@"D:\Raw\trip", _ => false, _ => VolumeKind.Fixed));
    }

    [Fact]
    public void CaseAndTrailingSeparators_DoNotMatter() {
        Assert.Equal(@"C:\work", NavigationFallback.AfterDelete(new[] { @"c:\WORK\New\" }, @"C:\work\new\a"));
        Assert.Equal(@"C:\work", NavigationFallback.AfterDelete(new[] { @"C:\work\new" }, @"C:\work\new\"));
    }
}
