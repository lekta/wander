using Wander.Core.FileSystem;
using Wander.Core.Menu;
using Wander.Core.Shell;

namespace Wander.Core.Tests;

public class ContextMenuBuilderTests {
    private const string Folder = @"C:\work";


    // --- Selection menu: layout ------------------------------------------

    [Fact]
    public void SelectionMenu_LeadsWithOpen() {
        var menu = ContextMenuBuilder.Build(SelectionOf(File("a.txt")), ContextMenuSettings.Default);

        Assert.Equal(MenuCommandId.Open, menu[0].Id);
        Assert.True(menu[0].IsDefault);
        Assert.Equal(MenuCommandId.OpenSubmenu, menu[1].Id);
    }

    [Fact]
    public void SelectionMenu_EndsWithProperties() {
        var menu = ContextMenuBuilder.Build(SelectionOf(File("a.txt")), ContextMenuSettings.Default);

        Assert.Equal(MenuCommandId.Properties, menu[^1].Id);
    }

    [Fact]
    public void SelectionMenu_KeepsFileOperationsBelowTheShellBlock() {
        // The whole point of the layout: what the menu was opened *for*
        // (edit in ..., extract here) is at the top, Wander's own rarer
        // file verbs wait at the bottom.
        var menu = ContextMenuBuilder.Build(
            SelectionOf(File("a.txt")), ContextMenuSettings.Default,
            new[] { ShellItem(0, "7-Zip") });

        Assert.True(IndexOfHeader(menu, "7-Zip") < IndexOf(menu, MenuCommandId.FileSubmenu));
        Assert.True(IndexOf(menu, MenuCommandId.OpenSubmenu) < IndexOfHeader(menu, "7-Zip"));
    }

    [Fact]
    public void SelectionMenu_FallsBackToOurOwnOpenWithPicker() {
        var menu = ContextMenuBuilder.Build(SelectionOf(File("a.txt")), ContextMenuSettings.Default);

        var open = Find(menu, MenuCommandId.OpenSubmenu);
        Assert.NotNull(open);
        Assert.NotNull(Find(open!.Children, MenuCommandId.OpenWith));
        // Nothing from that group is left loose at the top level.
        Assert.Null(Find(menu, MenuCommandId.OpenWith));
    }

    [Fact]
    public void ShellOpenWithPopup_IsPouredIntoOurOpenSubmenu() {
        // The shell's list of apps is richer than anything we could build,
        // so it replaces our single "choose an app" row rather than sitting
        // next to it as a second identically-named popup.
        var openWith = new ShellMenuEntry {
            Header = "Открыть с помощью",
            Verb = "openas",
            Children = new[] { ShellItem(5, "Paint"), ShellItem(6, "Krita") },
        };

        var menu = ContextMenuBuilder.Build(
            SelectionOf(File("a.txt")), ContextMenuSettings.Default, new[] { openWith });

        var open = Find(menu, MenuCommandId.OpenSubmenu)!;
        Assert.Equal(new[] { "Paint", "Krita" }, open.Children.Select(c => c.Header));
        Assert.Null(Find(open.Children, MenuCommandId.OpenWith));
        // Ours carries the same title, so the test is that there is exactly
        // one of them — not two identically-named popups side by side.
        Assert.Single(menu, e => e.Header == open.Header);
    }

    [Fact]
    public void OpenInTerminal_IsOfferedForAFolderOnly() {
        var onFolder = ContextMenuBuilder.Build(SelectionOf(Dir("sub")), ContextMenuSettings.Default);
        var onFile = ContextMenuBuilder.Build(SelectionOf(File("a.txt")), ContextMenuSettings.Default);
        var onMany = ContextMenuBuilder.Build(
            SelectionOf(Dir("a"), Dir("b")), ContextMenuSettings.Default);

        Assert.True(Enabled(onFolder, MenuCommandId.OpenInTerminal));
        // On a file it would silently mean "the folder it sits in" — dropped
        // rather than greyed, because the row would be lying.
        Assert.DoesNotContain(Flatten(onFile), e => e.Id == MenuCommandId.OpenInTerminal);
        Assert.DoesNotContain(Flatten(onMany), e => e.Id == MenuCommandId.OpenInTerminal);
    }

