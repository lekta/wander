using Wander.Core.FileSystem;
using Wander.Core.Layout;
using Wander.Core.Panels;
using Wander.Core.Workspace;

namespace Wander.Core.Tests;

/// <summary>
/// The target of the next operation (REDESIGN 4.3, table 4.10 T-1...T-12,
/// findings N1-N4, N11, N12). The target is worked out from where the
/// keyboard is, never set by the keyboard arriving somewhere.
/// </summary>
public class TargetRulesTests {
    private const string Open = @"C:\work";
    private const string PanelFolder = @"D:\photos\2026";


    // --- Where the keyboard is ---------------------------------------------

    /// <summary>T-1.</summary>
    [Fact]
    public void KeyboardInTheList_TargetsTheSelection() {
        var a = File("a.txt");
        var b = File("b.txt");

        var target = TargetRules.Of(InList(new[] { a, b }, primary: b));

        Assert.Equal(TargetKind.ListRows, target.Kind);
        Assert.Equal(new[] { a, b }, target.Rows);
        Assert.Same(b, target.Primary);
    }

    /// <summary>T-2 / N4: an empty selection is no target, whatever a panel holds.</summary>
    [Fact]
    public void KeyboardInTheList_WithNothingSelected_IsNoTarget() {
        var facts = InList(Array.Empty<FileSystemEntry>()) with { DrivesCaret = PanelFolder };

        var target = TargetRules.Of(facts);

        Assert.Equal(TargetKind.None, target.Kind);
        Assert.Empty(TargetRules.Items(target));
        Assert.Equal(OpenRoute.None, TargetRules.Open(target));
        Assert.Equal(RenameRoute.None, TargetRules.Rename(target));
    }

    /// <summary>
    /// T-2, decision B3: with no target the verbs about a folder are about
    /// the open one; nothing is deleted, opened or renamed.
    /// </summary>
    [Fact]
    public void NoTarget_PastesAndShowsPropertiesOfTheOpenFolder() {
        Assert.Equal(Open, TargetRules.PasteFolder(Target.None, Open, fromMenu: false));
        Assert.Equal(Open, TargetRules.PropertiesOf(Target.None, Open));
        Assert.Equal(Open, TargetRules.TerminalFolder(Target.None, Open));
        Assert.Equal(new[] { Open }, TargetRules.CopyPaths(Target.None, Open));
    }

    /// <summary>T-3, decision B2: the list keeps its selection; the target is the panel's row.</summary>
    [Fact]
    public void KeyboardInAPanel_TargetsTheRowUnderItsCursor() {
        var selected = File("a.txt");
        var facts = InList(new[] { selected }) with { Zone = WindowZone.Drives, DrivesCaret = PanelFolder };

        var target = TargetRules.Of(facts);

        Assert.Equal(TargetKind.PanelRow, target.Kind);
        Assert.Equal(Pane.Drives, target.Pane);
        Assert.Equal(PanelFolder, target.Folder);
        // Nothing of the list went anywhere: the facts still hold it.
        Assert.Same(selected, facts.ListSelection[0]);
    }

    [Fact]
    public void KeyboardInTheBookmarks_TargetsTheBookmarksCursor_NotTheDrives() {
        var facts = new TargetFacts {
            Zone = WindowZone.Bookmarks, BookmarksCaret = @"C:\Users\me\Downloads", DrivesCaret = PanelFolder,
        };

        var target = TargetRules.Of(facts);

        Assert.Equal(Pane.Bookmarks, target.Pane);
        Assert.Equal(@"C:\Users\me\Downloads", target.Folder);
    }

    /// <summary>A panel with no row under its cursor has nothing to act on.</summary>
    [Fact]
    public void KeyboardInAPanel_WithNoCursor_IsNoTarget() {
        var facts = InList(new[] { File("a.txt") }) with { Zone = WindowZone.Drives, DrivesCaret = null };

        Assert.Equal(TargetKind.None, TargetRules.Of(facts).Kind);
    }

