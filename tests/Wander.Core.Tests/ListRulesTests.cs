using Wander.Core.FileSystem;
using Wander.Core.Layout;
using Wander.Core.Listing;
using Wander.Core.Workspace;

namespace Wander.Core.Tests;

/// <summary>
/// The list as a model (REDESIGN 4.5, modules 3-4; table 4.10, L rows and
/// the list's K rows): what is selected once rows land, and where the
/// keyboard goes. The rows are paths; the view is not here.
/// </summary>
public class ListRulesTests {
    private const string Folder = @"C:\A";
    private const string A = @"C:\A\a.jpg";
    private const string B = @"C:\A\b.jpg";
    private const string C = @"C:\A\c.jpg";
    private const string D = @"C:\A\d.jpg";
    private const string E = @"C:\A\e.jpg";

    private static readonly string[] _rows = { A, B, C, D, E };


    // --- Arriving ----------------------------------------------------------------

    /// <summary>L-1: walked in with a row remembered - that row, brought into view; the keyboard in the list onto it.</summary>
    [Fact]
    public void L01_ArrivalWithARememberedRow_SelectsItAndScrolls() {
        var s = Opened().Enter(WindowZone.FileList);

        s.Land(new[] { @"C:\E\x.txt" }, _rows, ListingReason.Arrival, Asked(C));

        Assert.Equal(new[] { C }, s.State.List.Selection);
        Assert.Equal(C, s.State.List.Caret);
        Assert.True(Assert.Single(s.Effects.OfType<ApplyListSelection>()).Scroll);
        Assert.Equal(new FocusRow(WindowZone.FileList, C), Assert.Single(s.Effects.OfType<FocusRow>()));
    }

    /// <summary>L-1: walked in with nothing remembered - nothing selected, nothing scrolls, the keyboard stays.</summary>
    [Fact]
    public void L01_ArrivalWithNothingRemembered_SelectsNothing() {
        var s = Opened().Enter(WindowZone.FileList).Select(@"C:\E\x.txt");

        s.Land(new[] { @"C:\E\x.txt" }, _rows, ListingReason.Arrival);

        Assert.Empty(s.State.List.Selection);
        Assert.Null(s.State.List.Caret);
        Assert.False(Assert.Single(s.Effects.OfType<ApplyListSelection>()).Scroll);
        Assert.Empty(s.Effects.OfType<FocusRow>());
    }

    /// <summary>
    /// The window came up on the last session's place (2026-09-25): its file
    /// selected, the keyboard - on the window itself, where WPF put it - onto
    /// it. Clicked into a panel while the folder was listing, it stays there.
    /// </summary>
    [Theory]
    [InlineData(null, ZoneReason.FocusFell, true)]
    [InlineData(WindowZone.Drives, ZoneReason.Click, false)]
    public void TheLastSessionsPlace_TakesTheKeyboardFromNowhere(WindowZone? zone, ZoneReason reason, bool keyboardFollows) {
        var session = new FolderSession();
        session.SetArrival(ArrivalIntent.Place(Folder, C, new[] { B, C, D }, top: B));
        var decision = session.DecideArrival(Folder, _rows.Select(Entry).ToList());
        var s = Opened().Enter(zone, reason);

        s.Land(Array.Empty<string>(), _rows, ListingReason.Arrival, decision);

        Assert.Equal(new[] { C }, s.State.List.Selection);
        Assert.Equal(keyboardFollows, s.Effects.OfType<FocusRow>().Any(f => f.Path == C));
    }

    /// <summary>A folder opened from a panel row: nothing of its own listing is selected.</summary>
    [Fact]
    public void AFolderOpenedFromAPanel_SelectsNothingInIt() {
        var s = Opened().Select(@"C:\E\x.txt");

        s.Land(new[] { @"C:\E\x.txt" }, _rows, ListingReason.Arrival,
            new ArrivalDecision(ArrivalOutcome.SelectFolder, Array.Empty<FileSystemEntry>(), FolderPath: Folder));

        Assert.Empty(s.State.List.Selection);
    }

