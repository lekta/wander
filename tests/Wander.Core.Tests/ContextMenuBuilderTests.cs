using Wander.Core.FileSystem;
using Wander.Core.Menu;
using Wander.Core.Shell;

namespace Wander.Core.Tests;

public class ContextMenuBuilderTests {
    private const string Folder = @"C:\work";


    // --- Selection menu -------------------------------------------------

    [Fact]
    public void SelectionMenu_OnSingleFile_OffersTheUsualVerbs() {
        var menu = ContextMenuBuilder.Build(SelectionOf(File("a.txt")), ContextMenuSettings.Default);

        Assert.True(Enabled(menu, MenuCommandId.Open));
        Assert.True(Enabled(menu, MenuCommandId.OpenWith));
        Assert.True(Enabled(menu, MenuCommandId.Rename));
        Assert.True(Enabled(menu, MenuCommandId.Delete));
        Assert.True(Enabled(menu, MenuCommandId.Properties));
    }

    [Fact]
    public void SelectionMenu_MarksOpenAsTheDefaultVerb() {
        var menu = ContextMenuBuilder.Build(SelectionOf(File("a.txt")), ContextMenuSettings.Default);

        Assert.True(Find(menu, MenuCommandId.Open)!.IsDefault);
    }

    [Fact]
    public void SelectionMenu_PutsClipboardVerbsInTheFileSubmenu() {
        var menu = ContextMenuBuilder.Build(SelectionOf(File("a.txt")), ContextMenuSettings.Default);

        var file = Find(menu, MenuCommandId.FileSubmenu);
        Assert.NotNull(file);
        // Top level stays free of them; the submenu is their only home.
        Assert.Null(menu.FirstOrDefault(e => e.Id == MenuCommandId.Cut));
        Assert.NotNull(Find(file!.Children, MenuCommandId.Cut));
        Assert.NotNull(Find(file.Children, MenuCommandId.Copy));
        Assert.NotNull(Find(file.Children, MenuCommandId.CopyPath));
        Assert.NotNull(Find(file.Children, MenuCommandId.CreateShortcut));
    }

    [Fact]
    public void SelectionMenu_OnMultipleItems_DisablesSingleOnlyVerbs() {
        var menu = ContextMenuBuilder.Build(
            SelectionOf(File("a.txt"), File("b.txt")), ContextMenuSettings.Default);

        Assert.False(Enabled(menu, MenuCommandId.Rename));
        Assert.False(Enabled(menu, MenuCommandId.Properties));
        Assert.False(Enabled(menu, MenuCommandId.Open));
        // Batch verbs stay live.
        Assert.True(Enabled(menu, MenuCommandId.Delete));
    }

    [Fact]
    public void SelectionMenu_OnFolder_HidesOpenWith_AndOffersBookmark() {
        var menu = ContextMenuBuilder.Build(SelectionOf(Dir("sub")), ContextMenuSettings.Default);

        Assert.False(Enabled(menu, MenuCommandId.OpenWith));
        Assert.True(Enabled(menu, MenuCommandId.AddBookmark));
    }

    [Fact]
    public void SelectionMenu_OnFolderShortcut_DoesNotOfferBookmark() {
        // A .lnk is a file whose target is a folder; bookmarking it would
        // store the link's own path, which navigation can't use.
        var shortcut = new FileSystemEntry(
            "sub.lnk", System.IO.Path.Combine(Folder, "sub.lnk"), EntryKind.File, 1, DateTime.UnixEpoch,
            false, false, false, LinksToDirectory: true);

        var menu = ContextMenuBuilder.Build(SelectionOf(shortcut), ContextMenuSettings.Default);

        Assert.False(Enabled(menu, MenuCommandId.AddBookmark));
    }


    [Fact]
    public void SelectionMenu_OnFile_DoesNotOfferBookmark() {
        var menu = ContextMenuBuilder.Build(SelectionOf(File("a.txt")), ContextMenuSettings.Default);

        Assert.False(Enabled(menu, MenuCommandId.AddBookmark));
    }

