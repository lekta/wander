using Wander.App.Resources;

namespace Wander.App.ViewModels;

/// <summary>
/// Every keyboard shortcut Wander answers to, as one list the settings
/// dialog can draw.
///
/// <para>
/// A reference, not a binding table: the gestures themselves still live
/// where they are handled — <c>MainWindow.xaml</c>'s input bindings and the
/// two key handlers in the code-behind — and this does not drive them.
/// Which is the honest shape for what it is. Making them editable means
/// routing all three places through a registry of commands and gestures,
/// and that is a piece of work with its own entry in PLAN.md rather than a
/// side effect of writing them down.
/// </para>
///
/// <para>
/// The gesture column is deliberately literal rather than a resource, the
/// same way <c>InputGestureText</c> is spelled out in the menus: these are
/// the names printed on the keys, and they do not change with the
/// interface language. The descriptions beside them do, so those come
/// from <see cref="Strings"/>.
/// </para>
///
/// <para>
/// Mouse gestures are not here. They are in the guide, and a list called
/// "keyboard" that also explains what Alt-dragging does is a list nobody
/// can scan.
/// </para>
/// </summary>
public static class HotkeyCatalog {
    public static IReadOnlyList<HotkeyGroup> Groups { get; } = new[] {
        new HotkeyGroup(Strings.HotkeyGroupNavigation, new[] {
            new HotkeyRow("Alt + ←", Strings.HotkeyBack),
            new HotkeyRow("Alt + →", Strings.HotkeyForward),
            new HotkeyRow("Alt + ↑", Strings.HotkeyUp),
            new HotkeyRow("Backspace", Strings.HotkeyUpBackspace),
            new HotkeyRow("Enter", Strings.HotkeyOpen),
            new HotkeyRow("Ctrl + L, Alt + D", Strings.HotkeyAddressBar),
            new HotkeyRow("F4", Strings.HotkeyRecent),
            new HotkeyRow("Enter в адресной строке", Strings.HotkeyAddressGo),
            new HotkeyRow("Esc в адресной строке", Strings.HotkeyAddressCancel),
            new HotkeyRow("F5", Strings.HotkeyRefresh),
        }),
        new HotkeyGroup(Strings.HotkeyGroupPanes, new[] {
            new HotkeyRow("Tab / Shift + Tab", Strings.HotkeyNextPane),
            new HotkeyRow("Ctrl + 1", Strings.HotkeyToList),
            new HotkeyRow("Ctrl + 2", Strings.HotkeyToTree),
            new HotkeyRow("Ctrl + Shift + E", Strings.HotkeyRevealInTree),
            new HotkeyRow("Ctrl + Q", Strings.HotkeyTogglePreview),
            new HotkeyRow("← / → в дереве", Strings.HotkeyTreeExpand),
            new HotkeyRow("Enter в дереве", Strings.HotkeyTreeEnter),
            new HotkeyRow("Esc в дереве", Strings.HotkeyTreeEscape),
        }),
        new HotkeyGroup(Strings.HotkeyGroupFileOps, new[] {
            new HotkeyRow("Ctrl + C", Strings.HotkeyCopy),
            new HotkeyRow("Ctrl + Shift + C", Strings.HotkeyCopyPath),
            new HotkeyRow("Ctrl + X", Strings.HotkeyCut),
            new HotkeyRow("Ctrl + V", Strings.HotkeyPaste),
            new HotkeyRow("Delete", Strings.HotkeyDelete),
            new HotkeyRow("Shift + Delete", Strings.HotkeyDeleteForever),
            new HotkeyRow("F2", Strings.HotkeyRename),
            new HotkeyRow("Ctrl + Shift + N", Strings.HotkeyNewFolder),
            new HotkeyRow("Ctrl + Z", Strings.HotkeyUndo),
        }),
        new HotkeyGroup(Strings.HotkeyGroupSearch, new[] {
            new HotkeyRow("Ctrl + A", Strings.HotkeySelectAll),
            new HotkeyRow("Ctrl + F", Strings.HotkeyFilter),
            new HotkeyRow("Ctrl + Shift + F", Strings.HotkeySearchWindow),
            new HotkeyRow("Enter в окне поиска", Strings.HotkeySearchNow),
            new HotkeyRow("Esc в окне поиска", Strings.HotkeySearchClose),
            new HotkeyRow("Esc в поле фильтра", Strings.HotkeyFilterEscape),
            new HotkeyRow("F5 на результатах поиска", Strings.HotkeySearchRepeat),
            new HotkeyRow("Esc в списке", Strings.HotkeyClearSelection),
            new HotkeyRow("Буквы в списке", Strings.HotkeyTypeAhead),
            new HotkeyRow("Стрелки в плитках и значках", Strings.HotkeyGridArrows),
            new HotkeyRow("Alt + Enter", Strings.HotkeyProperties),
        }),
        new HotkeyGroup(Strings.HotkeyGroupView, new[] {
            new HotkeyRow("Ctrl + Shift + 1", Strings.HotkeyViewGallery),
            new HotkeyRow("0…5 в галерее", Strings.HotkeyRateInGallery),
            new HotkeyRow("Ctrl + Shift + 2", Strings.HotkeyViewLargeIcons),
            new HotkeyRow("Ctrl + Shift + 6", Strings.HotkeyViewDetails),
            new HotkeyRow("Ctrl + Shift + 7", Strings.HotkeyViewTiles),
        }),
    };
}


/// <summary>One section of the shortcut list — the headings the guide uses.</summary>
public sealed record HotkeyGroup(string Title, IReadOnlyList<HotkeyRow> Rows);


/// <summary>One shortcut: what to press, and what it does.</summary>
public sealed record HotkeyRow(string Gesture, string Description);
