using Wander.Core.Layout;
using Wander.Core.Navigation;
using Wander.Core.Panels;
using Wander.Core.Workspace;

namespace Wander.Core.Tests;

/// <summary>
/// The folder panels as a model (REDESIGN 4.5, modules 1-2; table 4.10
/// P-1...P-24). Everything here runs without a window: the events a view
/// would post go in, the state and what the application is asked to do
/// come out, and the disk is a folder tree answering the reads.
/// </summary>
public class PanelRulesTests {
    private const string A = @"C:\A";
    private const string B = @"C:\A\B";
    private const string C = @"C:\A\B\C";
    private const string D = @"C:\A\D";
    private const string E = @"C:\E";
    private const string Photos = @"D:\Photos";
    private const string Year = @"D:\Photos\2026";


    // --- Opening a folder from a panel ---------------------------------------

    /// <summary>P-1.</summary>
    [Fact]
    public void P01_AClickOpensTheRow_AndTheCursorStaysOnIt() {
        var s = Scene().Start().Navigate(E);
        s.Enter(WindowZone.Drives, ZoneReason.Click).Click(Pane.Drives, A);

        var navigate = Assert.Single(s.Effects.OfType<Navigate>());
        Assert.Equal(A, navigate.Path);
        Assert.Equal(NavigationSource.Drives, navigate.Source);

        s.Navigate(A);
        Assert.Equal(A, s.Drives.Location);
        Assert.Equal(A, s.Drives.Caret);
        Assert.Equal(TargetKind.PanelRow, s.Target.Kind);
        Assert.Equal(A, s.Target.Folder);
    }

    /// <summary>P-2: a click on the open folder's row opens nothing; the row is the target.</summary>
    [Fact]
    public void P02_AClickOnTheOpenFolder_OpensNothing() {
        var s = Scene().Start().Navigate(A);

        s.Enter(WindowZone.Drives, ZoneReason.Click).Click(Pane.Drives, A);

        Assert.Empty(s.Effects.OfType<Navigate>());
        Assert.Equal(A, s.Target.Folder);
    }

    /// <summary>P-3: the arrows move the cursor and open nothing; the cursor is the target and the active highlight.</summary>
    [Fact]
    public void P03_ArrowsMoveTheCursor_WithoutOpening() {
        var s = Scene().Start().Navigate(A).Enter(WindowZone.Drives);

        s.Key(Pane.Drives, PanelKey.Down);

        Assert.Equal(E, s.Drives.Caret);
        Assert.Empty(s.Effects.OfType<Navigate>());
        Assert.Equal(A, s.State.Folder.Path);
        Assert.Equal(E, s.Target.Folder);
        Assert.Equal(new PanelHighlight(E, Active: true), s.State.Highlight(Pane.Drives));
    }

    /// <summary>P-4: with the arrows opening folders, a lone press opens its row at once.</summary>
    [Fact]
    public void P04_ArrowsOpen_ALonePressOpensAtOnce() {
        var s = Scene().Options(arrowsOpen: true).Start().Navigate(A).Enter(WindowZone.Drives);

        s.Advance(1000).Key(Pane.Drives, PanelKey.Down);

        Assert.Equal(E, Assert.Single(s.Effects.OfType<Navigate>()).Path);
        Assert.Empty(s.Effects.OfType<ScheduleThrottle>());
    }

    /// <summary>
    /// P-4: a burst on the heels of a navigation opens one row - the one the
    /// cursor rested on - once it has rested.
    /// </summary>
    [Fact]
    public void P04_ArrowsOpen_ABurstOpensWhereTheCursorRests() {
        var s = Scene(@"C:\F", @"C:\G").Options(arrowsOpen: true).Start().Navigate(A).Enter(WindowZone.Drives);
        s.Advance(1000).Key(Pane.Drives, PanelKey.Down).Navigate(E);

        s.Advance(100).Key(Pane.Drives, PanelKey.Down);
        Assert.Empty(s.Effects.OfType<Navigate>());
        Assert.Equal(s.Now + TreeNavThrottle.SettleMs, Assert.Single(s.Effects.OfType<ScheduleThrottle>()).AtMs);

        s.Advance(50).Key(Pane.Drives, PanelKey.Down);
        long due = s.Now + TreeNavThrottle.SettleMs;

        s.Post(new ThrottleElapsed(due - 10));
        Assert.Empty(s.Effects.OfType<Navigate>());

        s.Post(new ThrottleElapsed(due));
        Assert.Equal(@"C:\G", Assert.Single(s.Effects.OfType<Navigate>()).Path);
    }