    /// <summary>
    /// The address bar, the filter and the toolbar are about the list's
    /// selection: the keyboard typing a path does not change what Delete is
    /// about.
    /// </summary>
    [Theory]
    [InlineData(WindowZone.Address)]
    [InlineData(WindowZone.Search)]
    [InlineData(WindowZone.Toolbar)]
    public void KeyboardInTheChrome_TargetsTheListSelection(WindowZone zone) {
        var a = File("a.txt");
        var facts = InList(new[] { a }) with { Zone = zone, LastZone = zone, DrivesCaret = PanelFolder };

        Assert.Equal(new[] { a }, TargetRules.Of(facts).Rows);
    }

    /// <summary>
    /// T-12: back from a menu, the window activated, the keyboard fallen onto
    /// the window - it is in no zone, and it means what it meant in the last.
    /// </summary>
    [Fact]
    public void KeyboardInNoZone_MeansTheLastZone() {
        var a = File("a.txt");
        var fromPanel = InList(new[] { a }) with { Zone = null, LastZone = WindowZone.Drives, DrivesCaret = PanelFolder };
        var fromList = fromPanel with { LastZone = WindowZone.FileList };

        Assert.Equal(PanelFolder, TargetRules.Of(fromPanel).Folder);
        Assert.Equal(new[] { a }, TargetRules.Of(fromList).Rows);
    }

    /// <summary>
    /// N4 / T-10: the keyboard left the panel for the list (Ctrl+2, Esc, a
    /// click on the list's empty space) and nothing is selected there - the
    /// panel's row is not what Delete, Paste, Enter or F2 are about any more.
    /// </summary>
    [Fact]
    public void LeavingThePanel_LeavesNothingOfItsRowBehind() {
        var inPanel = new TargetFacts { Zone = WindowZone.Drives, LastZone = WindowZone.Drives, DrivesCaret = PanelFolder };
        var backInList = inPanel with { Zone = WindowZone.FileList, LastZone = WindowZone.FileList };

        var target = TargetRules.Of(backInList);

        Assert.Equal(TargetKind.None, target.Kind);
        Assert.Empty(TargetRules.Items(target));
        Assert.Equal(Open, TargetRules.PasteFolder(target, Open, fromMenu: false));
        Assert.Equal(OpenRoute.None, TargetRules.Open(target));
        Assert.Equal(RenameRoute.None, TargetRules.Rename(target));
    }

    /// <summary>
    /// N12 / T-9: Backspace with the keyboard in a panel went up; the panel's
    /// cursor is on the parent now, and Delete is about the parent - not
    /// about the row the list selected in it (the folder it came out of).
    /// </summary>
    [Fact]
    public void UpFromAPanel_DeleteIsAboutTheParent() {
        var cameOutOf = Dir("child");
        var facts = InList(new[] { cameOutOf }) with { Zone = WindowZone.Drives, DrivesCaret = Open };

        var target = TargetRules.Of(facts);

        Assert.Equal(TargetKind.PanelRow, target.Kind);
        Assert.Equal(Open, Assert.Single(TargetRules.Items(target)).FullPath);
    }


    // --- An open menu --------------------------------------------------------

    /// <summary>T-4: a menu is about its subject while it is open.</summary>
    [Fact]
    public void OpenMenu_IsAboutItsSubject() {
        var subject = Target.OfPanelRow(Pane.Drives, PanelFolder);
        var facts = InList(new[] { File("a.txt") }) with { MenuSubject = subject };

        Assert.Same(subject, TargetRules.Of(facts));
    }

    /// <summary>T-5: closed, the target goes back to where the keyboard is.</summary>
    [Fact]
    public void ClosedMenu_LeavesNoSubject() {
        var a = File("a.txt");
        var open = InList(new[] { a }) with { MenuSubject = Target.OfPanelRow(Pane.Drives, PanelFolder) };

        var target = TargetRules.Of(open with { MenuSubject = null });

        Assert.Equal(new[] { a }, target.Rows);
    }