    /// <summary>The caret belongs to the folder being left; the selection stays on its rows until the new ones land.</summary>
    [Fact]
    public void Navigating_DropsTheCaret() {
        var s = Opened().Select(B);

        s.Navigate(@"C:\E");

        Assert.Null(s.State.List.Caret);
        Assert.Equal(new[] { B }, s.State.List.Selection);
    }


    // --- The same folder read again ------------------------------------------------

    /// <summary>L-2, K-7: read again without an intent - the selection in place, nothing scrolls, the keyboard stays (N9).</summary>
    [Fact]
    public void L02_K07_ARelist_KeepsTheSelection_AndMovesNothing() {
        var s = Listed().Enter(WindowZone.FileList).Select(B, D);

        s.Land(_rows, _rows.Append(F("f.jpg")).ToArray());

        Assert.Equal(new[] { B, D }, s.State.List.Selection);
        Assert.Equal(B, s.State.List.Primary);
        Assert.Equal(D, s.State.List.Caret);
        Assert.False(Assert.Single(s.Effects.OfType<ApplyListSelection>()).Scroll);
        Assert.Empty(s.Effects.OfType<FocusRow>());
    }

    /// <summary>L-3, K-1: the selected row went - deleted elsewhere - and the next one takes its place, the keyboard with it, nothing scrolling.</summary>
    [Fact]
    public void L03_K01_TheSelectedRowGone_TheNextTakesItsPlace() {
        var s = Listed().Enter(WindowZone.FileList).Select(B);

        s.Land(_rows, Without(B));

        Assert.Equal(new[] { C }, s.State.List.Selection);
        Assert.Equal(C, s.State.List.Caret);
        Assert.Equal(new FocusRow(WindowZone.FileList, C, Scroll: false), Assert.Single(s.Effects.OfType<FocusRow>()));
    }

    /// <summary>L-3: the last row went - the one before it.</summary>
    [Fact]
    public void L03_TheLastRowGone_TheOneBeforeItTakesItsPlace() {
        var s = Listed().Select(E);

        s.Land(_rows, Without(E));

        Assert.Equal(new[] { D }, s.State.List.Selection);
    }

    /// <summary>
    /// L-4, K-11: the filter hid the selected row - a rating changed under
    /// "three stars and up" - exactly as if it had been deleted (decision B6).
    /// </summary>
    [Fact]
    public void L04_K11_TheFilterHidTheSelectedRow_TheNextTakesItsPlace() {
        var s = Listed().Enter(WindowZone.FileList).Select(B);

        s.Land(_rows, new[] { A, C, E });

        Assert.Equal(new[] { C }, s.State.List.Selection);
        Assert.Equal(new FocusRow(WindowZone.FileList, C, Scroll: false), Assert.Single(s.Effects.OfType<FocusRow>()));
    }

    /// <summary>L-5, decision B7: renamed by another program - the new name stays selected, the keyboard on its new row.</summary>
    [Fact]
    public void L05_RenamedElsewhere_TheNewNameStaysSelected() {
        string renamed = F("b-final.jpg");
        var s = Listed().Enter(WindowZone.FileList).Select(B);

        s.Land(_rows, new[] { A, renamed, C, D, E }, renames: (B, renamed));

        Assert.Equal(new[] { renamed }, s.State.List.Selection);
        Assert.Equal(renamed, s.State.List.Caret);
        Assert.Equal(new FocusRow(WindowZone.FileList, renamed, Scroll: false), Assert.Single(s.Effects.OfType<FocusRow>()));
    }

    /// <summary>Some of the selection went: the rest stays; the caret on a row gone goes to the main row.</summary>
    [Fact]
    public void SomeOfTheSelectionGone_TheRestStays() {
        var s = Listed().Enter(WindowZone.FileList).Select(B, C, D);

        s.Land(_rows, Without(D));

        Assert.Equal(new[] { B, C }, s.State.List.Selection);
        Assert.Equal(B, s.State.List.Caret);
        Assert.Equal(new FocusRow(WindowZone.FileList, B, Scroll: false), Assert.Single(s.Effects.OfType<FocusRow>()));
    }