    /// <summary>P-4: the keyboard left the panel mid-burst - the row it was resting on is not opened.</summary>
    [Fact]
    public void P04_ArrowsOpen_LeavingThePanelMidBurst_OpensNothing() {
        var s = Scene(@"C:\F").Options(arrowsOpen: true).Start().Navigate(A).Enter(WindowZone.Drives);
        s.Advance(1000).Key(Pane.Drives, PanelKey.Down).Navigate(E);
        s.Advance(100).Key(Pane.Drives, PanelKey.Down);
        long due = s.Now + TreeNavThrottle.SettleMs;

        s.Enter(WindowZone.FileList).Post(new ThrottleElapsed(due));

        Assert.Empty(s.Effects.OfType<Navigate>());
        Assert.Null(s.State.PendingNavigation);
    }

    /// <summary>P-4: a click cancels whatever a burst still holds.</summary>
    [Fact]
    public void P04_AClickCancelsAPendingBurst() {
        var s = Scene(@"C:\F").Options(arrowsOpen: true).Start().Navigate(A).Enter(WindowZone.Drives);
        s.Advance(1000).Key(Pane.Drives, PanelKey.Down).Navigate(E);
        s.Advance(100).Key(Pane.Drives, PanelKey.Down);

        s.Click(Pane.Drives, A);

        Assert.Null(s.State.PendingNavigation);
    }

    /// <summary>P-5: Enter opens the row under the cursor.</summary>
    [Fact]
    public void P05_EnterOpensTheCursorRow() {
        var s = Scene().Start().Navigate(A).Enter(WindowZone.Drives).Key(Pane.Drives, PanelKey.Down);

        s.Post(new RowActivated(Pane.Drives, s.Now));

        Assert.Equal(E, Assert.Single(s.Effects.OfType<Navigate>()).Path);
    }


    // --- Opening and closing branches ----------------------------------------

    /// <summary>
    /// P-6: a chevron closes a branch over the open folder. The folder stays
    /// open, its place stays (hidden), the cursor does not move, nothing is
    /// read or opened.
    /// </summary>
    [Fact]
    public void P06_ClosingABranchOverTheOpenFolder_MovesNothing() {
        var s = Scene().Start().Navigate(B).Enter(WindowZone.FileList);

        s.Chevron(Pane.Drives, A, open: false);

        Assert.Equal(B, s.State.Folder.Path);
        Assert.Equal(B, s.Drives.Location);
        Assert.Equal(B, s.Drives.Caret);
        Assert.False(s.Shows(Pane.Drives, B));
        Assert.Empty(s.Effects);
        Assert.Equal(WindowZone.FileList, s.State.Keyboard.Zone);
    }

    /// <summary>P-7: Tab into the panel: the branch the cursor is hidden in opens, the cursor on it.</summary>
    [Fact]
    public void P07_TabIntoThePanel_OpensTheBranchTheCursorIsHiddenIn() {
        var s = Scene().Start().Navigate(B).Enter(WindowZone.FileList).Chevron(Pane.Drives, A, open: false);

        s.Enter(WindowZone.Drives, ZoneReason.Tab);

        Assert.True(s.Shows(Pane.Drives, B));
        Assert.Equal(B, s.Drives.Caret);
    }

    /// <summary>P-8, decision B5: Tab into a panel with no cursor - the first row, and nothing opened, arrows-open or not.</summary>
    [Fact]
    public void P08_TabIntoAPanelWithNoCursor_LandsOnTheFirstRow() {
        var s = Scene().Options(arrowsOpen: true).Start().Navigate(A).SetBookmarks(Bookmark(Photos), Bookmark(E));

        s.Enter(WindowZone.Bookmarks, ZoneReason.Tab);

        Assert.Equal(Photos, s.Bookmarks.Caret);
        Assert.Empty(s.Effects.OfType<Navigate>());
    }

    /// <summary>P-9: Alt with the chevron opens the children, one level; closed with Alt, everything below closes.</summary>
    [Fact]
    public void P09_AltWithTheChevron_OpensChildren_AndClosesDescendants() {
        var s = Scene().Start().Navigate(E);

        s.Chevron(Pane.Drives, A, open: true, all: true);
        Assert.True(s.Drives.IsExpanded(A));
        Assert.True(s.Drives.IsExpanded(B));
        Assert.True(s.Shows(Pane.Drives, C));

        s.Chevron(Pane.Drives, A, open: false, all: true);
        Assert.False(s.Drives.IsExpanded(A));
        Assert.False(s.Drives.IsExpanded(B));
        Assert.Equal(E, s.Drives.Caret);
    }