    /// <summary>
    /// N1 / T-4: "Properties" and Alt+Enter on a panel row are the row's -
    /// not the open folder's, which the list happens to be showing.
    /// </summary>
    [Fact]
    public void PanelRow_PropertiesAndTerminalAreTheRows() {
        var row = Target.OfPanelRow(Pane.Drives, PanelFolder);

        Assert.Equal(PanelFolder, TargetRules.PropertiesOf(row, Open));
        Assert.Equal(PanelFolder, TargetRules.TerminalFolder(row, Open));
        Assert.Equal(new[] { PanelFolder }, TargetRules.CopyPaths(row, Open));
        Assert.Equal(OpenRoute.PanelRow, TargetRules.Open(row));
        Assert.Equal(RenameRoute.PanelRow, TargetRules.Rename(row));
    }

    /// <summary>The empty space of a folder: its own paste, properties, terminal and path.</summary>
    [Fact]
    public void Background_IsAboutItsFolder() {
        var background = Target.OfBackground(Open);

        Assert.Empty(TargetRules.Items(background));
        Assert.Equal(Open, TargetRules.PasteFolder(background, @"C:\elsewhere", fromMenu: true));
        Assert.Equal(Open, TargetRules.PropertiesOf(background, @"C:\elsewhere"));
        Assert.Equal(Open, TargetRules.TerminalFolder(background, @"C:\elsewhere"));
        Assert.Equal(OpenRoute.None, TargetRules.Open(background));
        Assert.Equal(RenameRoute.None, TargetRules.Rename(background));
    }


    // --- Paste -------------------------------------------------------------

    /// <summary>T-7: Ctrl+V with the keyboard on a panel row goes into it.</summary>
    [Fact]
    public void Paste_IntoAPanelRow() {
        var row = Target.OfPanelRow(Pane.Drives, PanelFolder);

        Assert.Equal(PanelFolder, TargetRules.PasteFolder(row, Open, fromMenu: false));
    }

    /// <summary>
    /// T-7: "Paste" in the menu of one folder row of the list goes into that
    /// folder; Ctrl+V in the list with the same folder selected goes into the
    /// open folder, as in Explorer.
    /// </summary>
    [Fact]
    public void Paste_IntoAListFolder_OnlyFromItsMenu() {
        var sub = Dir("sub");
        var rows = Target.OfRows(new[] { sub }, sub);

        Assert.Equal(sub.FullPath, TargetRules.PasteFolder(rows, Open, fromMenu: true));
        Assert.Equal(Open, TargetRules.PasteFolder(rows, Open, fromMenu: false));
    }

    [Fact]
    public void Paste_FromTheMenuOfFiles_GoesIntoTheOpenFolder() {
        var a = File("a.txt");
        var two = Target.OfRows(new[] { Dir("sub"), a }, a);

        Assert.Equal(Open, TargetRules.PasteFolder(Target.OfRows(new[] { a }, a), Open, fromMenu: true));
        Assert.Equal(Open, TargetRules.PasteFolder(two, Open, fromMenu: true));
    }


    // --- One-row verbs and F2 ------------------------------------------------

    /// <summary>T-8.</summary>
    [Fact]
    public void Rename_OneRowInPlace_TwoOrMoreInTheWindow() {
        var a = File("a.txt");
        var b = File("b.txt");

        Assert.Equal(RenameRoute.ListRow, TargetRules.Rename(Target.OfRows(new[] { a }, a)));
        Assert.Equal(RenameRoute.ListBatch, TargetRules.Rename(Target.OfRows(new[] { a, b }, a)));
    }

    [Fact]
    public void Properties_OfTheListsCurrentRow() {
        var a = File("a.txt");
        var b = File("b.txt");

        Assert.Equal(b.FullPath, TargetRules.PropertiesOf(Target.OfRows(new[] { a, b }, b), Open));
    }