    /// <summary>A click on empty space left the caret and nothing selected: the caret's row gone, the caret moves on, nothing gets selected.</summary>
    [Fact]
    public void NothingSelected_TheCaretsRowGone_TheCaretMovesOn() {
        var s = Listed().Post(new ListSelectionChanged(Array.Empty<string>(), null, C));

        s.Land(_rows, Without(C));

        Assert.Empty(s.State.List.Selection);
        Assert.Equal(D, s.State.List.Caret);
    }

    /// <summary>K-12: Ctrl+click took the selection off the keyboard's row - a re-read leaves the keyboard there.</summary>
    [Fact]
    public void K12_TheKeyboardOffTheSelection_StaysThroughARelist() {
        var s = Listed().Enter(WindowZone.FileList).Post(new ListSelectionChanged(new[] { A, E }, A, C));

        s.Land(_rows, _rows);

        Assert.Equal(C, s.State.List.Caret);
        Assert.Equal(new[] { A, E }, s.State.List.Selection);
        Assert.Empty(s.Effects.OfType<FocusRow>());
    }

    /// <summary>L-9, N8: a rating on N selected rows - the rows swapped, all N are put back on the list, nothing moving.</summary>
    [Fact]
    public void L09_RowsReplaced_PutTheWholeSelectionBack() {
        var s = Listed().Enter(WindowZone.FileList).Select(A, C, E);

        s.Land(_rows, _rows, ListingReason.RowsReplaced);

        var apply = Assert.Single(s.Effects.OfType<ApplyListSelection>());
        Assert.Equal(new[] { A, C, E }, apply.List.Selection);
        Assert.False(apply.Scroll);
        Assert.Empty(s.Effects.OfType<FocusRow>());
    }

    /// <summary>
    /// A filter taken off, another order (2026-09-28): the selection stays by
    /// path, and its main row is followed into view - the eye is on it. The
    /// keyboard stays where it is.
    /// </summary>
    [Fact]
    public void ARearrangement_FollowsTheMainRow() {
        var s = Listed().Enter(WindowZone.FileList).Select(B, D);

        s.Land(new[] { B, D }, new[] { E, D, C, B, A }, ListingReason.Rearranged);

        Assert.Equal(new[] { B, D }, s.State.List.Selection);
        Assert.Equal(B, s.State.List.Primary);
        Assert.True(Assert.Single(s.Effects.OfType<ApplyListSelection>()).Scroll);
        Assert.Empty(s.Effects.OfType<FocusRow>());
    }

    /// <summary>A filter hid the selected row: the one that took its place is followed, the keyboard onto it as after any row gone.</summary>
    [Fact]
    public void ARearrangementHidingTheSelectedRow_FollowsTheOneInItsPlace() {
        var s = Listed().Enter(WindowZone.FileList).Select(B);

        s.Land(_rows, new[] { A, C, E }, ListingReason.Rearranged);

        var apply = Assert.Single(s.Effects.OfType<ApplyListSelection>());
        Assert.Equal(new[] { C }, apply.List.Selection);
        Assert.True(apply.Scroll);
        Assert.Equal(new FocusRow(WindowZone.FileList, C, Scroll: false), Assert.Single(s.Effects.OfType<FocusRow>()));
    }

    /// <summary>Nothing selected: a rearrangement has no row to follow, and nothing scrolls.</summary>
    [Fact]
    public void ARearrangementWithNothingSelected_ScrollsNothing() {
        var s = Listed();

        s.Land(_rows, new[] { A, C }, ListingReason.Rearranged);

        Assert.False(Assert.Single(s.Effects.OfType<ApplyListSelection>()).Scroll);
    }

    /// <summary>Search results left for the folder: a selected result not in it did not leave the folder, and nothing takes its place.</summary>
    [Fact]
    public void ResultsLeft_ASelectedResultNotInTheFolder_LeavesNothingSelected() {
        string result = @"D:\Elsewhere\x.jpg";
        var s = Listed().Select(result);

        s.Land(new[] { result, C }, _rows, ListingReason.ResultsLeft);

        Assert.Empty(s.State.List.Selection);
    }

