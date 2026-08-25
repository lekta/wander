using Wander.Core.Navigation;

namespace Wander.Core.Tests;

public class PathCrumbsTests {
    [Fact]
    public void Split_ReturnsRootFirst() {
        var crumbs = PathCrumbs.Split(@"D:\Dev\Wander");

        Assert.Equal(new[] { @"D:\", "Dev", "Wander" }, crumbs.Select(c => c.Label));
        Assert.Equal(new[] { @"D:\", @"D:\Dev", @"D:\Dev\Wander" }, crumbs.Select(c => c.Path));
    }

    [Fact]
    public void Split_DriveRootIsASingleCrumb() {
        var crumbs = PathCrumbs.Split(@"D:\");

        Assert.Equal(new[] { @"D:\" }, crumbs.Select(c => c.Label));
        Assert.Equal(new[] { @"D:\" }, crumbs.Select(c => c.Path));
    }

    [Fact]
    public void Split_IgnoresTrailingSeparator() {
        var crumbs = PathCrumbs.Split(@"D:\Dev\Wander\");

        Assert.Equal(new[] { @"D:\", "Dev", "Wander" }, crumbs.Select(c => c.Label));
        // The clicked crumb navigates to the path as typed, trailing
        // separator and all — normalising is the guard's business, not ours.
        Assert.Equal(@"D:\Dev\Wander\", crumbs[^1].Path);
    }

    [Fact]
    public void Split_UncShareIsTheRoot() {
        var crumbs = PathCrumbs.Split(@"\\server\share\photos");

        Assert.Equal(new[] { @"\\server\share", "photos" }, crumbs.Select(c => c.Label));
        Assert.Equal(new[] { @"\\server\share", @"\\server\share\photos" }, crumbs.Select(c => c.Path));
    }

    [Fact]
    public void Split_ShellSentinelStaysWhole() {
        var crumbs = PathCrumbs.Split("shell:RecycleBinFolder");

        Assert.Equal(new[] { "shell:RecycleBinFolder" }, crumbs.Select(c => c.Label));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Split_EmptyInputHasNoCrumbs(string? path) {
        Assert.Empty(PathCrumbs.Split(path));
    }
}
