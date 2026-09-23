using Wander.Core.Panels;

namespace Wander.Core.Tests;

public class PanelPathsTests {
    [Fact]
    public void Same_IgnoresCaseAndTheTrailingSeparator() {
        Assert.True(PanelPaths.Same(@"C:\", "c:"));
        Assert.True(PanelPaths.Same(@"D:\Photos\", @"d:\photos"));
        Assert.False(PanelPaths.Same(@"D:\Photos", null));
        Assert.True(PanelPaths.Same(null, null));
    }

    /// <summary>"C:" alone is the current folder of drive C: a key read back as a path gets the root's separator.</summary>
    [Fact]
    public void FromKey_GivesADriveItsRootBack() {
        Assert.Equal(@"C:\", PanelPaths.FromKey(PanelPaths.Key(@"C:\")));
        Assert.Equal(@"C:\A", PanelPaths.FromKey(PanelPaths.Key(@"C:\A\")));
    }

    [Fact]
    public void IsUnderOrSelf_IsAboutWholeNames() {
        Assert.True(PanelPaths.IsUnderOrSelf(@"C:\A\B", @"C:\A"));
        Assert.True(PanelPaths.IsUnderOrSelf(@"C:\A", @"C:\"));
        Assert.False(PanelPaths.IsUnderOrSelf(@"C:\AB", @"C:\A"));
        Assert.False(PanelPaths.IsInside(@"C:\A", @"C:\A"));
    }

    [Fact]
    public void Parent_StopsAtTheRoot_AndAtAShellPath() {
        Assert.Equal(@"C:\", PanelPaths.Parent(@"C:\A"));
        Assert.Null(PanelPaths.Parent(@"C:\"));
        Assert.Null(PanelPaths.Parent("shell:RecycleBinFolder"));
    }

    [Fact]
    public void Chain_RunsFromTheRootDown() {
        Assert.Equal(new[] { @"C:\", @"C:\A", @"C:\A\B" }, PanelPaths.Chain(@"C:\", @"C:\A\B"));
        Assert.Equal(new[] { @"D:\Photos", @"D:\Photos\2026" }, PanelPaths.Chain(@"D:\Photos", @"D:\Photos\2026"));
        Assert.Empty(PanelPaths.Chain(@"D:\Photos", @"C:\A"));
    }

    [Fact]
    public void Follow_MovesWhatIsInside_AndLeavesTheRest() {
        Assert.Equal(@"C:\X\B", PanelPaths.Follow(@"C:\A\B", @"C:\A", @"C:\X"));
        Assert.Equal(@"C:\X", PanelPaths.Follow(@"C:\A", @"C:\A", @"C:\X"));
        Assert.Equal(@"C:\AB", PanelPaths.Follow(@"C:\AB", @"C:\A", @"C:\X"));
    }
}