    /// <summary>Search results left for the folder with a row of it selected: it stays selected and is brought into view - the list is another one.</summary>
    [Fact]
    public void ResultsLeft_ASelectedResultOfTheFolder_IsShown() {
        var s = Listed().Select(C);

        s.Land(new[] { @"D:\Elsewhere\x.jpg", C }, _rows, ListingReason.ResultsLeft);

        var apply = Assert.Single(s.Effects.OfType<ApplyListSelection>());
        Assert.Equal(new[] { C }, apply.List.Selection);
        Assert.True(apply.Scroll);
        Assert.Equal(C, apply.Held);
    }

    /// <summary>
    /// Search results over the folder's rows, and more of them as the search
    /// goes on: a selected row among them stays selected, keeps its place
    /// and is not brought into view - the user may be looking at others.
    /// </summary>
    [Fact]
    public void Results_KeepASelectedRowThatIsAmongThem() {
        var s = Listed().Enter(WindowZone.FileList).Select(B);

        s.Land(_rows, new[] { @"C:\A\sub\x.jpg", B, @"C:\A\sub\y.jpg" }, ListingReason.Results);

        var apply = Assert.Single(s.Effects.OfType<ApplyListSelection>());
        Assert.Equal(new[] { B }, apply.List.Selection);
        Assert.False(apply.Scroll);
        Assert.Equal(B, apply.Held);
        Assert.Empty(s.Effects.OfType<FocusRow>());
    }

    /// <summary>A selected row the search did not find did not leave the folder: nothing takes its place.</summary>
    [Fact]
    public void Results_ASelectedRowNotAmongThem_LeavesNothingSelected() {
        var s = Listed().Select(B);

        s.Land(_rows, new[] { A, C }, ListingReason.Results);

        Assert.Empty(s.State.List.Selection);
        Assert.Null(Assert.Single(s.Effects.OfType<ApplyListSelection>()).Held);
    }


    // --- The main row's place on screen (2026-09-28) ---------------------------------

    /// <summary>Read again - the watcher, F5, an operation: the main row keeps its place, and one out of view is left there.</summary>
    [Fact]
    public void ARelist_HoldsTheMainRow_AndShowsNothing() {
        var s = Listed().Select(B, D);

        s.Land(_rows, _rows.Prepend(F("0.jpg")).ToArray());

        var apply = Assert.Single(s.Effects.OfType<ApplyListSelection>());
        Assert.Equal(B, apply.Held);
        Assert.False(apply.Scroll);
    }

    /// <summary>Renamed by another program: the row under its new name keeps the place it had under the old one.</summary>
    [Fact]
    public void RenamedElsewhere_TheNewNameKeepsThePlaceOfTheOld() {
        string renamed = F("z-final.jpg");
        var s = Listed().Select(B);

        s.Land(_rows, new[] { A, C, D, E, renamed }, renames: (B, renamed));

        var apply = Assert.Single(s.Effects.OfType<ApplyListSelection>());
        Assert.Equal(new[] { renamed }, apply.List.Selection);
        Assert.Equal(B, apply.Held);
    }

    /// <summary>The selected row went: the one that took its place takes its place on screen too.</summary>
    [Fact]
    public void TheSelectedRowGone_TheSuccessorStandsWhereItStood() {
        var s = Listed().Select(B);

        s.Land(_rows, Without(B));

        var apply = Assert.Single(s.Effects.OfType<ApplyListSelection>());
        Assert.Equal(new[] { C }, apply.List.Selection);
        Assert.Equal(B, apply.Held);
    }

    /// <summary>The main row went and another selected one is the main row now: it stood elsewhere, and nothing is held.</summary>
    [Fact]
    public void TheMainRowGone_AnotherSelectedOneTakingOver_HoldsNothing() {
        var s = Listed().Select(B, C, D);

        s.Land(_rows, Without(B));

        var apply = Assert.Single(s.Effects.OfType<ApplyListSelection>());
        Assert.Equal(C, apply.List.Primary);
        Assert.Null(apply.Held);
    }

