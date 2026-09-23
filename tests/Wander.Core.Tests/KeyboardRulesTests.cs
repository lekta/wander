using Wander.Core.Layout;
using Wander.Core.Navigation;
using Wander.Core.Panels;
using Wander.Core.Workspace;

namespace Wander.Core.Tests;

/// <summary>
/// Where the keyboard goes (REDESIGN 4.5, module 4; table 4.10, K rows for
/// the panels and the window). The list's rows - K-1, K-2, K-4...K-7, K-11,
/// K-12 - come with the list's model (ListingArrival).
/// </summary>
public class KeyboardRulesTests {
    private const string A = @"C:\A";
    private const string B = @"C:\A\B";
    private const string C = @"C:\A\B\C";
    private const string D = @"C:\A\D";
    private const string E = @"C:\E";
    private const string Photos = @"D:\Photos";


    // --- A line taken from under the keyboard ------------------------------------

    /// <summary>K-10: the line with the keyboard left the panel - the cursor is on its neighbour, and the keyboard goes onto it.</summary>
    [Fact]
    public void K10_TheLineWithTheKeyboardGoes_TheKeyboardGoesOntoTheNeighbour() {
        var s = Scene().Start().Navigate(E).Chevron(Pane.Drives, A, open: true)
            .Enter(WindowZone.Drives, ZoneReason.Click).Click(Pane.Drives, B);

        s.Delete(B).Post(new Removed(new[] { B })).Settle();
        Assert.Equal(D, s.Drives.Caret);

        s.Enter(null, ZoneReason.FocusFell);

        Assert.Equal(new FocusRow(WindowZone.Drives, D), Assert.Single(s.Effects.OfType<FocusRow>()));
    }

    /// <summary>K-10: nothing left in the panel to stand on - the keyboard goes to the list.</summary>
    [Fact]
    public void K10_NothingLeftInThePanel_TheKeyboardGoesToTheList() {
        var s = Scene().Start().Navigate(A).SetBookmarks(Bookmark(Photos)).Enter(WindowZone.Bookmarks, ZoneReason.Tab);

        s.SetBookmarks();
        Assert.Null(s.Bookmarks.Caret);

        s.Enter(null, ZoneReason.FocusFell);

        Assert.Equal(new FocusZone(WindowZone.FileList, ZoneReason.Programmatic), Assert.Single(s.Effects.OfType<FocusZone>()));
    }

    /// <summary>
    /// K-7: the keyboard fell out of the list's row - the row rebuilt,
    /// replaced - and goes back onto the caret, nothing scrolling; not into
    /// a panel.
    /// </summary>
    [Fact]
    public void K07_FallingOutOfTheListsRow_PutsTheKeyboardBackOnTheCaret() {
        var s = Scene().Start().Navigate(A).Enter(WindowZone.FileList).Select(@"C:\A\f.txt");

        s.Enter(WindowZone.FileList, ZoneReason.FocusFell);

        Assert.Equal(new FocusRow(WindowZone.FileList, @"C:\A\f.txt", Scroll: false), Assert.Single(s.Effects.OfType<FocusRow>()));
    }

    /// <summary>Fallen out of a list with no caret, the keyboard goes back into the list itself.</summary>
    [Fact]
    public void FallingOutOfTheList_WithNoCaret_PutsTheKeyboardBackInTheList() {
        var s = Scene().Start().Navigate(A).Enter(WindowZone.FileList);

        s.Enter(null, ZoneReason.FocusFell);

        Assert.Empty(s.Effects.OfType<FocusRow>());
        Assert.Equal(new FocusZone(WindowZone.FileList, ZoneReason.Programmatic), Assert.Single(s.Effects.OfType<FocusZone>()));
    }


    // --- Zones going away, dialogs, menus ------------------------------------------

    /// <summary>K-8: the folders pane put away with the keyboard in it - the keyboard goes to the list.</summary>
    [Fact]
    public void K08_ThePaneHiddenWithTheKeyboardInIt_SendsItToTheList() {
        var s = Scene().Start().Navigate(A).Enter(WindowZone.Drives);

        s.Post(new PaneHidden(new[] { WindowZone.Bookmarks, WindowZone.Drives }));

        Assert.Equal(new FocusZone(WindowZone.FileList, ZoneReason.Programmatic), Assert.Single(s.Effects.OfType<FocusZone>()));
    }

    /// <summary>K-8: put away with the keyboard elsewhere - nothing moves.</summary>
    [Fact]
    public void K08_ThePaneHiddenWithTheKeyboardElsewhere_MovesNothing() {
        var s = Scene().Start().Navigate(A).Enter(WindowZone.FileList);

        s.Post(new PaneHidden(new[] { WindowZone.Bookmarks, WindowZone.Drives }));

        Assert.Empty(s.Effects);
    }

