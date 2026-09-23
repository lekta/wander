using Wander.Core.FileSystem;
using Wander.Core.Menu;
using Wander.Core.Panels;
using Wander.Core.Workspace;

namespace Wander.Core.Tests;

/// <summary>
/// A menu's snapshot of its subject and of the subject's place (REDESIGN
/// 4.3; T-4, T-6, T-11; findings N2, N3). Which verbs a menu offers is a
/// question about the place of what was right-clicked, not about the folder
/// open in the list.
/// </summary>
public class MenuContextTests {
    private const string Open = @"C:\work";
    private const string Row = @"D:\photos";


    /// <summary>A panel row is one folder row of its own; the shell's verbs run in that folder.</summary>
    [Fact]
    public void PanelRow_IsAFolderRowOfItsOwn() {
        var context = MenuContext.For(Target.OfPanelRow(Pane.Drives, Row), PlaceFacts.Ordinary, clipboardHasContent: true, selectionIsArchive: false);

        var target = context.ToMenuTarget(Open);

        var folder = Assert.Single(target.Selection);
        Assert.Equal(Row, folder.FullPath);
        Assert.Equal(EntryKind.Directory, folder.Kind);
        Assert.Equal(Row, target.FolderPath);
        Assert.True(target.IsPanelRow);
        Assert.False(target.IsBackground);
        Assert.True(target.CanPaste);
    }

    /// <summary>
    /// N3 / T-6: the Recycle Bin is open in the list, and a folder in the
    /// drives tree is right-clicked. Its place is an ordinary folder, and
    /// its menu has every verb an ordinary folder has.
    /// </summary>
    [Fact]
    public void PanelRow_WhileTheBinIsOpen_HasEveryVerb() {
        var context = MenuContext.For(Target.OfPanelRow(Pane.Drives, Row), PlaceFacts.Ordinary, clipboardHasContent: true, selectionIsArchive: false);

        var menu = ContextMenuBuilder.Build(context.ToMenuTarget(openFolder: "shell:RecycleBinFolder"), ContextMenuSettings.Default);

        var file = menu.First(e => e.Id == MenuCommandId.FileSubmenu).Children;
        Assert.All(
            new[] { MenuCommandId.Cut, MenuCommandId.Copy, MenuCommandId.Paste, MenuCommandId.Rename, MenuCommandId.Delete },
            id => Assert.True(file.First(e => e.Id == id).IsEnabled, $"{id} is greyed"));
        Assert.DoesNotContain(menu, e => e.Id == MenuCommandId.RestoreFromRecycleBin);
    }

    /// <summary>N2 / T-11, decision B8: no shortcut from a panel row's menu.</summary>
    [Fact]
    public void PanelRow_OffersNoShortcut() {
        var context = MenuContext.For(Target.OfPanelRow(Pane.Bookmarks, Row), PlaceFacts.Ordinary, clipboardHasContent: false, selectionIsArchive: false);

        var menu = ContextMenuBuilder.Build(context.ToMenuTarget(Open), ContextMenuSettings.Default);

        Assert.DoesNotContain(Flatten(menu), e => e.Id == MenuCommandId.CreateShortcut);
    }

    /// <summary>Rows in the bin: read-only, nothing to paste into, the bin's own menu.</summary>
    [Fact]
    public void ListRows_InTheBin_AreReadOnly() {
        var row = File("a.txt");
        var bin = new PlaceFacts(IsReadOnly: true, IsRecycleBin: true, IsArchive: false);

        var context = MenuContext.For(Target.OfRows(new[] { row }, row), bin, clipboardHasContent: true, selectionIsArchive: false);
        var target = context.ToMenuTarget(Open);

        Assert.False(context.CanPaste);
        Assert.True(target.IsReadOnlyLocation);
        Assert.True(target.IsRecycleBin);
        Assert.Equal(Open, target.FolderPath);
        Assert.Equal(new[] { row }, target.Selection);
    }

    [Fact]
    public void Background_IsTheFolderWithNoRows() {
        var context = MenuContext.For(Target.OfBackground(Open), PlaceFacts.Ordinary, clipboardHasContent: true, selectionIsArchive: true);
        var target = context.ToMenuTarget(@"C:\elsewhere");

        Assert.True(target.IsBackground);
        Assert.Empty(target.Selection);
        Assert.Equal(Open, target.FolderPath);
        // Only rows can be archives to extract.
        Assert.False(target.SelectionIsArchive);
    }

    [Fact]
    public void ArchivesAmongTheRows_EarnExtract() {
        var zip = File("a.zip");

        var context = MenuContext.For(Target.OfRows(new[] { zip }, zip), PlaceFacts.Ordinary, clipboardHasContent: false, selectionIsArchive: true);

        Assert.True(context.ToMenuTarget(Open).SelectionIsArchive);
    }

    [Fact]
    public void TheHeaderShape_IsAskedFor() {
        var context = MenuContext.For(Target.None, PlaceFacts.Ordinary, clipboardHasContent: false, selectionIsArchive: false);

        Assert.Equal(MenuPlace.Header, context.ToMenuTarget(Open, MenuPlace.Header).Place);
    }


    // --- Helpers ----------------------------------------------------------

    private static IEnumerable<MenuEntry> Flatten(IReadOnlyList<MenuEntry> menu) {
        foreach (var entry in menu) {
            yield return entry;
            foreach (var child in Flatten(entry.Children)) {
                yield return child;
            }
        }
    }

    private static FileSystemEntry File(string name) {
        return new FileSystemEntry(
            name, Path.Combine(Open, name), EntryKind.File, 10, DateTime.UnixEpoch, false, false, false, false);
    }
}
