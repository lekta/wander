using Wander.Core.Actions;
using Wander.Core.FileSystem;
using Wander.Core.Menu;
using Wander.Core.Rename;
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


    // --- Inside an archive ------------------------------------------------

    [Fact]
    public void InsideArchive_OffersFourVerbsAndNothingThatWrites() {
        var target = SelectionOf(File("readme.txt")) with { IsReadOnlyLocation = true, IsArchive = true };

        var menu = ContextMenuBuilder.Build(target, ContextMenuSettings.Default);

        Assert.Equal(MenuCommandId.Open, menu[0].Id);
        Assert.True(menu[0].IsDefault);
        Assert.NotNull(Find(menu, MenuCommandId.Copy));
        Assert.NotNull(Find(menu, MenuCommandId.Extract));
        Assert.NotNull(Find(menu, MenuCommandId.CopyPath));
        // Not greyed out - absent. There is no writing into an archive at
        // all, so a disabled row would be promising something for later.
        Assert.Null(Find(menu, MenuCommandId.FileSubmenu));
        Assert.Null(Find(menu, MenuCommandId.Delete));
        Assert.Null(Find(menu, MenuCommandId.Rename));
        Assert.Null(Find(menu, MenuCommandId.Cut));
        Assert.Null(Find(menu, MenuCommandId.Paste));
    }

    [Fact]
    public void InsideArchive_BackgroundOffersThePathAndNothingElse() {
        var target = Background() with { IsReadOnlyLocation = true, IsArchive = true };

        var menu = ContextMenuBuilder.Build(target, ContextMenuSettings.Default);

        Assert.Equal(MenuCommandId.CopyPath, Assert.Single(menu).Id);
    }

    [Fact]
    public void ArchiveInAnOrdinaryFolder_GetsExtractOnTopOfTheUsualMenu() {
        var target = SelectionOf(File("pack.zip")) with { SelectionIsArchive = true };

        var menu = ContextMenuBuilder.Build(target, ContextMenuSettings.Default);

        Assert.NotNull(Find(menu, MenuCommandId.Extract));
        // Still a file: everything a file can do is still on the menu.
        Assert.NotNull(Find(menu, MenuCommandId.FileSubmenu));
        Assert.Equal(MenuCommandId.Properties, menu[^1].Id);
    }

    [Fact]
    public void OrdinaryFile_HasNoExtractRow() {
        var menu = ContextMenuBuilder.Build(SelectionOf(File("a.txt")), ContextMenuSettings.Default);

        Assert.Null(Find(menu, MenuCommandId.Extract));
    }


    // --- Background menu ------------------------------------------------

    [Fact]
    public void BackgroundMenu_LeadsWithTheNewSubmenu() {
        var menu = ContextMenuBuilder.Build(Background(), ContextMenuSettings.Default);

        Assert.Equal(MenuCommandId.NewSubmenu, menu[0].Id);
        Assert.Equal(MenuCommandId.NewFolder, menu[0].Children[0].Id);
    }

    [Fact]
    public void BackgroundMenu_IsFolderVerbsOnly() {
        // View mode, sorting, refresh, undo and paste were all here once.
        // They are window-wide state, they live in the toolbar's "Вид" menu
        // and on hotkeys, and a right-click on a folder is not where anyone
        // goes looking for them. What is left acts on the folder itself.
        var menu = ContextMenuBuilder.Build(Background() with { CanPaste = true }, ContextMenuSettings.Default);

        Assert.Equal(
            new[] {
                MenuCommandId.NewSubmenu,
                MenuCommandId.OpenInTerminal,
                MenuCommandId.CopyPath,
                MenuCommandId.Properties,
            },
            menu.Where(e => !e.IsSeparator).Select(e => e.Id).ToArray());
        Assert.DoesNotContain(Flatten(menu), e => e.Id == MenuCommandId.Paste);
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
    public void ShellNewPopup_IsPouredIntoOurCreateSubmenu() {
        // Windows contributes its own "Создать" for a folder background. Left
        // alone it sits next to ours and the menu shows the word twice, so it
        // is folded in — and its folder row, which duplicates ours, is cut.
        var shellNew = new ShellMenuEntry {
            Header = "Создать",
            Children = new[] {
                new ShellMenuEntry { CommandId = 1, Header = "Папку", Verb = "NewFolder" },
                new ShellMenuEntry { CommandId = 2, Header = "Ярлык", Verb = "NewLink" },
                new ShellMenuEntry { IsSeparator = true },
                new ShellMenuEntry { CommandId = 3, Header = "Текстовый документ", Verb = ".txt" },
            },
        };

        var menu = ContextMenuBuilder.Build(Background(), ContextMenuSettings.Default, new[] { shellNew });

        // One container, ours, and the shell's is gone from the top level.
        Assert.Single(menu.Where(e => !e.IsSeparator), e => e.Id == MenuCommandId.NewSubmenu);
        Assert.Equal(-1, IndexOfHeader(menu, "Создать"));

        var create = Find(menu, MenuCommandId.NewSubmenu)!.Children;
        Assert.Equal(MenuCommandId.NewFolder, create[0].Id);
        Assert.True(IndexOfHeader(create, "Ярлык") > 0);
        Assert.True(IndexOfHeader(create, "Текстовый документ") > 0);
        // The shell's own folder row is the one thing dropped.
        Assert.Equal(-1, IndexOfHeader(create, "Папку"));
    }

    [Fact]
    public void ShellPopupWithoutTheNewFolderVerb_StaysWhereItWas() {
        // The signature is the canonical verb, not the word "Создать": a
        // third-party submenu that happens to be called that must not have
        // its contents swallowed.
        var impostor = new ShellMenuEntry {
            Header = "Создать",
            Children = new[] {
                new ShellMenuEntry { CommandId = 1, Header = "Архив", Verb = "compress" },
            },
        };

        var menu = ContextMenuBuilder.Build(Background(), ContextMenuSettings.Default, new[] { impostor });

        Assert.True(IndexOfHeader(menu, "Создать") > 0);
        Assert.Equal(-1, IndexOfHeader(Find(menu, MenuCommandId.NewSubmenu)!.Children, "Архив"));
    }

    [Fact]
    public void BackgroundMenu_DropsTheNewSubmenuWhereNothingCanBeCreated() {
        // Read-only location: "Создать" would hold one disabled row, which is
        // a submenu that exists to say no. Normalize takes it out instead.
        var menu = ContextMenuBuilder.Build(
            Background() with { IsReadOnlyLocation = true },
            ContextMenuSettings.Default with { HiddenItems = new HashSet<MenuCommandId> { MenuCommandId.NewFolder } });

        Assert.Null(Find(menu, MenuCommandId.NewSubmenu));
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
    public void Normalize_DropsASubmenuHeaderBuiltWithoutRows() {
        // Built empty rather than emptied by hiding: without the rule it
        // stayed as a leaf named like a submenu, doing nothing on a click.
        var items = new[] {
            new MenuEntry { Id = MenuCommandId.Open, Header = "Open" },
            MenuEntry.Divider,
            ContextMenuBuilder.Sub(MenuCommandId.ActionsSubmenu, Array.Empty<MenuEntry>()),
            MenuEntry.Divider,
            new MenuEntry { Id = MenuCommandId.Properties, Header = "Properties" },
        };

        var menu = ContextMenuBuilder.Normalize(items, ContextMenuSettings.Default);

        Assert.Null(Find(menu, MenuCommandId.ActionsSubmenu));
        Assert.Equal(new[] { MenuCommandId.Open, MenuCommandId.None, MenuCommandId.Properties }, menu.Select(e => e.Id));
        Assert.True(menu[1].IsSeparator);
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


    // --- Batch rename ---------------------------------------------------

    [Fact]
    public void BatchRename_AppearsInFile_ForTwoOrMoreOfOneKind() {
        var two = ContextMenuBuilder.Build(SelectionOf(File("a.txt"), File("b.txt")), ContextMenuSettings.Default);
        var folders = ContextMenuBuilder.Build(SelectionOf(Dir("a"), Dir("b")), ContextMenuSettings.Default);

        Assert.True(Enabled(Find(two, MenuCommandId.FileSubmenu)!.Children, MenuCommandId.BatchRename));
        Assert.True(Enabled(Find(folders, MenuCommandId.FileSubmenu)!.Children, MenuCommandId.BatchRename));
        // The in-place rename stays greyed beside it, as before.
        Assert.False(Enabled(Find(two, MenuCommandId.FileSubmenu)!.Children, MenuCommandId.Rename));
    }

    [Fact]
    public void BatchRename_IsAbsent_ForOneItem_ForAMix_AndInTheBin() {
        var one = ContextMenuBuilder.Build(SelectionOf(File("a.txt")), ContextMenuSettings.Default);
        var mixed = ContextMenuBuilder.Build(SelectionOf(File("a.txt"), Dir("b")), ContextMenuSettings.Default);
        var bin = ContextMenuBuilder.Build(
            SelectionOf(File("a.txt"), File("b.txt")) with { IsReadOnlyLocation = true, IsRecycleBin = true },
            ContextMenuSettings.Default);

        Assert.Null(Find(Find(one, MenuCommandId.FileSubmenu)!.Children, MenuCommandId.BatchRename));
        Assert.Null(Find(Find(mixed, MenuCommandId.FileSubmenu)!.Children, MenuCommandId.BatchRename));
        Assert.Null(Find(Find(bin, MenuCommandId.FileSubmenu)!.Children, MenuCommandId.BatchRename));
    }


    // --- Custom actions in the context menu --------------------------------

    [Fact]
    public void ContextMenu_ShowsOnlyTheActionsThatApply() {
        var target = SelectionOf(File("a.mp4")) with { Actions = new[] { _forVideo, _forImages, _forAll } };

        var menu = ContextMenuBuilder.Build(target, ContextMenuSettings.Default);
        var actions = Find(menu, MenuCommandId.ActionsSubmenu)!.Children;

        Assert.Equal(new[] { _forVideo.Id, _forAll.Id }, actions.Select(e => e.Argument));
        Assert.All(actions, e => Assert.Equal(MenuCommandId.RunAction, e.Id));
        Assert.All(actions, e => Assert.True(e.IsEnabled));
        Assert.Equal(_forVideo.Title, actions[0].Header);
    }

    [Fact]
    public void ContextMenu_DropsTheSubmenu_WhenNothingApplies() {
        var target = SelectionOf(File("a.txt")) with { Actions = new[] { _forVideo } };

        var menu = ContextMenuBuilder.Build(target, ContextMenuSettings.Default);

        Assert.Null(Find(menu, MenuCommandId.ActionsSubmenu));
        Assert.Null(Find(menu, MenuCommandId.ConvertSubmenu));
        AssertSeparatorsAreSane(menu);
    }

    [Fact]
    public void ContextMenu_ActionsSitBetweenTheShellBlockAndFile() {
        var target = SelectionOf(File("a.mp4")) with { Actions = new[] { _forVideo } };

        var menu = ContextMenuBuilder.Build(target, ContextMenuSettings.Default, new[] { ShellItem(0, "7-Zip") });

        Assert.True(IndexOfHeader(menu, "7-Zip") < IndexOf(menu, MenuCommandId.ActionsSubmenu));
        Assert.True(IndexOf(menu, MenuCommandId.ActionsSubmenu) < IndexOf(menu, MenuCommandId.FileSubmenu));
    }

    [Fact]
    public void ContextMenu_SplitsPresetsIntoConvert_AndLooseRowsOutOfTheSubmenu() {
        var loose = _forVideo with { Id = "loose", InSubmenu = false };
        var preset = _forVideo with { Id = "preset", Category = ActionCategory.Convert };
        var target = SelectionOf(File("a.mp4")) with { Actions = new[] { _forVideo, loose, preset } };

        var menu = ContextMenuBuilder.Build(target, ContextMenuSettings.Default);

        Assert.Contains(menu, e => e.Id == MenuCommandId.RunAction && e.Argument == "loose");
        Assert.Equal(new[] { _forVideo.Id }, Find(menu, MenuCommandId.ActionsSubmenu)!.Children.Select(e => e.Argument));
        Assert.Equal(new[] { "preset" }, Find(menu, MenuCommandId.ConvertSubmenu)!.Children.Select(e => e.Argument));
    }

    [Fact]
    public void ContextMenu_HidesWhatNeedsAMissingTool_AndWhatIsSwitchedOff() {
        var needsFfmpeg = _forVideo with { Id = "ff", RequiredTool = "ffmpeg" };
        var off = _forVideo with { Id = "off", Enabled = false };
        var headerOnly = _forVideo with { Id = "header", Placement = ActionPlacement.Header };
        var target = SelectionOf(File("a.mp4")) with {
            Actions = new[] { needsFfmpeg, off, headerOnly },
            MissingTools = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ffmpeg" },
        };

        var menu = ContextMenuBuilder.Build(target, ContextMenuSettings.Default);

        Assert.Null(Find(menu, MenuCommandId.ActionsSubmenu));
    }

    [Fact]
    public void DebugOnlyActions_AreOfferedOnlyWhileTheDebugMenuIsOn() {
        var hold = _forAll with { Id = "hold", DebugOnly = true };
        var target = SelectionOf(File("a.mp4")) with { Actions = new[] { _forVideo, hold } };

        var off = ContextMenuBuilder.Build(target, ContextMenuSettings.Default);
        var on = ContextMenuBuilder.Build(target with { ShowDebug = true }, ContextMenuSettings.Default);

        Assert.Equal(new[] { _forVideo.Id }, Find(off, MenuCommandId.ActionsSubmenu)!.Children.Select(e => e.Argument));
        Assert.Equal(new[] { _forVideo.Id, "hold" }, Find(on, MenuCommandId.ActionsSubmenu)!.Children.Select(e => e.Argument));
    }

    [Fact]
    public void HeaderMenu_KeepsDebugOnlyActionsToTheDebugMenuToo() {
        var hold = _forAll with { Id = "hold", DebugOnly = true };
        var target = new ContextMenuTarget {
            Place = MenuPlace.Header, Selection = new[] { File("a.mp4") }, FolderPath = Folder,
            Actions = new[] { hold },
        };

        var off = ContextMenuBuilder.Build(target, ContextMenuSettings.Default);
        var on = ContextMenuBuilder.Build(target with { ShowDebug = true }, ContextMenuSettings.Default);

        Assert.DoesNotContain(Find(off, MenuCommandId.ActionsSubmenu)!.Children, e => e.Argument == "hold");
        Assert.Contains(Find(on, MenuCommandId.ActionsSubmenu)!.Children, e => e.Argument == "hold");
    }

    [Fact]
    public void Background_OffersFolderActions_ForTheFolderOnScreen() {
        var target = Background() with { Actions = new[] { _forFolders, _forVideo } };

        var menu = ContextMenuBuilder.Build(target, ContextMenuSettings.Default);

        Assert.Equal(new[] { _forFolders.Id }, Find(menu, MenuCommandId.ActionsSubmenu)!.Children.Select(e => e.Argument));
    }

    [Fact]
    public void InsideArchiveOrBin_NoActionsAtAll() {
        var archive = SelectionOf(File("a.mp4")) with { IsReadOnlyLocation = true, IsArchive = true, Actions = new[] { _forVideo, _forAll } };
        var bin = SelectionOf(File("a.mp4")) with { IsReadOnlyLocation = true, IsRecycleBin = true, Actions = new[] { _forVideo, _forAll } };

        Assert.Null(Find(ContextMenuBuilder.Build(archive, ContextMenuSettings.Default), MenuCommandId.ActionsSubmenu));
        Assert.Null(Find(ContextMenuBuilder.Build(bin, ContextMenuSettings.Default), MenuCommandId.ActionsSubmenu));
    }

    [Fact]
    public void HidingTheActionsSubmenu_HidesItInBothPlaces() {
        var target = SelectionOf(File("a.mp4")) with { Actions = new[] { _forVideo } };
        var settings = Hiding(MenuCommandId.ActionsSubmenu);

        Assert.Null(Find(ContextMenuBuilder.Build(target, settings), MenuCommandId.ActionsSubmenu));
        Assert.Null(Find(ContextMenuBuilder.Build(target with { Place = MenuPlace.Header }, settings), MenuCommandId.ActionsSubmenu));
    }


    // --- The header's "Actions" ----------------------------------------------

    [Fact]
    public void Header_KeepsItsOrder_AndOffersOnlyWhatApplies() {
        var none = Header();
        var one = Header(File("a.txt"));
        var many = Header(File("a.txt"), File("b.txt"));
        var archive = ContextMenuBuilder.Build(
            SelectionOf(File("a.zip"), File("b.zip")) with { Place = MenuPlace.Header, SelectionIsArchive = true },
            ContextMenuSettings.Default);

        foreach (var menu in new[] { none, one, many, archive }) {
            // Caption first: a disabled, command-less row saying what the
            // rows below are about.
            Assert.Equal(MenuCommandId.None, menu[0].Id);
            Assert.False(menu[0].IsEnabled);
            Assert.NotEmpty(menu[0].Header);
            AssertSeparatorsAreSane(menu);
            // Whatever is there works: nothing is greyed without an action.
            Assert.All(menu.Where(e => !e.IsSeparator && e.Id != MenuCommandId.None), e => Assert.True(e.IsEnabled));
        }

        Assert.Equal(new[] { MenuCommandId.ActionsSubmenu, MenuCommandId.CopyPath, MenuCommandId.OpenInTerminal }, Ids(none));
        Assert.Equal(new[] { MenuCommandId.ActionsSubmenu, MenuCommandId.CreateShortcut, MenuCommandId.CopyPath }, Ids(one));
        Assert.Equal(new[] {
            MenuCommandId.BatchRename, MenuCommandId.ActionsSubmenu, MenuCommandId.CreateShortcut, MenuCommandId.CopyPath,
        }, Ids(many));
        Assert.Equal(new[] {
            MenuCommandId.BatchRename, MenuCommandId.ActionsSubmenu,
            MenuCommandId.Extract, MenuCommandId.CreateShortcut, MenuCommandId.CopyPath,
        }, Ids(archive));

        static IEnumerable<MenuCommandId> Ids(IReadOnlyList<MenuEntry> menu) {
            return menu.Where(e => !e.IsSeparator && e.Id != MenuCommandId.None).Select(e => e.Id);
        }
    }

    [Fact]
    public void Header_BatchRename_OnlyForTwoOrMoreOfOneKind() {
        Assert.Null(Find(Header(File("a.txt")), MenuCommandId.BatchRename));
        Assert.Null(Find(Header(File("a.txt"), Dir("b")), MenuCommandId.BatchRename));
        Assert.True(Enabled(Header(File("a.txt"), File("b.txt")), MenuCommandId.BatchRename));
        Assert.True(Enabled(Header(Dir("a"), Dir("b")), MenuCommandId.BatchRename));
    }

    [Fact]
    public void Header_FolderVerbsWorkOnTheFolder_WhenNothingIsSelected() {
        var none = Header();
        var file = Header(File("a.txt"));
        var folder = Header(Dir("d"));

        Assert.True(Enabled(none, MenuCommandId.OpenInTerminal));
        Assert.Null(Find(file, MenuCommandId.OpenInTerminal));
        Assert.True(Enabled(folder, MenuCommandId.OpenInTerminal));

        Assert.Null(Find(none, MenuCommandId.CreateShortcut));
        Assert.True(Enabled(file, MenuCommandId.CreateShortcut));
    }

    [Fact]
    public void Header_ExtractApplies_ToArchivesAndInsideThem() {
        var archives = ContextMenuBuilder.Build(
            SelectionOf(File("a.zip")) with { Place = MenuPlace.Header, SelectionIsArchive = true },
            ContextMenuSettings.Default);
        var inside = ContextMenuBuilder.Build(
            SelectionOf(File("readme.txt")) with { Place = MenuPlace.Header, IsReadOnlyLocation = true, IsArchive = true },
            ContextMenuSettings.Default);

        Assert.True(Enabled(archives, MenuCommandId.Extract));
        Assert.True(Enabled(inside, MenuCommandId.Extract));
        // Nothing else writes inside an archive.
        Assert.Null(Find(inside, MenuCommandId.BatchRename));
        Assert.Null(Find(inside, MenuCommandId.CreateShortcut));
        Assert.Null(Find(inside, MenuCommandId.OpenInTerminal));
    }

    [Fact]
    public void Header_ActionsSubmenu_GreysOnlyAMissingTool_AndLeadsToSettings() {
        var videoNeedsFfmpeg = _forVideo with { Id = "ff-video", RequiredTool = "ffmpeg" };
        var imagesNeedFfmpeg = _forImages with { Id = "ff-images", RequiredTool = "ffmpeg" };
        var target = SelectionOf(File("a.jpg")) with {
            Place = MenuPlace.Header,
            Actions = new[] { _forVideo, _forImages, videoNeedsFfmpeg, imagesNeedFfmpeg },
            MissingTools = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ffmpeg" },
        };

        var actions = Find(ContextMenuBuilder.Build(target, ContextMenuSettings.Default), MenuCommandId.ActionsSubmenu)!.Children;

        // The video actions do not apply to a picture, tool or no tool.
        Assert.Equal(new[] { _forImages.Id, "ff-images" }, actions.Where(e => e.Id == MenuCommandId.RunAction).Select(e => e.Argument));
        Assert.True(actions[0].IsEnabled);
        Assert.Null(actions[0].Tooltip);
        Assert.False(actions[1].IsEnabled);
        // The tool's name is in the text; with no text source the key comes
        // back unformatted, and that is what is pinned here.
        Assert.Equal(ActionApplicability.ToolMissingKey, actions[1].Tooltip);
        Assert.Equal(MenuCommandId.ConfigureActions, actions[^1].Id);
        Assert.True(actions[^2].IsSeparator);
    }

    [Fact]
    public void Header_NoActions_WhereFilesCannotBeWritten() {
        var bin = SelectionOf(File("a.jpg")) with {
            Place = MenuPlace.Header,
            IsReadOnlyLocation = true,
            IsRecycleBin = true,
            Actions = new[] { _forImages, _forAll },
        };

        var actions = Find(ContextMenuBuilder.Build(bin, ContextMenuSettings.Default), MenuCommandId.ActionsSubmenu)!.Children;

        Assert.Equal(MenuCommandId.NoActions, actions[0].Id);
        Assert.DoesNotContain(actions, e => e.Id == MenuCommandId.RunAction);
    }

    [Fact]
    public void Header_EmptyCatalog_SaysSo_AndStillLeadsToSettings() {
        var actions = Find(Header(File("a.jpg")), MenuCommandId.ActionsSubmenu)!.Children;

        Assert.Equal(MenuCommandId.NoActions, actions[0].Id);
        Assert.False(actions[0].IsEnabled);
        Assert.Equal(MenuCommandId.ConfigureActions, actions[^1].Id);
    }

    [Fact]
    public void Header_ConvertSubmenu_ExistsOnlyWithPresets() {
        var preset = _forVideo with { Id = "preset", Category = ActionCategory.Convert };
        var without = ContextMenuBuilder.Build(
            SelectionOf(File("a.mp4")) with { Place = MenuPlace.Header, Actions = new[] { _forVideo } }, ContextMenuSettings.Default);
        var with = ContextMenuBuilder.Build(
            SelectionOf(File("a.mp4")) with { Place = MenuPlace.Header, Actions = new[] { _forVideo, preset } }, ContextMenuSettings.Default);

        Assert.Null(Find(without, MenuCommandId.ConvertSubmenu));
        var convert = Find(with, MenuCommandId.ConvertSubmenu)!.Children;
        Assert.Equal("preset", convert[0].Argument);
        Assert.Equal(MenuCommandId.ConfigureActions, convert[^1].Id);
    }

    [Fact]
    public void ToFolder_OffersTheApplicableActionsWithAnOutput_InBothMenus() {
        var preset = _forVideo with { Id = "preset", Category = ActionCategory.Convert, Output = "{name}.mp4" };
        var noOutput = _forVideo with { Id = "open", Category = ActionCategory.Convert };
        var forImages = _forImages with { Id = "img", Category = ActionCategory.Convert, Output = "{name}.jpg" };
        var target = SelectionOf(File("a.mp4")) with { Actions = new[] { preset, noOutput, forImages } };

        var context = Find(ContextMenuBuilder.Build(target, ContextMenuSettings.Default), MenuCommandId.ConvertSubmenu)!.Children;
        var header = Find(ContextMenuBuilder.Build(target with { Place = MenuPlace.Header }, ContextMenuSettings.Default), MenuCommandId.ConvertSubmenu)!.Children;

        foreach (var convert in new[] { context, header }) {
            var toFolder = Find(convert, MenuCommandId.ToFolderSubmenu)!;
            var row = Assert.Single(toFolder.Children);
            Assert.Equal(MenuCommandId.RunActionTo, row.Id);
            Assert.Equal("preset", row.Argument);
            Assert.Equal(preset.Title, row.Header);
        }
        // In the header it sits above the way to settings.
        Assert.Equal(MenuCommandId.ConfigureActions, header[^1].Id);
        Assert.True(IndexOf(header, MenuCommandId.ToFolderSubmenu) < IndexOf(header, MenuCommandId.ConfigureActions));
    }

    [Fact]
    public void ToFolder_IsAbsent_WhenNothingApplicableDeclaresAnOutput() {
        var target = SelectionOf(File("a.mp4")) with { Actions = new[] { _forVideo with { Category = ActionCategory.Convert } } };

        var convert = Find(ContextMenuBuilder.Build(target, ContextMenuSettings.Default), MenuCommandId.ConvertSubmenu)!.Children;

        Assert.Null(Find(convert, MenuCommandId.ToFolderSubmenu));
        Assert.DoesNotContain(convert, e => e.IsSeparator);
    }

    [Fact]
    public void Header_ContextOnlyActions_StayOutOfIt() {
        var contextOnly = _forVideo with { Id = "ctx", Placement = ActionPlacement.ContextMenu };
        var target = SelectionOf(File("a.mp4")) with { Place = MenuPlace.Header, Actions = new[] { contextOnly } };

        var actions = Find(ContextMenuBuilder.Build(target, ContextMenuSettings.Default), MenuCommandId.ActionsSubmenu)!.Children;

        Assert.Equal(MenuCommandId.NoActions, actions[0].Id);
    }

    [Fact]
    public void Header_Caption_NamesTheCountAndTheType() {
        // No text source in tests: keys come back as themselves, so the
        // caption is the key with the arguments unformatted in it - which
        // is enough to see which shape was chosen.
        Assert.Equal(ContextMenuBuilder.CaptionFolderKey, Header()[0].Header);
        Assert.Equal("MenuCaptionImages", Header(File("a.jpg"), File("b.png"))[0].Header);
        Assert.Equal("MenuCaptionFolders", Header(Dir("a"))[0].Header);
        Assert.Equal(ContextMenuBuilder.CaptionSelectionKey, Header(File("a.jpg"), File("b.mp4"))[0].Header);
    }


    // --- A row of a folder panel (decision B8) ------------------------------

    /// <summary>
    /// A shortcut to a folder right-clicked in a panel would land in the
    /// folder open in the list, which the row says nothing about - so the
    /// row's menu does not offer one, in either shape.
    /// </summary>
    [Fact]
    public void PanelRow_OffersNoShortcut() {
        var row = new ContextMenuTarget { Selection = new[] { Dir("sub") }, FolderPath = Folder, IsPanelRow = true, CanPaste = true };

        var menu = ContextMenuBuilder.Build(row, ContextMenuSettings.Default);
        var header = ContextMenuBuilder.Build(row with { Place = MenuPlace.Header }, ContextMenuSettings.Default);

        Assert.DoesNotContain(Flatten(menu), e => e.Id == MenuCommandId.CreateShortcut);
        Assert.DoesNotContain(Flatten(header), e => e.Id == MenuCommandId.CreateShortcut);
        // The rest of the folder's verbs stay.
        var file = Find(menu, MenuCommandId.FileSubmenu)!.Children;
        Assert.True(Enabled(file, MenuCommandId.Paste));
        Assert.True(Enabled(file, MenuCommandId.Rename));
        Assert.True(Enabled(file, MenuCommandId.Delete));
        AssertSeparatorsAreSane(file);
    }

    [Fact]
    public void ListRow_StillOffersAShortcut() {
        var menu = ContextMenuBuilder.Build(SelectionOf(Dir("sub")), ContextMenuSettings.Default);

        Assert.True(Enabled(Find(menu, MenuCommandId.FileSubmenu)!.Children, MenuCommandId.CreateShortcut));
    }


    // --- Helpers ----------------------------------------------------------

    private static readonly CustomAction _forVideo = new() {
        Id = "video", Title = "Encode", Program = "ffmpeg", Types = new FileTypeSelector(FileTypeGroup.Video),
    };

    private static readonly CustomAction _forImages = new() {
        Id = "images", Title = "Shrink", Program = "magick", Types = new FileTypeSelector(FileTypeGroup.Images),
    };

    private static readonly CustomAction _forAll = new() {
        Id = "all", Title = "Anything", Program = "tool", Types = new FileTypeSelector(FileTypeGroup.All),
    };

    private static readonly CustomAction _forFolders = new() {
        Id = "folders", Title = "Index", Program = "tool", Types = new FileTypeSelector(FileTypeGroup.Folders),
    };

    private static IReadOnlyList<MenuEntry> Header(params FileSystemEntry[] selection) {
        return ContextMenuBuilder.Build(
            new ContextMenuTarget { Place = MenuPlace.Header, Selection = selection, FolderPath = Folder },
            ContextMenuSettings.Default);
    }

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