    [Fact]
    public void SelectionMenu_PasteFollowsClipboardState() {
        var target = SelectionOf(File("a.txt")) with { CanPaste = true };

        var withContent = ContextMenuBuilder.Build(target, ContextMenuSettings.Default);
        var withoutContent = ContextMenuBuilder.Build(
            target with { CanPaste = false }, ContextMenuSettings.Default);

        Assert.True(Enabled(Find(withContent, MenuCommandId.FileSubmenu)!.Children, MenuCommandId.Paste));
        Assert.False(Enabled(Find(withoutContent, MenuCommandId.FileSubmenu)!.Children, MenuCommandId.Paste));
    }

    [Fact]
    public void ReadOnlyLocation_DisablesEveryFilesystemVerb() {
        var target = SelectionOf(File("a.txt")) with { IsReadOnlyLocation = true };

        var menu = ContextMenuBuilder.Build(target, ContextMenuSettings.Default);
        var file = Find(menu, MenuCommandId.FileSubmenu)!.Children;

        Assert.False(Enabled(menu, MenuCommandId.Delete));
        Assert.False(Enabled(menu, MenuCommandId.PermanentDelete));
        Assert.False(Enabled(menu, MenuCommandId.Rename));
        Assert.False(Enabled(file, MenuCommandId.Cut));
        Assert.False(Enabled(file, MenuCommandId.CreateShortcut));
        // Reading is still fine — that's the whole point of browsing the bin.
        Assert.True(Enabled(file, MenuCommandId.CopyPath));
    }


    // --- Background menu ------------------------------------------------

    [Fact]
    public void BackgroundMenu_LeadsWithPasteAndNewFolder() {
        var menu = ContextMenuBuilder.Build(Background() with { CanPaste = true }, ContextMenuSettings.Default);

        Assert.Equal(MenuCommandId.Paste, menu[0].Id);
        Assert.Equal(MenuCommandId.NewFolder, menu[1].Id);
    }

    [Fact]
    public void BackgroundMenu_HasNoSelectionVerbs() {
        var menu = ContextMenuBuilder.Build(Background(), ContextMenuSettings.Default);

        Assert.Null(Find(menu, MenuCommandId.Rename));
        Assert.Null(Find(menu, MenuCommandId.Delete));
        Assert.Null(Find(menu, MenuCommandId.OpenWith));
    }

    [Fact]
    public void BackgroundMenu_ChecksTheActiveViewModeAndSortKey() {
        var target = Background() with {
            ViewMode = "Tiles",
            SortKey = SortKey.Size,
            SortAscending = false,
        };

        var menu = ContextMenuBuilder.Build(target, ContextMenuSettings.Default);
        var view = Find(menu, MenuCommandId.ViewSubmenu)!.Children;
        var sort = Find(menu, MenuCommandId.SortSubmenu)!.Children;

        Assert.True(Find(view, MenuCommandId.ViewTiles)!.IsChecked);
        Assert.False(Find(view, MenuCommandId.ViewDetails)!.IsChecked);
        Assert.True(Find(sort, MenuCommandId.SortBySize)!.IsChecked);
        Assert.False(Find(sort, MenuCommandId.SortAscending)!.IsChecked);
    }

    [Fact]
    public void BackgroundMenu_UndoFollowsTheUndoStack() {
        Assert.False(Enabled(
            ContextMenuBuilder.Build(Background(), ContextMenuSettings.Default), MenuCommandId.Undo));
        Assert.True(Enabled(
            ContextMenuBuilder.Build(Background() with { CanUndo = true }, ContextMenuSettings.Default),
            MenuCommandId.Undo));
    }


    // --- Hiding ----------------------------------------------------------

    [Fact]
    public void HiddenItems_AreRemovedNotDisabled() {
        var settings = Hiding(MenuCommandId.PermanentDelete);

        var menu = ContextMenuBuilder.Build(SelectionOf(File("a.txt")), settings);

        Assert.Null(Find(menu, MenuCommandId.PermanentDelete));
        Assert.NotNull(Find(menu, MenuCommandId.Delete));
    }