    // --- Where the open folder is ----------------------------------------------

    /// <summary>
    /// P-10: a folder opened from the bookmarks has its place there, going
    /// deeper too; one they cannot reach goes to the drives, and the
    /// bookmarks keep their cursor (P-21 since 2026-09-23).
    /// </summary>
    [Fact]
    public void P10_ThePlaceIsInThePanelTheFolderCameFrom() {
        var s = Scene().Start().SetBookmarks(Bookmark(Photos));

        s.Navigate(Photos, NavigationSource.Bookmark).Navigate(Year, NavigationSource.Bookmark);
        Assert.Equal(Year, s.Bookmarks.Location);
        Assert.Equal(Year, s.Bookmarks.Caret);
        Assert.True(s.Shows(Pane.Bookmarks, Year));
        Assert.Null(s.Drives.Location);

        s.Navigate(E, NavigationSource.Bookmark);
        Assert.Equal(E, s.Drives.Location);
        Assert.Null(s.Bookmarks.Location);
        Assert.Equal(Year, s.Bookmarks.Caret);
    }

    /// <summary>P-11: back and forward put the place in the panel of the history record.</summary>
    [Fact]
    public void P11_BackPutsThePlaceInTheRecordsPanel() {
        var s = Scene().Start().SetBookmarks(Bookmark(Photos)).Navigate(A).Navigate(Photos, NavigationSource.Bookmark);

        s.Navigate(A, NavigationSource.Drives, NavigationKind.Back);

        Assert.Equal(A, s.Drives.Location);
        Assert.Null(s.Bookmarks.Location);
        Assert.Equal(Photos, s.Bookmarks.Caret);
    }

    /// <summary>P-12: the open folder moved; its place follows in the same panel, and the drives do not open by themselves.</summary>
    [Fact]
    public void P12_TheOpenFolderMoved_ThePlaceFollowsInItsPanel() {
        var s = Scene().Start().SetBookmarks(Bookmark(Photos)).Navigate(Year, NavigationSource.Bookmark);
        var drivesOpen = s.Drives.Expanded;

        s.Delete(Year).Add(@"D:\Photos\2027");
        s.Post(new Relocated(Year, @"D:\Photos\2027")).Settle();
        s.Navigate(@"D:\Photos\2027", NavigationSource.Bookmark, NavigationKind.Rewrite);

        Assert.Equal(@"D:\Photos\2027", s.Bookmarks.Location);
        Assert.Equal(@"D:\Photos\2027", s.Bookmarks.Caret);
        Assert.Equal(drivesOpen, s.Drives.Expanded);
    }

    /// <summary>
    /// P-12 into another folder: the row leaves the level it was in, the place
    /// and the cursor go with it, and it stays open - also when the level it
    /// left is read again after the navigation, as the application answers.
    /// </summary>
    [Fact]
    public void P12_TheOpenFolderMovedIntoAnotherFolder_TheCursorFollows() {
        const string moved = @"C:\E\B";
        var s = Scene().Start().Navigate(B).Chevron(Pane.Drives, B, open: true).Enter(WindowZone.Drives);
        s.Delete(B).Add(@"C:\E\B\C");

        s.Post(new Relocated(B, moved)).Post(new FolderChanged(E)).Post(new FolderChanged(A));
        var late = Assert.Single(s.Effects.OfType<ReadBranch>());
        s.Navigate(moved, NavigationSource.Drives, NavigationKind.Rewrite).Answer(late);

        Assert.Equal(moved, s.Drives.Location);
        Assert.Equal(moved, s.Drives.Caret);
        Assert.True(s.Shows(Pane.Drives, moved));
        Assert.False(s.Shows(Pane.Drives, B));
        Assert.True(s.Drives.IsExpanded(moved));
        Assert.True(s.Shows(Pane.Drives, @"C:\E\B\C"));
    }

    /// <summary>P-13: the open folder deleted from its panel: the parent opens, and it is the cursor and the target.</summary>
    [Fact]
    public void P13_TheOpenFolderDeleted_TheParentIsTheCursor() {
        var s = Scene().Start().Navigate(B).Enter(WindowZone.Drives);

        s.Delete(B).Post(new Removed(new[] { B })).Settle().Navigate(A);

        Assert.Equal(A, s.Drives.Location);
        Assert.Equal(A, s.Drives.Caret);
        Assert.Equal(A, s.Target.Folder);
        Assert.False(s.Shows(Pane.Drives, B));
    }

