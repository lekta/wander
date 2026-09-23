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

    /// <summary>Search results left for the folder: a selected result not in it did not leave the folder, and nothing takes its place.</summary>
    [Fact]
    public void ResultsLeft_ASelectedResultNotInTheFolder_LeavesNothingSelected() {
        string result = @"D:\Elsewhere\x.jpg";
        var s = Listed().Select(result);

        s.Land(new[] { result, C }, _rows, ListingReason.ResultsLeft);

        Assert.Empty(s.State.List.Selection);
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
        var entry = new FileSystemEntry(
            Name: Path.GetFileName(row),
            FullPath: row,
            Kind: EntryKind.File,
            Size: 0,
            ModifiedUtc: DateTime.MinValue,
            IsHidden: false,
            IsReadOnly: false,
            IsSystem: false,
            LinksToDirectory: false);

        return new ArrivalDecision(ArrivalOutcome.SelectRows, new[] { entry }, takeFocus, rename);
    }
}