    [Fact]
    public void SelectionMenu_PutsEveryFileVerbInTheFileSubmenu() {
        var menu = ContextMenuBuilder.Build(SelectionOf(File("a.txt")), ContextMenuSettings.Default);

        var file = Find(menu, MenuCommandId.FileSubmenu);
        Assert.NotNull(file);
        foreach (var id in new[] {
            MenuCommandId.Cut, MenuCommandId.Copy, MenuCommandId.Paste,
            MenuCommandId.CopyPath, MenuCommandId.CopyName,
            MenuCommandId.Rename, MenuCommandId.CreateShortcut, MenuCommandId.Delete,
        }) {
            Assert.NotNull(Find(file!.Children, id));
            Assert.Null(Find(menu, id));
        }
    }

    [Fact]
    public void SelectionMenu_HasNoPermanentDelete() {
        // Deliberately hotkey-only (Shift+Del): Wander leans safe, and an
        // unundoable verb has no business one slip away from Delete.
        var menu = ContextMenuBuilder.Build(SelectionOf(File("a.txt")), ContextMenuSettings.Default);

        Assert.DoesNotContain(Flatten(menu), e => e.Header.Contains("безвозвратно"));
    }


    // --- Selection menu: enablement ---------------------------------------

    [Fact]
    public void SelectionMenu_OnMultipleItems_DisablesSingleOnlyVerbs() {
        var menu = ContextMenuBuilder.Build(
            SelectionOf(File("a.txt"), File("b.txt")), ContextMenuSettings.Default);
        var file = Find(menu, MenuCommandId.FileSubmenu)!.Children;

        Assert.False(Enabled(menu, MenuCommandId.Open));
        Assert.False(Enabled(menu, MenuCommandId.Properties));
        Assert.False(Enabled(file, MenuCommandId.Rename));
        // Batch verbs stay live.
        Assert.True(Enabled(file, MenuCommandId.Delete));
        Assert.True(Enabled(file, MenuCommandId.Copy));
    }

    [Fact]
    public void SelectionMenu_OnFolder_DropsTheOpenWithSubmenu() {
        // Its only row would be a disabled "choose an app", so the whole
        // submenu falls away rather than sitting there greyed.
        var menu = ContextMenuBuilder.Build(SelectionOf(Dir("sub")), ContextMenuSettings.Default);

        Assert.Null(Find(menu, MenuCommandId.OpenSubmenu));
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

        Assert.False(Enabled(file, MenuCommandId.Delete));
        Assert.False(Enabled(file, MenuCommandId.Rename));
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

        Assert.Null(Find(menu, MenuCommandId.FileSubmenu));
        Assert.DoesNotContain(Flatten(menu), e => e.Id == MenuCommandId.Rename);
        Assert.DoesNotContain(Flatten(menu), e => e.Id == MenuCommandId.Delete);
        Assert.DoesNotContain(Flatten(menu), e => e.Id == MenuCommandId.OpenWith);
    }

