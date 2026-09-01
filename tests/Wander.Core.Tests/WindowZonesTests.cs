using Wander.Core.Layout;

namespace Wander.Core.Tests;

public class WindowZonesTests {

    // --- The Tab ring ----------------------------------------------------

    [Fact]
    public void Tab_TriesTheNextZoneFirst() {
        Assert.Equal(WindowZone.Address, WindowZones.Ring(WindowZone.Toolbar, 1).First());
    }

    [Fact]
    public void ShiftTab_TriesThePreviousZoneFirst() {
        Assert.Equal(WindowZone.Toolbar, WindowZones.Ring(WindowZone.Address, -1).First());
    }

    /// <summary>
    /// Every zone is offered before the ring closes, because any of them
    /// can refuse: bookmarks collapsed, every toolbar button disabled.
    /// </summary>
    [Fact]
    public void TheRing_OffersEveryZoneOnce() {
        var ring = WindowZones.Ring(WindowZone.Search, 1).ToList();

        Assert.Equal(WindowZones.Order.Count, ring.Count);
        Assert.Equal(WindowZones.Order.OrderBy(z => z), ring.OrderBy(z => z));
    }

    /// <summary>
    /// The zone Tab started from is the last resort, not a stop that gets
    /// skipped: with every other zone refusing, the keyboard stays put
    /// rather than falling out of the window.
    /// </summary>
    [Fact]
    public void TheRing_ComesBackToWhereItStarted() {
        Assert.Equal(WindowZone.Search, WindowZones.Ring(WindowZone.Search, 1).Last());
        Assert.Equal(WindowZone.Search, WindowZones.Ring(WindowZone.Search, -1).Last());
    }

    [Fact]
    public void TheRing_WrapsPastTheLastZone() {
        Assert.Equal(WindowZone.Toolbar, WindowZones.Ring(WindowZone.FileList, 1).First());
    }

    [Fact]
    public void TheRing_WrapsPastTheFirstZone() {
        Assert.Equal(WindowZone.FileList, WindowZones.Ring(WindowZone.Toolbar, -1).First());
    }


    // --- Which folder panel Ctrl+1 opens ---------------------------------

    private static WindowZone Pane(
        bool toggle,
        WindowZone? focused = null,
        WindowZone? owner = null,
        WindowZone last = WindowZone.Drives,
        bool hasBookmarks = true) {
        return WindowZones.FolderPane(toggle, focused, owner, last, hasBookmarks);
    }

    /// <summary>Ctrl+Shift+E: always the panel the folder was opened from.</summary>
    [Fact]
    public void WithoutToggle_TheOwningPanelWins() {
        Assert.Equal(WindowZone.Bookmarks, Pane(toggle: false, owner: WindowZone.Bookmarks));
        Assert.Equal(WindowZone.Drives, Pane(toggle: false, owner: WindowZone.Drives));
    }

    /// <summary>
    /// A folder opened from the address bar belongs to neither panel; the
    /// drives tree holds every path, so it is the one that can show it.
    /// </summary>
    [Fact]
    public void WithoutToggle_AndNoOwner_FallsBackToDrives() {
        Assert.Equal(WindowZone.Drives, Pane(toggle: false, owner: null, last: WindowZone.Bookmarks));
    }

    [Fact]
    public void Toggle_FromOnePanel_SwapsToTheOther() {
        Assert.Equal(WindowZone.Drives, Pane(toggle: true, focused: WindowZone.Bookmarks));
        Assert.Equal(WindowZone.Bookmarks, Pane(toggle: true, focused: WindowZone.Drives));
    }

    /// <summary>The swap is about where the keyboard is, not where the folder came from.</summary>
    [Fact]
    public void Toggle_FromOnePanel_IgnoresTheOwner() {
        Assert.Equal(
            WindowZone.Drives,
            Pane(toggle: true, focused: WindowZone.Bookmarks, owner: WindowZone.Bookmarks));
    }

    [Fact]
    public void Toggle_FromElsewhere_OpensTheOwningPanel() {
        Assert.Equal(
            WindowZone.Bookmarks,
            Pane(toggle: true, focused: WindowZone.FileList, owner: WindowZone.Bookmarks, last: WindowZone.Drives));
    }

    /// <summary>
    /// From the list, into a folder that came from neither panel: the last
    /// panel the keyboard was in is the only thing left that knows anything
    /// about the user's habit.
    /// </summary>
    [Fact]
    public void Toggle_FromElsewhere_AndNoOwner_UsesTheLastPanel() {
        Assert.Equal(
            WindowZone.Bookmarks,
            Pane(toggle: true, focused: WindowZone.FileList, owner: null, last: WindowZone.Bookmarks));
    }

    /// <summary>
    /// Every default bookmark switched off and none added: an empty panel
    /// is not somewhere to send the keyboard, whichever way it was asked
    /// for.
    /// </summary>
    [Fact]
    public void AnEmptyBookmarksPanel_IsNeverTheTarget() {
        Assert.Equal(
            WindowZone.Drives,
            Pane(toggle: false, owner: WindowZone.Bookmarks, hasBookmarks: false));
        Assert.Equal(
            WindowZone.Drives,
            Pane(toggle: true, focused: WindowZone.Drives, hasBookmarks: false));
        Assert.Equal(
            WindowZone.Drives,
            Pane(toggle: true, focused: WindowZone.FileList, last: WindowZone.Bookmarks, hasBookmarks: false));
    }
}