    [Fact]
    public void Terminal_InTheOneSelectedFolder_ElseInTheOpenOne() {
        var sub = Dir("sub");
        var a = File("a.txt");

        Assert.Equal(sub.FullPath, TargetRules.TerminalFolder(Target.OfRows(new[] { sub }, sub), Open));
        Assert.Equal(Open, TargetRules.TerminalFolder(Target.OfRows(new[] { a }, a), Open));
        Assert.Equal(Open, TargetRules.TerminalFolder(Target.OfRows(new[] { sub, a }, a), Open));
    }

    /// <summary>The list reports its current item apart from its selection; one that is not among the rows gives way.</summary>
    [Fact]
    public void Primary_NotAmongTheRows_GivesWayToTheFirst() {
        var a = File("a.txt");

        var target = Target.OfRows(new[] { a }, File("gone.txt"));

        Assert.Same(a, target.Primary);
    }


    // --- One change, one line (N11) ----------------------------------------

    /// <summary>
    /// N11: the same target whatever objects stand for it - rows replaced by
    /// a re-listing or a rating written are not a new target, and neither is
    /// the same panel row reported twice.
    /// </summary>
    [Fact]
    public void SameAs_IgnoresReplacedRowObjects() {
        var a = File("a.txt");
        var again = a with { Rating = new SidecarRating(3, 0) };

        Assert.True(Target.OfRows(new[] { a }, a).SameAs(Target.OfRows(new[] { again }, again)));
        Assert.True(Target.OfPanelRow(Pane.Drives, PanelFolder).SameAs(Target.OfPanelRow(Pane.Drives, PanelFolder.ToUpperInvariant())));
        Assert.True(Target.None.SameAs(Target.OfRows(Array.Empty<FileSystemEntry>(), null)));
    }

    [Fact]
    public void SameAs_TellsTargetsApart() {
        var a = File("a.txt");
        var b = File("b.txt");

        Assert.False(Target.OfRows(new[] { a }, a).SameAs(Target.OfRows(new[] { a, b }, a)));
        Assert.False(Target.OfRows(new[] { a, b }, a).SameAs(Target.OfRows(new[] { a, b }, b)));
        Assert.False(Target.OfPanelRow(Pane.Drives, PanelFolder).SameAs(Target.OfPanelRow(Pane.Bookmarks, PanelFolder)));
        Assert.False(Target.OfPanelRow(Pane.Drives, PanelFolder).SameAs(Target.OfBackground(PanelFolder)));
    }


    // --- A panel row as a row --------------------------------------------------

    [Fact]
    public void FolderEntry_IsAFolderByItsPath() {
        var entry = TargetRules.FolderEntry(PanelFolder + @"\");

        Assert.Equal("2026", entry.Name);
        Assert.Equal(EntryKind.Directory, entry.Kind);
        Assert.Equal(PanelFolder + @"\", entry.FullPath);
    }

    [Fact]
    public void FolderEntry_OfADrive_IsNamedByItsPath() {
        Assert.Equal(@"D:\", TargetRules.FolderEntry(@"D:\").Name);
    }


    // --- Helpers ----------------------------------------------------------

    private static TargetFacts InList(IReadOnlyList<FileSystemEntry> selection, FileSystemEntry? primary = null) {
        return new TargetFacts {
            Zone = WindowZone.FileList,
            LastZone = WindowZone.FileList,
            ListSelection = selection,
            ListPrimary = primary ?? (selection.Count > 0 ? selection[0] : null),
        };
    }

    private static FileSystemEntry File(string name) {
        return new FileSystemEntry(
            name, Path.Combine(Open, name), EntryKind.File, 10, DateTime.UnixEpoch, false, false, false, false);
    }

    private static FileSystemEntry Dir(string name) {
        return new FileSystemEntry(
            name, Path.Combine(Open, name), EntryKind.Directory, null, DateTime.UnixEpoch, false, false, false, false);
    }
}