    /// <summary>
    /// K-3: a dialog closed - the keyboard goes back to the panel it was in,
    /// whatever WPF did with it meanwhile; the cursor there does not move.
    /// </summary>
    [Fact]
    public void K03_ADialogClosed_TheKeyboardGoesBackToThePanel() {
        var s = Scene().Start().Navigate(A).Enter(WindowZone.Drives).Key(Pane.Drives, PanelKey.Down);

        s.Post(new DialogOpened()).Enter(WindowZone.Toolbar, ZoneReason.Activation).Post(new DialogClosed());

        Assert.Equal(new FocusZone(WindowZone.Drives, ZoneReason.DialogReturn), Assert.Single(s.Effects.OfType<FocusZone>()));
        Assert.Null(s.State.Keyboard.BeforeDialog);
        s.Enter(WindowZone.Drives, ZoneReason.DialogReturn);
        Assert.Equal(E, s.Drives.Caret);
    }

    /// <summary>K-3: from the list, or from anywhere that is not a panel, the keyboard goes to the list.</summary>
    [Theory]
    [InlineData(WindowZone.FileList)]
    [InlineData(WindowZone.Address)]
    [InlineData(WindowZone.Search)]
    public void K03_ADialogClosed_ElsewhereTheKeyboardGoesToTheList(WindowZone zone) {
        var s = Scene().Start().Navigate(A).Enter(zone);

        s.Post(new DialogOpened()).Post(new DialogClosed());

        Assert.Equal(new FocusZone(WindowZone.FileList, ZoneReason.DialogReturn), Assert.Single(s.Effects.OfType<FocusZone>()));
    }

    /// <summary>K-9: a menu closed - WPF puts the keyboard back, and nothing is asked; the target is the zone's again.</summary>
    [Fact]
    public void K09_AMenuClosed_MovesNothing() {
        var s = Scene().Start().Navigate(A).Enter(WindowZone.Drives).Key(Pane.Drives, PanelKey.Down);
        var menu = MenuContext.For(Target.OfPanelRow(Pane.Drives, A), PlaceFacts.Ordinary, false, false);

        s.Post(new MenuOpened(menu)).Enter(null, ZoneReason.Unknown).Post(new MenuClosed(menu));
        s.Enter(WindowZone.Drives, ZoneReason.MenuReturn);

        Assert.Empty(s.Effects.OfType<FocusRow>());
        Assert.Empty(s.Effects.OfType<FocusZone>());
        Assert.Equal(E, s.Target.Folder);
    }


    // --- The keyboard on the panel's cursor ----------------------------------------------

    /// <summary>A key moves the cursor of the panel with the keyboard: the keyboard goes onto the line.</summary>
    [Fact]
    public void AKeyMovingTheCursor_TakesTheKeyboardAlong() {
        var s = Scene().Start().Navigate(A).Enter(WindowZone.Drives);

        s.Key(Pane.Drives, PanelKey.Down);

        Assert.Equal(new FocusRow(WindowZone.Drives, E), Assert.Single(s.Effects.OfType<FocusRow>()));
    }

    /// <summary>A key that only opens or closes the row moves no keyboard.</summary>
    [Fact]
    public void AKeyOpeningTheRow_MovesNoKeyboard() {
        var s = Scene().Start().Navigate(E).Enter(WindowZone.Drives).Post(new CaretMoved(Pane.Drives, A)).Settle();

        s.Key(Pane.Drives, PanelKey.Right);

        Assert.True(s.Drives.IsExpanded(A));
        Assert.Empty(s.Effects.OfType<FocusRow>());
    }

    /// <summary>P-7 with the keyboard: Tab into the panel - the keyboard onto the cursor, drawn once its branch is read.</summary>
    [Fact]
    public void TabIntoThePanel_PutsTheKeyboardOnTheCursor() {
        var s = Scene().Start().Navigate(B).Enter(WindowZone.FileList).Chevron(Pane.Drives, A, open: false);

        s.Enter(WindowZone.Drives, ZoneReason.Tab);

        Assert.Equal(new FocusRow(WindowZone.Drives, B), Assert.Single(s.Effects.OfType<FocusRow>()));
    }

    /// <summary>A cursor moved while the keyboard is elsewhere - a folder opened from the list - moves no keyboard.</summary>
    [Fact]
    public void TheCursorMovedWithTheKeyboardElsewhere_MovesNoKeyboard() {
        var s = Scene().Start().Navigate(A).Enter(WindowZone.FileList);

        s.Navigate(B);

        Assert.Equal(B, s.Drives.Caret);
        Assert.Empty(s.Effects.OfType<FocusRow>());
    }

    /// <summary>The application putting the keyboard in a panel moves no cursor and opens no branch - it said where the cursor is.</summary>
    [Fact]
    public void AProgrammaticArrival_MovesNoCursor() {
        var s = Scene().Start().Navigate(B).Enter(WindowZone.FileList).Chevron(Pane.Drives, A, open: false);

        s.Enter(WindowZone.Drives, ZoneReason.Programmatic);

        Assert.False(s.Drives.IsExpanded(A));
        Assert.Empty(s.Effects);
    }


    // --- Helpers -------------------------------------------------------------------------

    private static WorkspaceScene Scene() {
        return new WorkspaceScene(C, D, E, @"D:\Photos\2026");
    }

    private static PanelRow Bookmark(string path) {
        return WorkspaceScene.Bookmark(path);
    }
}
