using Wander.Core.Navigation;

namespace Wander.Core.Tests;

public class NavigationServiceTests {
    // --- Paths reused across cases ------------------------------------
    private const string Foo = @"C:\foo";
    private const string Bar = @"C:\bar";
    private const string Baz = @"C:\baz";
    private const string FooBar = @"C:\foo\bar";
    private const string DriveRoot = @"C:\";


    [Fact]
    public void NavigateTo_SetsCurrent() {
        var nav = new NavigationService();
        nav.NavigateTo(Foo);

        Assert.Equal(Foo, nav.Current);
        Assert.False(nav.CanGoBack);
        Assert.False(nav.CanGoForward);
    }

    [Fact]
    public void NavigateTo_PushesPreviousOntoBackStack() {
        var nav = new NavigationService();
        nav.NavigateTo(Foo);
        nav.NavigateTo(Bar);

        Assert.True(nav.CanGoBack);
        Assert.Equal(Bar, nav.Current);
    }

    [Fact]
    public void GoBack_RestoresPreviousAndEnablesForward() {
        var nav = new NavigationService();
        nav.NavigateTo(Foo);
        nav.NavigateTo(Bar);

        string? result = nav.GoBack();

        Assert.Equal(Foo, result);
        Assert.Equal(Foo, nav.Current);
        Assert.True(nav.CanGoForward);
        Assert.False(nav.CanGoBack);
    }

    [Fact]
    public void GoForward_RedoesNavigation() {
        var nav = new NavigationService();
        nav.NavigateTo(Foo);
        nav.NavigateTo(Bar);
        nav.GoBack();

        string? result = nav.GoForward();

        Assert.Equal(Bar, result);
        Assert.False(nav.CanGoForward);
    }

    [Fact]
    public void NavigateTo_ClearsForwardStack() {
        var nav = new NavigationService();
        nav.NavigateTo(Foo);
        nav.NavigateTo(Bar);
        nav.GoBack();

        nav.NavigateTo(Baz);

        Assert.False(nav.CanGoForward);
    }

    [Fact]
    public void GoUp_NavigatesToParent() {
        var nav = new NavigationService();
        nav.NavigateTo(FooBar);

        string? result = nav.GoUp();

        Assert.Equal(Foo, result);
        Assert.Equal(Foo, nav.Current);
    }

    [Fact]
    public void GoUp_AtRoot_ReturnsNull() {
        var nav = new NavigationService();
        nav.NavigateTo(DriveRoot);

        string? result = nav.GoUp();

        Assert.Null(result);
    }

    [Fact]
    public void CurrentChanged_RaisedOnNavigate() {
        var nav = new NavigationService();
        string? captured = null;
        nav.CurrentChanged += (_, p) => captured = p;

        nav.NavigateTo(Foo);

        Assert.Equal(Foo, captured);
    }

    [Fact]
    public void NavigateTo_SamePath_NoOp() {
        var nav = new NavigationService();
        nav.NavigateTo(Foo);
        nav.NavigateTo(Foo);

        Assert.False(nav.CanGoBack);
    }


    // --- Source preservation -------------------------------------------

    [Fact]
    public void NavigateTo_CarriesSource() {
        var nav = new NavigationService();
        nav.NavigateTo(Foo, NavigationSource.Bookmark);

        Assert.Equal(NavigationSource.Bookmark, nav.CurrentSource);
    }

    [Fact]
    public void GoBack_RestoresOriginalSource() {
        var nav = new NavigationService();
        nav.NavigateTo(Foo, NavigationSource.Drives);
        nav.NavigateTo(Bar, NavigationSource.Bookmark);
        nav.NavigateTo(Baz, NavigationSource.Address);

        nav.GoBack();
        Assert.Equal(Bar, nav.Current);
        Assert.Equal(NavigationSource.Bookmark, nav.CurrentSource);

        nav.GoBack();
        Assert.Equal(Foo, nav.Current);
        Assert.Equal(NavigationSource.Drives, nav.CurrentSource);
    }

    [Fact]
    public void GoForward_RestoresOriginalSource() {
        var nav = new NavigationService();
        nav.NavigateTo(Foo, NavigationSource.Drives);
        nav.NavigateTo(Bar, NavigationSource.Bookmark);
        nav.GoBack();

        nav.GoForward();

        Assert.Equal(Bar, nav.Current);
        Assert.Equal(NavigationSource.Bookmark, nav.CurrentSource);
    }

    [Fact]
    public void GoUp_InheritsCurrentSource() {
        var nav = new NavigationService();
        nav.NavigateTo(FooBar, NavigationSource.Bookmark);

        nav.GoUp();

        Assert.Equal(Foo, nav.Current);
        Assert.Equal(NavigationSource.Bookmark, nav.CurrentSource);
    }

    [Fact]
    public void DefaultSource_IsExternal() {
        var nav = new NavigationService();
        nav.NavigateTo(Foo);

        Assert.Equal(NavigationSource.External, nav.CurrentSource);
    }

    [Fact]
    public void NavigateTo_ForwardEntriesDropped_OnDivergence() {
        var nav = new NavigationService();
        nav.NavigateTo(Foo, NavigationSource.Drives);
        nav.NavigateTo(Bar, NavigationSource.Bookmark);
        nav.GoBack();
        nav.NavigateTo(Baz, NavigationSource.Address);

        Assert.False(nav.CanGoForward);
        Assert.True(nav.CanGoBack);
        nav.GoBack();
        Assert.Equal(Foo, nav.Current);
        Assert.Equal(NavigationSource.Drives, nav.CurrentSource);
    }
}
