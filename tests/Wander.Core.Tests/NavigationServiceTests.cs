using Wander.Core.Navigation;

namespace Wander.Core.Tests;

public class NavigationServiceTests {
    [Fact]
    public void NavigateTo_SetsCurrent() {
        var nav = new NavigationService();
        nav.NavigateTo(@"C:\foo");

        Assert.Equal(@"C:\foo", nav.Current);
        Assert.False(nav.CanGoBack);
        Assert.False(nav.CanGoForward);
    }

    [Fact]
    public void NavigateTo_PushesPreviousOntoBackStack() {
        var nav = new NavigationService();
        nav.NavigateTo(@"C:\foo");
        nav.NavigateTo(@"C:\bar");

        Assert.True(nav.CanGoBack);
        Assert.Equal(@"C:\bar", nav.Current);
    }

    [Fact]
    public void GoBack_RestoresPreviousAndEnablesForward() {
        var nav = new NavigationService();
        nav.NavigateTo(@"C:\foo");
        nav.NavigateTo(@"C:\bar");

        string? result = nav.GoBack();

        Assert.Equal(@"C:\foo", result);
        Assert.Equal(@"C:\foo", nav.Current);
        Assert.True(nav.CanGoForward);
        Assert.False(nav.CanGoBack);
    }

    [Fact]
    public void GoForward_RedoesNavigation() {
        var nav = new NavigationService();
        nav.NavigateTo(@"C:\foo");
        nav.NavigateTo(@"C:\bar");
        nav.GoBack();

        string? result = nav.GoForward();

        Assert.Equal(@"C:\bar", result);
        Assert.False(nav.CanGoForward);
    }

    [Fact]
    public void NavigateTo_ClearsForwardStack() {
        var nav = new NavigationService();
        nav.NavigateTo(@"C:\foo");
        nav.NavigateTo(@"C:\bar");
        nav.GoBack();

        nav.NavigateTo(@"C:\baz");

        Assert.False(nav.CanGoForward);
    }

    [Fact]
    public void GoUp_NavigatesToParent() {
        var nav = new NavigationService();
        nav.NavigateTo(@"C:\foo\bar");

        string? result = nav.GoUp();

        Assert.Equal(@"C:\foo", result);
        Assert.Equal(@"C:\foo", nav.Current);
    }

    [Fact]
    public void GoUp_AtRoot_ReturnsNull() {
        var nav = new NavigationService();
        nav.NavigateTo(@"C:\");

        string? result = nav.GoUp();

        Assert.Null(result);
    }

    [Fact]
    public void CurrentChanged_RaisedOnNavigate() {
        var nav = new NavigationService();
        string? captured = null;
        nav.CurrentChanged += (_, p) => captured = p;

        nav.NavigateTo(@"C:\foo");

        Assert.Equal(@"C:\foo", captured);
    }

    [Fact]
    public void NavigateTo_SamePath_NoOp() {
        var nav = new NavigationService();
        nav.NavigateTo(@"C:\foo");
        nav.NavigateTo(@"C:\foo");

        Assert.False(nav.CanGoBack);
    }
}
