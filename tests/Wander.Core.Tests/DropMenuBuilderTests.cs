using Wander.Core.Actions;
using Wander.Core.FileSystem;
using Wander.Core.Menu;

namespace Wander.Core.Tests;

public class DropMenuBuilderTests {
    private const string Target = @"D:\out\Photos";

    private static readonly CustomAction _toJpeg = new() {
        Id = "jpeg", Title = "JPEG", Types = new FileTypeSelector(FileTypeGroup.Images),
        Output = "{name}.jpg", Category = ActionCategory.Convert,
    };

    private static readonly CustomAction _ownWithOutput = new() {
        Id = "own", Title = "Own", Types = new FileTypeSelector(FileTypeGroup.All), Output = "{name}.out",
    };

    private static readonly CustomAction _noOutput = new() {
        Id = "open", Title = "Open in editor", Types = new FileTypeSelector(FileTypeGroup.All),
    };


    [Fact]
    public void LeadsWithADisabledCaption_AndEndsWithCancel() {
        var menu = DropMenuBuilder.Build(Drop(File("a.jpg")));

        Assert.Equal(MenuCommandId.DropCaption, menu[0].Id);
        Assert.False(menu[0].IsEnabled);
        Assert.NotEmpty(menu[0].Header);
        Assert.Equal(MenuCommandId.DropCancel, menu[^1].Id);
    }

    [Fact]
    public void OffersCopyMoveAndShortcut_WithTheLeftButtonsChoiceInBold() {
        var move = DropMenuBuilder.Build(Drop(File("a.jpg")) with { MoveByDefault = true });
        var copy = DropMenuBuilder.Build(Drop(File("a.jpg")) with { MoveByDefault = false });

        Assert.True(Find(move, MenuCommandId.DropMoveHere)!.IsDefault);
        Assert.False(Find(move, MenuCommandId.DropCopyHere)!.IsDefault);
        Assert.True(Find(copy, MenuCommandId.DropCopyHere)!.IsDefault);
        Assert.False(Find(copy, MenuCommandId.DropMoveHere)!.IsDefault);
        Assert.NotNull(Find(copy, MenuCommandId.DropLinkHere));
    }

    [Fact]
    public void OutOfAnArchive_OnlyCopyingIsOffered() {
        var menu = DropMenuBuilder.Build(Drop(File("a.jpg")) with { FromArchive = true, MoveByDefault = true });

        Assert.True(Find(menu, MenuCommandId.DropCopyHere)!.IsDefault);
        Assert.Null(Find(menu, MenuCommandId.DropMoveHere));
        Assert.Null(Find(menu, MenuCommandId.DropLinkHere));
        Assert.Null(Find(menu, MenuCommandId.ConvertSubmenu));
    }

    [Fact]
    public void ActionsWithAnOutput_GoIntoTheirSubmenus_AsRunToFolder() {
        var menu = DropMenuBuilder.Build(Drop(File("a.jpg"), File("b.png")));

        var convert = Find(menu, MenuCommandId.ConvertSubmenu);
        Assert.NotNull(convert);
        var row = Assert.Single(convert!.Children);
        Assert.Equal(MenuCommandId.RunActionTo, row.Id);
        Assert.Equal("jpeg", row.Argument);

        var own = Find(menu, MenuCommandId.ActionsSubmenu);
        Assert.NotNull(own);
        Assert.Equal("own", Assert.Single(own!.Children).Argument);
        // Nothing to send into the folder: not offered at all.
        Assert.DoesNotContain(Flatten(menu), e => e.Argument == "open");
    }

    [Fact]
    public void AnActionThatDoesNotApplyToEveryItem_IsLeftOut() {
        var menu = DropMenuBuilder.Build(Drop(File("a.jpg"), File("b.mp4")));

        Assert.Null(Find(menu, MenuCommandId.ConvertSubmenu));
        // "Own" takes anything, so it is still there.
        Assert.NotNull(Find(menu, MenuCommandId.ActionsSubmenu));
    }

    [Fact]
    public void AMissingTool_LeavesTheActionOut() {
        var needsTool = _toJpeg with { RequiredTool = "magick" };
        var target = Drop(File("a.jpg")) with {
            Actions = new[] { needsTool },
            MissingTools = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "magick" },
        };

        Assert.Null(Find(DropMenuBuilder.Build(target), MenuCommandId.ConvertSubmenu));
    }

    [Fact]
    public void ADropThatCouldNotBeReadInFull_OffersNoActions() {
        var target = Drop(File("a.jpg")) with { Paths = new[] { @"C:\in\a.jpg", @"C:\in\gone.jpg" } };

        var menu = DropMenuBuilder.Build(target);

        Assert.Null(Find(menu, MenuCommandId.ConvertSubmenu));
        Assert.Null(Find(menu, MenuCommandId.ActionsSubmenu));
        Assert.NotNull(Find(menu, MenuCommandId.DropCopyHere));
    }

    [Fact]
    public void ASubmenuHiddenInSettings_StaysHidden() {
        var settings = ContextMenuSettings.Default with {
            HiddenItems = new HashSet<MenuCommandId> { MenuCommandId.ConvertSubmenu },
        };

        var menu = DropMenuBuilder.Build(Drop(File("a.jpg")) with { Settings = settings });

        Assert.Null(Find(menu, MenuCommandId.ConvertSubmenu));
        Assert.NotNull(Find(menu, MenuCommandId.ActionsSubmenu));
    }


    private static DropMenuTarget Drop(params FileSystemEntry[] entries) {
        return new DropMenuTarget {
            Paths = entries.Select(e => e.FullPath).ToArray(),
            Entries = entries,
            TargetFolder = Target,
            Actions = new[] { _toJpeg, _ownWithOutput, _noOutput },
        };
    }

    private static FileSystemEntry File(string name) {
        return new FileSystemEntry(
            name, System.IO.Path.Combine(@"C:\in", name), EntryKind.File, 10, DateTime.UnixEpoch,
            false, false, false, false);
    }

    private static MenuEntry? Find(IReadOnlyList<MenuEntry> menu, MenuCommandId id) {
        return menu.FirstOrDefault(e => e.Id == id);
    }

    private static IEnumerable<MenuEntry> Flatten(IReadOnlyList<MenuEntry> entries) {
        foreach (var entry in entries) {
            yield return entry;
            foreach (var child in Flatten(entry.Children)) {
                yield return child;
            }
        }
    }
}