    /// <summary>Rows that were asked for are shown; the main one of them keeps its place only when it is the row that stood there.</summary>
    [Fact]
    public void RowsAskedFor_HoldTheMainRow_OnlyWhenItIsTheSameRow() {
        string pasted = F("pasted.jpg");
        string renamed = F("b2.jpg");

        var paste = Listed().Select(B);
        paste.Land(_rows, _rows.Append(pasted).ToArray(), intent: Asked(pasted));
        var rename = Listed().Select(B);
        rename.Land(_rows, new[] { A, renamed, C, D, E }, intent: Asked(renamed), renames: (B, renamed));

        Assert.Null(Assert.Single(paste.Effects.OfType<ApplyListSelection>()).Held);
        Assert.Equal(B, Assert.Single(rename.Effects.OfType<ApplyListSelection>()).Held);
    }

    /// <summary>Another folder's rows, and rows swapped for copies: no place to keep.</summary>
    [Fact]
    public void AnArrivalAndRowsReplaced_HoldNothing() {
        var arrival = Opened().Select(@"C:\E\x.txt");
        arrival.Land(new[] { @"C:\E\x.txt" }, _rows, ListingReason.Arrival);
        var replaced = Listed().Select(B);
        replaced.Land(_rows, _rows, ListingReason.RowsReplaced);

        Assert.Null(Assert.Single(arrival.Effects.OfType<ApplyListSelection>()).Held);
        Assert.Null(Assert.Single(replaced.Effects.OfType<ApplyListSelection>()).Held);
    }


    // --- What an operation brought ---------------------------------------------------

    /// <summary>L-6: Delete - the next row that survived is selected, the keyboard on it.</summary>
    [Fact]
    public void L06_Delete_SelectsTheNextSurvivor_WithTheKeyboard() {
        var s = Listed().Enter(WindowZone.FileList).Select(B);

        s.Land(_rows, Without(B), intent: Asked(C, takeFocus: true));

        Assert.Equal(new[] { C }, s.State.List.Selection);
        Assert.Equal(new FocusRow(WindowZone.FileList, C), Assert.Single(s.Effects.OfType<FocusRow>()));
    }

    /// <summary>
    /// L-7, K-3: a paste behind a dialog - what arrived is selected; the
    /// keyboard onto it from the list or from nowhere, a panel keeps it.
    /// </summary>
    [Theory]
    [InlineData(WindowZone.FileList, true)]
    [InlineData(null, true)]
    [InlineData(WindowZone.Drives, false)]
    public void L07_K03_APasteBehindADialog_SelectsWhatArrived(WindowZone? zone, bool keyboardFollows) {
        string pasted = F("pasted.jpg");
        var s = Listed().Enter(zone, ZoneReason.Unknown);

        s.Land(_rows, _rows.Append(pasted).ToArray(), intent: Asked(pasted, takeFocus: true));

        Assert.Equal(new[] { pasted }, s.State.List.Selection);
        Assert.Equal(keyboardFollows, s.Effects.OfType<FocusRow>().Any(f => f.Path == pasted));
    }

    /// <summary>K-6: Ctrl+Z brought a file back - the keyboard in the list goes onto it; nowhere or in a panel, it stays.</summary>
    [Theory]
    [InlineData(WindowZone.FileList, true)]
    [InlineData(null, false)]
    [InlineData(WindowZone.Bookmarks, false)]
    public void K06_UndoBringsAFileBack_TheKeyboardInTheListGoesOntoIt(WindowZone? zone, bool keyboardFollows) {
        var s = Listed().Enter(WindowZone.FileList).Select(C).Enter(zone, ZoneReason.Unknown);

        s.Land(Without(B), _rows, intent: Asked(B));

        Assert.Equal(new[] { B }, s.State.List.Selection);
        Assert.Equal(keyboardFollows, s.Effects.OfType<FocusRow>().Any(f => f.Path == B));
    }

    /// <summary>
    /// L-12: a folder just created - selected, its name editor open. The
    /// editor takes the keyboard itself: a row focused after it would take it
    /// away, and the name would be committed untouched.
    /// </summary>
    [Fact]
    public void L12_ANewFolder_IsSelectedWithTheEditorOpen() {
        string created = F("New folder");
        var s = Listed().Enter(WindowZone.FileList);

        s.Land(_rows, _rows.Prepend(created).ToArray(), intent: Asked(created, takeFocus: true, rename: created));

        Assert.Equal(new[] { created }, s.State.List.Selection);
        Assert.Equal(new OpenEditor(created), Assert.Single(s.Effects.OfType<OpenEditor>()));
        Assert.Empty(s.Effects.OfType<FocusRow>());
    }