    [Fact]
    public void HidingASubmenuHeader_TakesItsChildrenWithIt() {
        var menu = ContextMenuBuilder.Build(
            SelectionOf(File("a.txt")), Hiding(MenuCommandId.FileSubmenu));

        Assert.Null(Find(menu, MenuCommandId.FileSubmenu));
    }

    [Fact]
    public void HidingEverySubmenuChild_DropsTheSubmenu() {
        var settings = Hiding(
            MenuCommandId.Cut, MenuCommandId.Copy, MenuCommandId.Paste,
            MenuCommandId.CopyPath, MenuCommandId.CopyName, MenuCommandId.CreateShortcut);

        var menu = ContextMenuBuilder.Build(SelectionOf(File("a.txt")), settings);

        Assert.Null(Find(menu, MenuCommandId.FileSubmenu));
    }

    [Fact]
    public void Hiding_LeavesNoDanglingSeparators() {
        // Empty out the whole middle of the menu and check the dividers that
        // framed those groups went with them.
        var settings = Hiding(
            MenuCommandId.FileSubmenu, MenuCommandId.Rename, MenuCommandId.Delete,
            MenuCommandId.PermanentDelete, MenuCommandId.AddBookmark,
            MenuCommandId.OpenInExplorer, MenuCommandId.OpenInTerminal);

        var menu = ContextMenuBuilder.Build(SelectionOf(File("a.txt")), settings);

        AssertSeparatorsAreSane(menu);
    }

    [Fact]
    public void UnknownPersistedName_IsIgnoredRatherThanFatal() {
        var settings = ContextMenuSettings.From(new Persistence.AppSettings {
            HiddenContextMenuItems = new[] { "Delete", "SomeVerbFromTheFuture", "" },
        });

        Assert.Contains(MenuCommandId.Delete, settings.HiddenItems);
        Assert.Single(settings.HiddenItems);
    }


    // --- Shell extensions -------------------------------------------------

    [Fact]
    public void ShellItems_AreAppendedAboveProperties() {
        var menu = ContextMenuBuilder.Build(
            SelectionOf(File("a.txt")), ContextMenuSettings.Default,
            new[] { ShellItem(0, "7-Zip"), ShellItem(1, "TortoiseGit") });

        int sevenZip = IndexOfHeader(menu, "7-Zip");
        int properties = menu.ToList().FindIndex(e => e.Id == MenuCommandId.Properties);

        Assert.True(sevenZip >= 0);
        Assert.True(sevenZip < properties);
    }

    [Fact]
    public void ShellItems_AreDroppedWhenExtensionsAreOff() {
        var settings = ContextMenuSettings.Default with { ShellExtensionsEnabled = false };

        var menu = ContextMenuBuilder.Build(
            SelectionOf(File("a.txt")), settings, new[] { ShellItem(0, "7-Zip") });

        Assert.Equal(-1, IndexOfHeader(menu, "7-Zip"));
    }

    [Fact]
    public void BlockedShellItem_IsDroppedWhileTheRestSurvive() {
        var settings = ContextMenuSettings.From(new Persistence.AppSettings {
            BlockedShellExtensions = new[] { "7-Zip" },
        });

        var menu = ContextMenuBuilder.Build(
            SelectionOf(File("a.txt")), settings,
            new[] { ShellItem(0, "7-Zip"), ShellItem(1, "TortoiseGit") });

        Assert.Equal(-1, IndexOfHeader(menu, "7-Zip"));
        Assert.True(IndexOfHeader(menu, "TortoiseGit") >= 0);
    }

    [Fact]
    public void BlockedShellItem_MatchesPastWin32Decoration() {
        var settings = ContextMenuSettings.From(new Persistence.AppSettings {
            BlockedShellExtensions = new[] { "7-Zip" },
        });

        var menu = ContextMenuBuilder.Build(
            SelectionOf(File("a.txt")), settings, new[] { ShellItem(0, "7-Zip...") });

        Assert.Equal(-1, IndexOfHeader(menu, "7-Zip..."));
    }

