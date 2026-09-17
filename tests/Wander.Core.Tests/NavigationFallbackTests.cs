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
    public void CaseAndTrailingSeparators_DoNotMatter() {
        Assert.Equal(@"C:\work", NavigationFallback.AfterDelete(new[] { @"c:\WORK\New\" }, @"C:\work\new\a"));
        Assert.Equal(@"C:\work", NavigationFallback.AfterDelete(new[] { @"C:\work\new" }, @"C:\work\new\"));
    }
}