    /// <summary>P-14: a row gone from under the cursor - the next row of its level, else the one before, else the row above; the open folder does not change.</summary>
    [Fact]
    public void P14_ARowGoneFromUnderTheCursor_TheCursorGoesToANeighbour() {
        var s = Scene(@"C:\A\F").Start().Navigate(A).Chevron(Pane.Drives, A, open: true).Enter(WindowZone.Drives);
        s.Key(Pane.Drives, PanelKey.Down);
        Assert.Equal(B, s.Drives.Caret);

        s.Delete(B).Post(new Removed(new[] { B })).Settle();
        Assert.Equal(D, s.Drives.Caret);
        Assert.Equal(A, s.State.Folder.Path);

        s.Key(Pane.Drives, PanelKey.Down);
        Assert.Equal(@"C:\A\F", s.Drives.Caret);
        s.Delete(@"C:\A\F").Post(new Removed(new[] { @"C:\A\F" })).Settle();
        Assert.Equal(D, s.Drives.Caret);

        s.Delete(D).Post(new Removed(new[] { D })).Settle();
        Assert.Equal(A, s.Drives.Caret);
    }


    // --- The bookmarks built again ---------------------------------------------

    /// <summary>P-15, N7: the bookmarks built again keep what was open, the cursor and the place, by path.</summary>
    [Fact]
    public void P15_BookmarksBuiltAgain_KeepWhatWasOpenAndTheCursor() {
        var s = Scene().Start().SetBookmarks(Bookmark(Photos)).Navigate(Year, NavigationSource.Bookmark);

        s.SetBookmarks(Bookmark(Photos), Bookmark(E));

        Assert.Equal(Year, s.Bookmarks.Location);
        Assert.Equal(Year, s.Bookmarks.Caret);
        Assert.True(s.Shows(Pane.Bookmarks, Year));
    }

    /// <summary>P-16: a bookmark moved up keeps the cursor on it.</summary>
    [Fact]
    public void P16_ABookmarkMoved_TheCursorGoesWithIt() {
        var s = Scene().Start().SetBookmarks(Bookmark(Photos), Bookmark(E)).Enter(WindowZone.Bookmarks);
        s.Key(Pane.Bookmarks, PanelKey.End);
        Assert.Equal(E, s.Bookmarks.Caret);

        s.SetBookmarks(Bookmark(E), Bookmark(Photos));

        Assert.Equal(E, s.Bookmarks.Caret);
        Assert.Equal(new[] { E, Photos }, s.Lines(Pane.Bookmarks));
    }

    /// <summary>A bookmark removed from under the cursor: its neighbour takes it.</summary>
    [Fact]
    public void ABookmarkRemoved_TheCursorGoesToItsNeighbour() {
        var s = Scene().Start().SetBookmarks(Bookmark(Photos), Bookmark(E)).Enter(WindowZone.Bookmarks);

        s.SetBookmarks(Bookmark(E));

        Assert.Equal(E, s.Bookmarks.Caret);
    }


    // --- Renaming in a panel -------------------------------------------------------

    /// <summary>P-17: F2 then Enter - the row is under its new name in place, still open, the cursor on it.</summary>
    [Fact]
    public void P17_ARenamedRowStaysInPlace_OpenAndUnderTheCursor() {
        var s = Scene().Start().Navigate(B).Chevron(Pane.Drives, B, open: true);
        s.Post(new EditRequested(Pane.Drives, B));
        Assert.Equal(B, s.Drives.Editing);

        s.Delete(B).Add(@"C:\A\B2\C");
        s.Post(new Relocated(B, @"C:\A\B2")).Post(new EditEnded(Pane.Drives)).Post(new FolderChanged(A)).Settle();

        Assert.Null(s.Drives.Editing);
        Assert.Equal(@"C:\A\B2", s.Drives.Caret);
        Assert.True(s.Drives.IsExpanded(@"C:\A\B2"));
        Assert.True(s.Shows(Pane.Drives, @"C:\A\B2\C"));
        var lines = s.Lines(Pane.Drives).ToList();
        Assert.Equal(lines.IndexOf(A) + 1, lines.IndexOf(@"C:\A\B2"));
        Assert.Equal("B2", s.Drives.Find(@"C:\A\B2")!.Name);
    }