    [Fact]
    public void ShellSubmenuMode_FoldsEverythingUnderOneHeader() {
        var settings = ContextMenuSettings.Default with { ShellExtensionsInSubmenu = true };

        var menu = ContextMenuBuilder.Build(
            SelectionOf(File("a.txt")), settings,
            new[] { ShellItem(0, "7-Zip"), ShellItem(1, "TortoiseGit") });

        var more = Find(menu, MenuCommandId.ShellSubmenu);
        Assert.NotNull(more);
        Assert.Equal(2, more!.Children.Count);
        Assert.Equal(-1, IndexOfHeader(menu, "7-Zip"));
    }

    [Fact]
    public void ShellSubmenuHeader_IsNotItselfInvokable() {
        var menu = ContextMenuBuilder.Build(
            SelectionOf(File("a.txt")), ContextMenuSettings.Default,
            new[] { ShellItem(0, "7-Zip", ShellItem(1, "Add to archive")) });

        var sevenZip = menu.First(e => e.Header == "7-Zip");

        Assert.False(sevenZip.IsShellCommand);
        Assert.Equal(1, sevenZip.Children[0].ShellCommand);
    }

    [Fact]
    public void ShellSeparators_DoNotLeakIntoTheMenuEdges() {
        var menu = ContextMenuBuilder.Build(
            SelectionOf(File("a.txt")), ContextMenuSettings.Default,
            new[] {
                new ShellMenuEntry { IsSeparator = true },
                ShellItem(0, "7-Zip"),
                new ShellMenuEntry { IsSeparator = true },
                new ShellMenuEntry { IsSeparator = true },
            });

        AssertSeparatorsAreSane(menu);
    }

    [Fact]
    public void EveryShellItemBlocked_LeavesTheMenuIntact() {
        var settings = ContextMenuSettings.From(new Persistence.AppSettings {
            BlockedShellExtensions = new[] { "7-Zip" },
        });

        var menu = ContextMenuBuilder.Build(
            SelectionOf(File("a.txt")), settings, new[] { ShellItem(0, "7-Zip") });

        AssertSeparatorsAreSane(menu);
        Assert.NotNull(Find(menu, MenuCommandId.Properties));
    }


    // --- Helpers ----------------------------------------------------------

    private static void AssertSeparatorsAreSane(IReadOnlyList<MenuEntry> menu) {
        Assert.NotEmpty(menu);
        Assert.False(menu[0].IsSeparator, "menu starts with a separator");
        Assert.False(menu[^1].IsSeparator, "menu ends with a separator");
        for (int i = 1; i < menu.Count; i++) {
            Assert.False(menu[i].IsSeparator && menu[i - 1].IsSeparator, $"double separator at {i}");
        }
    }

    private static ContextMenuTarget SelectionOf(params FileSystemEntry[] entries) {
        return new ContextMenuTarget { Selection = entries, FolderPath = Folder };
    }

    private static ContextMenuTarget Background() {
        return new ContextMenuTarget { FolderPath = Folder, IsBackground = true };
    }

    private static ContextMenuSettings Hiding(params MenuCommandId[] ids) {
        return ContextMenuSettings.Default with { HiddenItems = new HashSet<MenuCommandId>(ids) };
    }

    private static FileSystemEntry File(string name) {
        return new FileSystemEntry(
            name, System.IO.Path.Combine(Folder, name), EntryKind.File, 10, DateTime.UnixEpoch,
            false, false, false, false);
    }

    private static FileSystemEntry Dir(string name) {
        return new FileSystemEntry(
            name, System.IO.Path.Combine(Folder, name), EntryKind.Directory, null, DateTime.UnixEpoch,
            false, false, false, false);
    }

    private static ShellMenuEntry ShellItem(int id, string header, params ShellMenuEntry[] children) {
        return new ShellMenuEntry { CommandId = id, Header = header, Children = children };
    }

    private static MenuEntry? Find(IReadOnlyList<MenuEntry> menu, MenuCommandId id) {
        return menu.FirstOrDefault(e => e.Id == id);
    }

    private static bool Enabled(IReadOnlyList<MenuEntry> menu, MenuCommandId id) {
        return Find(menu, id) is { IsEnabled: true };
    }

    private static int IndexOfHeader(IReadOnlyList<MenuEntry> menu, string header) {
        return menu.ToList().FindIndex(e => e.Header == header);
    }
}
