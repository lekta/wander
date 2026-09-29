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

    /// <summary>
    /// A step off the panels - into a folder of the list, a drag held over
    /// one, the address bar and its crumbs, "open file location" - keeps
    /// the bookmarks' context; the panels fall back to the drives once the
    /// path leaves every bookmark (2026-09-29: a drag held over a subfolder
    /// of a bookmark opened the drives tree down to it).
    /// </summary>
    [Theory]
    [InlineData(NavigationSource.RightPane)]
    [InlineData(NavigationSource.Address)]
    [InlineData(NavigationSource.External)]
    public void AStepOffThePanels_KeepsTheBookmarksContext(NavigationSource step) {
        var nav = new NavigationService();
        nav.NavigateTo(Foo, NavigationSource.Bookmark);

        nav.NavigateTo(FooBar, step);

        Assert.Equal(NavigationSource.Bookmark, nav.CurrentSource);
    }

    /// <summary>The logged case: a drag held over a subfolder, then Up and Enter - all of it stays in the bookmarks.</summary>
    [Fact]
    public void DragIntoASubfolder_ThenUpAndBackIn_StaysInTheBookmarks() {
        var nav = new NavigationService();
        nav.NavigateTo(Foo, NavigationSource.Bookmark);

        nav.NavigateTo(FooBar, NavigationSource.RightPane);
        nav.GoUp();
        nav.NavigateTo(FooBar, NavigationSource.RightPane);

        Assert.Equal(FooBar, nav.Current);
        Assert.Equal(NavigationSource.Bookmark, nav.CurrentSource);
    }

    /// <summary>A panel's own click says where it was made; nothing is inherited over it, and nothing but the bookmarks is inherited.</summary>
    [Fact]
    public void APanelClick_IsKept_AndTheDrivesContextIsNotInherited() {
        var nav = new NavigationService();
        nav.NavigateTo(Foo, NavigationSource.Bookmark);

        nav.NavigateTo(Bar, NavigationSource.Drives);
        Assert.Equal(NavigationSource.Drives, nav.CurrentSource);

        nav.NavigateTo(Baz, NavigationSource.RightPane);
        Assert.Equal(NavigationSource.RightPane, nav.CurrentSource);

        nav.NavigateTo(Foo, NavigationSource.Bookmark);
        nav.NavigateTo(FooBar, NavigationSource.Restore);
        Assert.Equal(NavigationSource.Restore, nav.CurrentSource);
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

    [Fact]
    public void RewritePaths_FollowsAMovedFolder_InTheCurrentEntryAndBehindIt() {
        var nav = new NavigationService();
        nav.NavigateTo(Foo, NavigationSource.Bookmark);
        nav.NavigateTo(FooBar, NavigationSource.Drives);
        int raised = 0;
        nav.CurrentChanged += (_, _) => raised++;

        bool followed = nav.RewritePaths(Foo, Baz);

        Assert.True(followed);
        Assert.Equal(1, raised);
        Assert.Equal(@"C:\baz\bar", nav.Current);
        Assert.Equal(NavigationSource.Drives, nav.CurrentSource);
        nav.GoBack();
        Assert.Equal(Baz, nav.Current);
        Assert.Equal(NavigationSource.Bookmark, nav.CurrentSource);
    }

    [Fact]
    public void RewritePaths_LeavesTheRest_AndStaysQuietWhenTheCurrentEntryIsNotInvolved() {
        var nav = new NavigationService();
        nav.NavigateTo(Foo);
        nav.NavigateTo(Bar);
        int raised = 0;
        nav.CurrentChanged += (_, _) => raised++;

        bool followed = nav.RewritePaths(Foo, Baz);

        Assert.False(followed);
        Assert.Equal(0, raised);
        Assert.Equal(Bar, nav.Current);
        nav.GoBack();
        Assert.Equal(Baz, nav.Current);
    }
}