    /// <summary>K-4: the editor closed by Enter - the keyboard onto the renamed row once it lands.</summary>
    [Fact]
    public void K04_EnterCommitsTheRename_TheKeyboardFollowsTheNewName() {
        string renamed = F("b2.jpg");
        var s = Listed().Enter(WindowZone.FileList).Select(B);

        s.Land(_rows, new[] { A, renamed, C, D, E }, intent: Asked(renamed, takeFocus: true), renames: (B, renamed));

        Assert.Equal(new[] { renamed }, s.State.List.Selection);
        Assert.Equal(new FocusRow(WindowZone.FileList, renamed), Assert.Single(s.Effects.OfType<FocusRow>()));
    }

    /// <summary>K-4: the editor closed by a click on another row - the selection and the keyboard are where the click put them.</summary>
    [Fact]
    public void K04_AClickAwayCommits_TheKeyboardStaysWhereTheClickPutIt() {
        string renamed = F("b2.jpg");
        var s = Listed().Enter(WindowZone.FileList).Select(B).Select(D);

        s.Land(_rows, new[] { A, renamed, C, D, E }, renames: (B, renamed));

        Assert.Equal(new[] { D }, s.State.List.Selection);
        Assert.Equal(D, s.State.List.Caret);
        Assert.Empty(s.Effects.OfType<FocusRow>());
    }


    // --- Another view ------------------------------------------------------------------

    /// <summary>L-10, K-5: another view - the selection is untouched, the keyboard in the list goes onto the caret's row there.</summary>
    [Fact]
    public void L10_K05_AnotherView_KeepsTheSelection_AndTheKeyboardOnTheCaret() {
        var s = Listed().Enter(WindowZone.FileList).Select(A, C);

        s.Post(new ViewModeChanged());

        Assert.Equal(new[] { A, C }, s.State.List.Selection);
        Assert.Equal(new FocusRow(WindowZone.FileList, C), Assert.Single(s.Effects.OfType<FocusRow>()));
    }

    /// <summary>K-5: another view with the keyboard elsewhere - the keyboard is not pulled into the list.</summary>
    [Fact]
    public void K05_AnotherView_WithTheKeyboardElsewhere_MovesNoKeyboard() {
        var s = Listed().Select(A).Enter(WindowZone.Drives, ZoneReason.Click);

        s.Post(new ViewModeChanged());

        Assert.Empty(s.Effects.OfType<FocusRow>());
    }


    // --- Helpers -----------------------------------------------------------------------

    private static string F(string name) {
        return Path.Combine(Folder, name);
    }

    private static string[] Without(string row) {
        return _rows.Where(r => r != row).ToArray();
    }

    /// <summary>The window with the folder open, its rows not landed yet.</summary>
    private static WorkspaceScene Opened() {
        return new WorkspaceScene(@"C:\A\B", @"C:\E").Start().Navigate(Folder);
    }

    /// <summary>The window with the folder open and its rows landed.</summary>
    private static WorkspaceScene Listed() {
        return Opened().Land(Array.Empty<string>(), _rows, ListingReason.Arrival);
    }

    /// <summary>An intent that found its rows.</summary>
    private static ArrivalDecision Asked(string row, bool takeFocus = false, string? rename = null) {
        return new ArrivalDecision(ArrivalOutcome.SelectRows, new[] { Entry(row) }, takeFocus, rename);
    }

    private static FileSystemEntry Entry(string path) {
        return new FileSystemEntry(
            Name: Path.GetFileName(path),
            FullPath: path,
            Kind: EntryKind.File,
            Size: 0,
            ModifiedUtc: DateTime.MinValue,
            IsHidden: false,
            IsReadOnly: false,
            IsSystem: false,
            LinksToDirectory: false);
    }
}