    [Fact]
    public void EditRequested_OnADrive_IsRefused() {
        var s = Scene().Start();

        s.Post(new EditRequested(Pane.Drives, @"C:\"));

        Assert.Null(s.Drives.Editing);
    }

    /// <summary>A built-in bookmark keeps its label through a rename of its folder: the label is Windows's name for it.</summary>
    [Fact]
    public void ABuiltInBookmark_KeepsItsLabelThroughARename() {
        var s = Scene().Start().SetBookmarks(new PanelRow(Photos, "Pictures", PanelRowKind.Folder) { Role = PanelRowRole.BuiltInBookmark });

        s.Post(new Relocated(Photos, @"D:\Pics"));

        Assert.Equal("Pictures", s.Bookmarks.Top[0].Name);
        Assert.Equal(@"D:\Pics", s.Bookmarks.Top[0].Path);
    }


    // --- Reading levels ---------------------------------------------------------------

    /// <summary>P-18: a branch closed before its level came back drops the answer.</summary>
    [Fact]
    public void P18_AnAnswerForAClosedBranch_IsDropped() {
        var s = Scene().Start().Navigate(E);

        s.Post(new ChevronToggled(Pane.Drives, A, true, false));
        var read = Assert.Single(s.Effects.OfType<ReadBranch>());
        s.Post(new ChevronToggled(Pane.Drives, A, false, false));
        s.Post(new BranchRead(Pane.Drives, A, new[] { new PanelRow(B, "B", PanelRowKind.Folder) }, read.Epoch));

        Assert.Equal(LevelState.Unread, s.Drives.LevelOf(A).State);
        Assert.False(s.Shows(Pane.Drives, B));
    }

    /// <summary>An answer to a question asked again is dropped too: only the latest read of a level counts.</summary>
    [Fact]
    public void AnOlderAnswer_IsDropped() {
        var s = Scene().Start().Navigate(A);
        s.Post(new FolderChanged(@"C:\"));
        var first = Assert.Single(s.Effects.OfType<ReadBranch>());
        s.Post(new FolderChanged(@"C:\"));
        var second = Assert.Single(s.Effects.OfType<ReadBranch>());

        s.Post(new BranchRead(Pane.Drives, @"C:\", Array.Empty<PanelRow>(), first.Epoch));

        Assert.Equal(LevelState.Reading, s.Drives.LevelOf(@"C:\").State);
        Assert.NotEqual(first.Epoch, second.Epoch);
        Assert.True(s.Shows(Pane.Drives, A));
    }

    /// <summary>P-18: the chevron of a row never opened is asked for again when its folder changes.</summary>
    [Fact]
    public void P18_AClosedRowsChevron_IsAskedForAgain() {
        var s = Scene().Start().Navigate(A);

        s.Post(new FolderChanged(E));

        Assert.Equal(new[] { E }, Assert.Single(s.Effects.OfType<ProbeChevrons>()).Paths);
    }

    /// <summary>An open level read again keeps its rows, what is open under them and the cursor - the tree never closes itself.</summary>
    [Fact]
    public void AnOpenLevelReadAgain_KeepsEverything() {
        var s = Scene().Start().Navigate(C);
        var before = s.Lines(Pane.Drives);

        s.Add(@"C:\A\B\X").Post(new FolderChanged(B)).Settle();

        Assert.Equal(C, s.Drives.Caret);
        Assert.True(s.Drives.IsExpanded(A));
        Assert.True(s.Drives.IsExpanded(B));
        Assert.Equal(before.Count + 1, s.Lines(Pane.Drives).Count);
    }

    /// <summary>An empty folder that got its first subfolder gets its chevron back; one that lost its last loses it.</summary>
    [Fact]
    public void Chevrons_FollowWhatTheFolderHolds() {
        var s = Scene().Start().Navigate(A).Chevron(Pane.Drives, A, open: true);
        Assert.False(s.Drives.Find(D)!.HasChevron);

        s.Add(@"C:\A\D\New").Post(new FolderChanged(D)).Settle();

        Assert.True(s.Drives.Find(D)!.HasChevron);
    }

    /// <summary>Branches saved open come back open, their levels read.</summary>
    [Fact]
    public void Start_OpensTheBranchesSavedOpen() {
        var s = Scene().Start(new NavigationStop(B, NavigationSource.Drives));

        Assert.True(s.Shows(Pane.Drives, C));
        Assert.Equal(LevelState.Loaded, s.Drives.LevelOf(B).State);
    }

    /// <summary>P-19: hidden folders switched off with the cursor on one - the cursor goes to the row above.</summary>
    [Fact]
    public void P19_HiddenSwitchedOff_TheCursorLeavesAHiddenRow() {
        var s = Scene().Add(@"C:\A\Hidden", hidden: true).Options(showHidden: true).Start().Navigate(A)
            .Chevron(Pane.Drives, A, open: true).Enter(WindowZone.Drives);
        s.Key(Pane.Drives, PanelKey.Down).Key(Pane.Drives, PanelKey.Down).Key(Pane.Drives, PanelKey.Down);
        Assert.Equal(@"C:\A\Hidden", s.Drives.Caret);

        s.Options(showHidden: false);

        Assert.Equal(A, s.Drives.Caret);
    }


    // --- One highlight per panel -------------------------------------------------------

    /// <summary>
    /// P-20: arrows in the drives onto a row, then a click on a bookmark:
    /// the bookmark is lit, the drives' row stays lit, inactive.
    /// </summary>
    [Fact]
    public void P20_TheDrivesKeepTheirRow_WhenABookmarkOpens() {
        var s = Scene().Start().SetBookmarks(Bookmark(Photos)).Navigate(A).Enter(WindowZone.Drives).Key(Pane.Drives, PanelKey.Down);

        s.Enter(WindowZone.Bookmarks, ZoneReason.Click).Click(Pane.Bookmarks, Photos).Navigate(Photos, NavigationSource.Bookmark);

        Assert.Equal(new PanelHighlight(Photos, Active: true), s.State.Highlight(Pane.Bookmarks));
        Assert.Equal(new PanelHighlight(E, Active: false), s.State.Highlight(Pane.Drives));
    }

    /// <summary>
    /// P-21: at most one lit row a panel, at most one active in the window;
    /// each panel keeps its row whichever panel the next folder is opened
    /// from (both ways since 2026-09-23).
    /// </summary>
    [Fact]
    public void P21_OneActiveHighlight_AndEachPanelItsOwn() {
        var s = Scene().Start().SetBookmarks(Bookmark(Photos));
        var steps = new Action[] {
            () => s.Navigate(A).Enter(WindowZone.Drives),
            () => s.Key(Pane.Drives, PanelKey.Down),
            () => s.Enter(WindowZone.Bookmarks, ZoneReason.Click).Click(Pane.Bookmarks, Photos).Navigate(Photos, NavigationSource.Bookmark),
            () => s.Enter(WindowZone.FileList).Navigate(Year, NavigationSource.Bookmark),
            () => s.Navigate(E, NavigationSource.RightPane),
        };

        foreach (var step in steps) {
            step();
            int active = new[] { Pane.Bookmarks, Pane.Drives }.Count(p => s.State.Highlight(p).Active);
            Assert.True(active <= 1);
        }

        Assert.Equal(Year, s.Bookmarks.Caret);
        Assert.Equal(E, s.Drives.Caret);
    }

    /// <summary>
    /// P-22: the open folder came from the bookmarks; Ctrl+1 from them into
    /// the drives lands on the row the drives hold - and with the arrows
    /// opening folders, opens it.
    /// </summary>
    [Fact]
    public void P22_Ctrl1IntoTheDrives_LandsOnTheRowTheyHold() {
        var s = Scene().Options(arrowsOpen: true).Start().SetBookmarks(Bookmark(Photos)).Navigate(A).Navigate(Photos, NavigationSource.Bookmark);
        s.Enter(WindowZone.Bookmarks);

        s.Enter(WindowZone.Drives, ZoneReason.PanelKey);

        Assert.Equal(A, s.Drives.Caret);
        Assert.Equal(A, Assert.Single(s.Effects.OfType<Navigate>()).Path);
    }

    /// <summary>
    /// P-22 the other way (2026-09-23): the open folder came from the drives;
    /// Ctrl+1 from them into the bookmarks lands on the row the bookmarks
    /// hold and, with the arrows opening folders, opens it - Ctrl+1 goes to
    /// and fro between two folders.
    /// </summary>
    [Fact]
    public void P22_Ctrl1IntoTheBookmarks_LandsOnTheRowTheyHold() {
        var s = Scene().Options(arrowsOpen: true).Start().SetBookmarks(Bookmark(Photos), Bookmark(E))
            .Navigate(Year, NavigationSource.Bookmark)
            .Enter(WindowZone.Drives, ZoneReason.Click).Click(Pane.Drives, A).Navigate(A);
        Assert.Equal(Year, s.Bookmarks.Caret);
        Assert.False(s.State.Highlight(Pane.Bookmarks).Active);

        s.Enter(WindowZone.Bookmarks, ZoneReason.PanelKey);
        Assert.Equal(Year, s.Bookmarks.Caret);
        Assert.Equal(Year, Assert.Single(s.Effects.OfType<Navigate>()).Path);

        s.Navigate(Year, NavigationSource.Bookmark).Enter(WindowZone.Drives, ZoneReason.PanelKey);
        Assert.Equal(A, Assert.Single(s.Effects.OfType<Navigate>()).Path);
    }

    /// <summary>Without the arrows opening folders, Ctrl+1 from the drives puts the cursor on the bookmarks' row and opens nothing.</summary>
    [Fact]
    public void Ctrl1IntoTheBookmarks_WithoutArrowsOpening_OnlyMovesTheCursor() {
        var s = Scene().Start().SetBookmarks(Bookmark(Photos), Bookmark(E))
            .Navigate(Year, NavigationSource.Bookmark).Navigate(A).Enter(WindowZone.Drives);

        s.Enter(WindowZone.Bookmarks, ZoneReason.PanelKey);

        Assert.Equal(Year, s.Bookmarks.Caret);
        Assert.Empty(s.Effects.OfType<Navigate>());
    }

    /// <summary>From the list Ctrl+1 only shows where the open folder is: into the bookmarks that cannot reach it, onto their row, opening nothing.</summary>
    [Fact]
    public void Ctrl1FromTheList_OpensNothing() {
        var s = Scene().Options(arrowsOpen: true).Start().SetBookmarks(Bookmark(Photos), Bookmark(E))
            .Navigate(Year, NavigationSource.Bookmark).Navigate(A).Enter(WindowZone.FileList);

        s.Enter(WindowZone.Bookmarks, ZoneReason.PanelKey);

        Assert.Equal(Year, s.Bookmarks.Caret);
        Assert.Empty(s.Effects.OfType<Navigate>());
    }

    /// <summary>P-22: the drives hold no row - Ctrl+1 opens them down to the open folder.</summary>
    [Fact]
    public void P22_Ctrl1IntoDrivesThatHoldNoRow_RevealsTheOpenFolder() {
        var s = Scene().Start().SetBookmarks(Bookmark(Photos)).Navigate(Year, NavigationSource.Bookmark);

        s.Enter(WindowZone.Drives, ZoneReason.PanelKey);

        Assert.Equal(Year, s.Drives.Caret);
        Assert.True(s.Shows(Pane.Drives, Year));
        Assert.Empty(s.Effects.OfType<Navigate>());
    }

    /// <summary>Ctrl+Shift+E always opens the panel down to the open folder, whatever it held.</summary>
    [Fact]
    public void RevealKey_AlwaysOpensDownToTheOpenFolder() {
        var s = Scene().Start().SetBookmarks(Bookmark(Photos)).Navigate(A).Navigate(Year, NavigationSource.Bookmark);

        s.Enter(WindowZone.Drives, ZoneReason.RevealKey);

        Assert.Equal(Year, s.Drives.Caret);
        Assert.True(s.Shows(Pane.Drives, Year));
    }

    /// <summary>Ctrl+1 into the bookmarks for a folder outside every bookmark: the cursor stays where it was.</summary>
    [Fact]
    public void Ctrl1IntoBookmarks_ThatCannotReachTheFolder_LeaveTheCursor() {
        var s = Scene().Start().SetBookmarks(Bookmark(Photos), Bookmark(E)).Navigate(E, NavigationSource.Bookmark).Navigate(A);

        s.Enter(WindowZone.Bookmarks, ZoneReason.PanelKey);

        Assert.Equal(E, s.Bookmarks.Caret);
        Assert.Null(s.Bookmarks.Location);
    }

    /// <summary>Ctrl+1 into bookmarks that hold no row and cannot reach the folder: the first row, nothing opened (B5).</summary>
    [Fact]
    public void Ctrl1IntoBookmarks_ThatHoldNoRow_TakesTheFirst() {
        var s = Scene().Options(arrowsOpen: true).Start().SetBookmarks(Bookmark(Photos), Bookmark(E)).Navigate(A).Enter(WindowZone.Drives);

        s.Enter(WindowZone.Bookmarks, ZoneReason.PanelKey);

        Assert.Equal(Photos, s.Bookmarks.Caret);
        Assert.Empty(s.Effects.OfType<Navigate>());
    }

    /// <summary>Back from a menu, a dialog or another window, put there by the application - or for a reason nobody gave - the keyboard moves no cursor.</summary>
    [Theory]
    [InlineData(ZoneReason.MenuReturn)]
    [InlineData(ZoneReason.DialogReturn)]
    [InlineData(ZoneReason.Activation)]
    [InlineData(ZoneReason.FocusFell)]
    [InlineData(ZoneReason.Programmatic)]
    [InlineData(ZoneReason.Unknown)]
    public void ComingBack_MovesNoCursor(ZoneReason reason) {
        var s = Scene().Start().SetBookmarks(Bookmark(Photos)).Navigate(A);

        s.Enter(WindowZone.Bookmarks, reason);

        Assert.Null(s.Bookmarks.Caret);
    }

    /// <summary>The cursor put on a row by name (type-ahead): shown if hidden, nothing opened.</summary>
    [Fact]
    public void CaretMoved_PutsTheCursorOnTheRow_AndShowsIt() {
        var s = Scene().Options(arrowsOpen: true).Start().Navigate(B).Chevron(Pane.Drives, A, open: false);

        s.Post(new CaretMoved(Pane.Drives, D)).Settle();

        Assert.Equal(D, s.Drives.Caret);
        Assert.True(s.Shows(Pane.Drives, D));
        Assert.Empty(s.Effects.OfType<Navigate>());
    }


    // --- The window coming back -----------------------------------------------------------

    /// <summary>P-24: the window activated - the open levels are read again; not again within the pause.</summary>
    [Fact]
    public void P24_Activation_RereadsTheOpenLevels_OnceInAWhile() {
        var s = Scene().Start().Navigate(B).SetBookmarks(Bookmark(Photos)).Chevron(Pane.Bookmarks, Photos, open: true);

        s.Post(new WindowActivated(s.Now));
        var reads = s.Effects.OfType<ReadBranch>().Select(r => (r.Pane, r.Path)).ToList();
        Assert.Contains((Pane.Drives, @"C:\"), reads);
        Assert.Contains((Pane.Drives, A), reads);
        Assert.Contains((Pane.Bookmarks, Photos), reads);
        Assert.DoesNotContain(reads, r => r.Path.Length == 0);
        s.Settle();

        s.Post(new WindowActivated(s.Now + 1000));
        Assert.Empty(s.Effects.OfType<ReadBranch>());

        s.Post(new WindowActivated(s.Now + PanelRules.ActivationRefreshPauseMs));
        Assert.NotEmpty(s.Effects.OfType<ReadBranch>());
    }

    /// <summary>F5 reads every level again; a closed one is dropped and read when it opens.</summary>
    [Fact]
    public void Refresh_ReadsOpenLevels_AndDropsClosedOnes() {
        var s = Scene().Start().Navigate(B).Chevron(Pane.Drives, A, open: false);

        s.Post(new PanelsRefreshRequested());

        Assert.Equal(LevelState.Unread, s.Drives.LevelOf(A).State);
        Assert.Contains(s.Effects.OfType<ReadBranch>(), r => r.Path == @"C:\");
    }


    // --- Target from the model -------------------------------------------------------------

    [Fact]
    public void TheTarget_FollowsTheKeyboardBetweenThePanels() {
        var s = Scene().Start().SetBookmarks(Bookmark(Photos)).Navigate(A);
        s.Enter(WindowZone.Bookmarks, ZoneReason.Tab);
        Assert.Equal((Pane.Bookmarks, Photos), (s.Target.Pane, s.Target.Folder));

        s.Enter(WindowZone.Drives, ZoneReason.Tab);
        Assert.Equal((Pane.Drives, A), (s.Target.Pane, s.Target.Folder));

        s.Enter(null, ZoneReason.FocusFell);
        Assert.Equal(A, s.Target.Folder);

        s.Enter(WindowZone.FileList, ZoneReason.Tab);
        Assert.Equal(TargetKind.None, s.Target.Kind);
    }

    [Fact]
    public void AnOpenMenu_IsTheTarget_UntilItsOwnCloseArrives() {
        var s = Scene().Start().Navigate(A).Enter(WindowZone.FileList);
        var first = MenuContext.For(Target.OfPanelRow(Pane.Drives, E), PlaceFacts.Ordinary, false, false);
        var second = MenuContext.For(Target.OfPanelRow(Pane.Drives, A), PlaceFacts.Ordinary, false, false);

        s.Post(new MenuOpened(first)).Post(new MenuOpened(second)).Post(new MenuClosed(first));
        Assert.Equal(A, s.Target.Folder);

        s.Post(new MenuClosed(second));
        Assert.Equal(TargetKind.None, s.Target.Kind);
    }


    // --- Helpers -----------------------------------------------------------------------------

    private static WorkspaceScene Scene(params string[] more) {
        return new WorkspaceScene(new[] { C, D, E, Year }.Concat(more).ToArray());
    }

    private static PanelRow Bookmark(string path) {
        return WorkspaceScene.Bookmark(path);
    }
}