    [Fact]
    public void BackgroundMenu_OffersTerminalForTheListedFolder() {
        var menu = ContextMenuBuilder.Build(Background(), ContextMenuSettings.Default);

        Assert.True(Enabled(menu, MenuCommandId.OpenInTerminal));
        Assert.True(Enabled(menu, MenuCommandId.CopyPath));
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
    public void BackgroundMenu_RendersShellFileVerbsInline() {
        // There is no File submenu on the background menu, so the folder's
        // own shell verbs stay at the top level rather than getting a
        // one-item container invented for them.
        var restore = new ShellMenuEntry {
            CommandId = 9, Header = "Восстановить прежнюю версию", Verb = "PreviousVersions",
        };

        var menu = ContextMenuBuilder.Build(Background(), ContextMenuSettings.Default, new[] { restore });

        Assert.Null(Find(menu, MenuCommandId.FileSubmenu));
        Assert.True(IndexOfHeader(menu, "Восстановить прежнюю версию") >= 0);
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
        var menu = ContextMenuBuilder.Build(
            SelectionOf(File("a.txt")), Hiding(MenuCommandId.CopyName));
        var file = Find(menu, MenuCommandId.FileSubmenu)!.Children;

        Assert.Null(Find(file, MenuCommandId.CopyName));
        Assert.NotNull(Find(file, MenuCommandId.CopyPath));
    }

    [Fact]
    public void HidingASubmenuHeader_TakesItsChildrenWithIt() {
        var menu = ContextMenuBuilder.Build(
            SelectionOf(File("a.txt")), Hiding(MenuCommandId.FileSubmenu));

        Assert.Null(Find(menu, MenuCommandId.FileSubmenu));
        Assert.DoesNotContain(Flatten(menu), e => e.Id == MenuCommandId.Cut);
    }

    [Fact]
    public void HidingTheOpenSubmenu_TakesTheWholeOpenGroup() {
        var menu = ContextMenuBuilder.Build(
            SelectionOf(File("a.txt")), Hiding(MenuCommandId.OpenSubmenu));

        Assert.Null(Find(menu, MenuCommandId.OpenSubmenu));
        Assert.DoesNotContain(Flatten(menu), e => e.Id == MenuCommandId.OpenWith);
        // The plain Open verb is a separate entry and survives.
        Assert.NotNull(Find(menu, MenuCommandId.Open));
    }

    [Fact]
    public void HidingEverySubmenuChild_DropsTheSubmenu() {
        var settings = Hiding(
            MenuCommandId.Cut, MenuCommandId.Copy, MenuCommandId.Paste,
            MenuCommandId.CopyPath, MenuCommandId.CopyName,
            MenuCommandId.Rename, MenuCommandId.CreateShortcut, MenuCommandId.Delete);

        var menu = ContextMenuBuilder.Build(SelectionOf(File("a.txt")), settings);

        Assert.Null(Find(menu, MenuCommandId.FileSubmenu));
    }

    [Fact]
    public void Hiding_LeavesNoDanglingSeparators() {
        // Empty out the whole middle of the menu and check the dividers that
        // framed those groups went with them.
        var settings = Hiding(
            MenuCommandId.OpenSubmenu, MenuCommandId.FileSubmenu, MenuCommandId.Open);

        var menu = ContextMenuBuilder.Build(SelectionOf(File("a.txt")), settings);

        AssertSeparatorsAreSane(menu);
        Assert.Single(menu);
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
    public void BlockedShellItem_IsDroppedEvenWhenItIsAFileVerb() {
        // Blocking is applied before placement, so a name the user switched
        // off never reappears just because it belongs in the File submenu.
        var settings = ContextMenuSettings.From(new Persistence.AppSettings {
            BlockedShellExtensions = new[] { "Отправить" },
        });
        var sendTo = new ShellMenuEntry {
            Header = "Отправить",
            Children = new[] { new ShellMenuEntry { CommandId = 3, Header = "Документы" } },
        };

        var menu = ContextMenuBuilder.Build(SelectionOf(File("a.txt")), settings, new[] { sendTo });

        Assert.DoesNotContain(Flatten(menu), e => e.Header == "Отправить");
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
    public void ShellFileVerbs_MoveIntoTheFileSubmenu() {
        var restore = new ShellMenuEntry {
            CommandId = 9, Header = "Восстановить прежнюю версию", Verb = "PreviousVersions",
        };

        var menu = ContextMenuBuilder.Build(
            SelectionOf(File("a.txt")), ContextMenuSettings.Default, new[] { restore });

        var file = Find(menu, MenuCommandId.FileSubmenu)!;
        Assert.Contains(file.Children, e => e.Header == "Восстановить прежнюю версию");
        Assert.Equal(-1, IndexOfHeader(menu, "Восстановить прежнюю версию"));
    }

    [Fact]
    public void VerblessShellLeaf_MovesIntoTheFileSubmenu() {
        // "Проверка с использованием Microsoft Defender" publishes no
        // canonical verb at all — measured, not assumed.
        var defender = new ShellMenuEntry { CommandId = 7, Header = "Проверка с Defender..." };

        var menu = ContextMenuBuilder.Build(
            SelectionOf(File("a.txt")), ContextMenuSettings.Default, new[] { defender });

        var file = Find(menu, MenuCommandId.FileSubmenu)!;
        Assert.Contains(file.Children, e => e.Header == "Проверка с Defender...");
        Assert.Equal(-1, IndexOfHeader(menu, "Проверка с Defender..."));
    }

    [Fact]
    public void DynamicShellPopups_MoveIntoTheFileSubmenu() {
        // "Отправить" / "Передать на устройство": the shell fills these at
        // popup time, so nothing inside carries a canonical verb. That shape
        // is what tells them apart from a third-party handler's own menu.
        var sendTo = new ShellMenuEntry {
            Header = "Отправить",
            Children = new[] {
                new ShellMenuEntry { CommandId = 3, Header = "Документы" },
                new ShellMenuEntry { CommandId = 4, Header = "Устройство Bluetooth" },
            },
        };

        var menu = ContextMenuBuilder.Build(
            SelectionOf(File("a.txt")), ContextMenuSettings.Default, new[] { sendTo });

        var file = Find(menu, MenuCommandId.FileSubmenu)!;
        Assert.Contains(file.Children, e => e.Header == "Отправить");
        Assert.Equal(-1, IndexOfHeader(menu, "Отправить"));
    }

    [Fact]
    public void ThirdPartyPopup_StaysAtTheTopLevel() {
        // 7-Zip registers verbs for its entries, so it is not mistaken for
        // one of the shell's own dynamic popups.
        var sevenZip = new ShellMenuEntry {
            Header = "7-Zip",
            Children = new[] {
                new ShellMenuEntry { CommandId = 3, Header = "Распаковать здесь", Verb = "SevenZipExtract" },
            },
        };

        var menu = ContextMenuBuilder.Build(
            SelectionOf(File("a.txt")), ContextMenuSettings.Default, new[] { sevenZip });

        Assert.True(IndexOfHeader(menu, "7-Zip") >= 0);
        Assert.DoesNotContain(Find(menu, MenuCommandId.FileSubmenu)!.Children, e => e.Header == "7-Zip");
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

    private static IEnumerable<MenuEntry> Flatten(IReadOnlyList<MenuEntry> menu) {
        foreach (var entry in menu) {
            yield return entry;
            foreach (var child in Flatten(entry.Children)) {
                yield return child;
            }
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

    /// <summary>
    /// A third-party entry. The verb defaults to something non-empty on
    /// purpose: an entry without one reads as the shell's own dynamic popup
    /// and gets filed under file operations.
    /// </summary>
    private static ShellMenuEntry ShellItem(int id, string header, params ShellMenuEntry[] children) {
        return new ShellMenuEntry {
            CommandId = id,
            Header = header,
            Verb = children.Length == 0 ? "handler." + id : string.Empty,
            Children = children,
        };
    }

    private static MenuEntry? Find(IReadOnlyList<MenuEntry> menu, MenuCommandId id) {
        return menu.FirstOrDefault(e => e.Id == id);
    }

    private static bool Enabled(IReadOnlyList<MenuEntry> menu, MenuCommandId id) {
        return Find(menu, id) is { IsEnabled: true };
    }

    private static int IndexOf(IReadOnlyList<MenuEntry> menu, MenuCommandId id) {
        return menu.ToList().FindIndex(e => e.Id == id);
    }

    private static int IndexOfHeader(IReadOnlyList<MenuEntry> menu, string header) {
        return menu.ToList().FindIndex(e => e.Header == header);
    }
}
