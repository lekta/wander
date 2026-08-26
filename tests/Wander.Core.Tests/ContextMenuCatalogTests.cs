using Wander.Core.FileSystem;
using Wander.Core.Menu;

namespace Wander.Core.Tests;

/// <summary>
/// Drift guards. The catalog is a hand-written dictionary keyed by an enum,
/// so the failure mode is silent: add a menu entry, forget its resource key,
/// and the menu shows "SortByType" to the user. These tests make that loud.
///
/// <para>
/// No text source is registered here, so <c>Title</c> returns the resource
/// key itself. That is exactly what these tests need — they check that every
/// entry has a key and that keys do not collide; whether the key resolves to
/// Russian is the app layer's business.
/// </para>
/// </summary>
public class ContextMenuCatalogTests {

    [Fact]
    public void EveryHideableEntryHasAKey() {
        foreach (var id in ContextMenuCatalog.Hideable) {
            Assert.NotEqual(id.ToString(), ContextMenuCatalog.Title(id));
        }
    }

    [Fact]
    public void EveryEntryTheBuilderEmitsHasAKey() {
        foreach (var entry in AllBuiltInEntries()) {
            Assert.NotEqual(entry.Id.ToString(), entry.Header);
            Assert.NotEmpty(entry.Header);
        }
    }

    [Fact]
    public void EveryEntryTheBuilderEmitsIsHideable() {
        // Otherwise the settings dialog silently can't reach it. Submenu
        // children are exempt: hiding "Имя" from the sort list is noise, the
        // whole submenu is the useful switch.
        var hideable = ContextMenuCatalog.Hideable.ToHashSet();

        foreach (var entry in TopLevelBuiltInEntries()) {
            Assert.Contains(entry.Id, hideable);
        }
    }

    [Fact]
    public void NoTwoTopLevelEntriesShareALabel() {
        // Two identical rows in one menu is always a bug. Distinct keys are
        // the half that can be checked here; that two keys do not translate
        // to the same words is the string table's business. The shell half —
        // our "open with" against the system's own — is pinned by
        // ContextMenuBuilderTests.ShellOpenWithPopup_IsPouredIntoOurOpenSubmenu.
        foreach (var menu in BothMenus()) {
            var headers = menu.Where(e => !e.IsSeparator).Select(e => e.Header).ToList();

            Assert.Equal(headers.Count, headers.Distinct().Count());
        }
    }

    [Fact]
    public void GesturesOnlyAdvertiseKeysTheAppActuallyBinds() {
        // A menu that promises a hotkey nobody wired up is worse than a menu
        // with no hint at all. Keep this list in step with
        // MainWindow.xaml's InputBindings.
        var bound = new HashSet<string> {
            "Enter", "Ctrl+X", "Ctrl+C", "Ctrl+V", "Ctrl+Shift+C",
            "F2", "Del", "Ctrl+Shift+N", "F5", "Ctrl+Z", "Alt+Enter",
        };

        foreach (var entry in AllBuiltInEntries()) {
            if (entry.Gesture is { } gesture) {
                Assert.Contains(gesture, bound);
            }
        }
    }


    private static IEnumerable<IReadOnlyList<MenuEntry>> BothMenus() {
        var file = new FileSystemEntry(
            "a.txt", @"C:\work\a.txt", EntryKind.File, 1, DateTime.UnixEpoch, false, false, false, false);

        yield return ContextMenuBuilder.Build(
            new ContextMenuTarget { Selection = new[] { file }, FolderPath = @"C:\work" },
            ContextMenuSettings.Default);

        yield return ContextMenuBuilder.Build(
            new ContextMenuTarget { FolderPath = @"C:\work", IsBackground = true },
            ContextMenuSettings.Default);
    }

    private static IEnumerable<MenuEntry> TopLevelBuiltInEntries() {
        return BothMenus()
            .SelectMany(menu => menu)
            .Where(entry => !entry.IsSeparator && entry.Id != MenuCommandId.None);
    }

    private static IEnumerable<MenuEntry> AllBuiltInEntries() {
        return BothMenus()
            .SelectMany(Flatten)
            .Where(entry => !entry.IsSeparator && entry.Id != MenuCommandId.None);
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
