namespace Wander.Core.Menu;

/// <summary>
/// Single source of truth for what every built-in entry is called and which
/// hotkey it advertises. Both the builder and the settings dialog read from
/// here, so a label never drifts between the menu and the checkbox that
/// hides it.
///
/// <para>
/// Labels are hard-coded Russian, matching the settings dialog and the
/// entries the shell itself contributes on a Russian Windows. Proper
/// localisation is a separate, app-wide job (see PLAN.md) — when it lands,
/// this dictionary is the one place the menu needs to change.
/// </para>
/// </summary>
public static class ContextMenuCatalog {
    private static readonly Dictionary<MenuCommandId, string> _titles = new() {
        [MenuCommandId.OpenSubmenu] = "Открыть с помощью",
        [MenuCommandId.FileSubmenu] = "Файл",
        [MenuCommandId.ViewSubmenu] = "Вид",
        [MenuCommandId.SortSubmenu] = "Сортировка",

        [MenuCommandId.Open] = "Открыть",
        [MenuCommandId.OpenWith] = "Выбрать приложение...",
        [MenuCommandId.OpenInTerminal] = "Открыть в терминале",

        [MenuCommandId.Cut] = "Вырезать",
        [MenuCommandId.Copy] = "Копировать",
        [MenuCommandId.Paste] = "Вставить",
        [MenuCommandId.CopyPath] = "Копировать путь",
        [MenuCommandId.CopyName] = "Копировать имя",
        [MenuCommandId.CreateShortcut] = "Создать ярлык",

        [MenuCommandId.Rename] = "Переименовать",
        [MenuCommandId.Delete] = "Удалить",
        [MenuCommandId.NewFolder] = "Создать папку",

        [MenuCommandId.ViewDetails] = "Таблица",
        [MenuCommandId.ViewTiles] = "Плитка",
        [MenuCommandId.ViewLargeIcons] = "Крупные значки",
        [MenuCommandId.TogglePreview] = "Область просмотра",
        [MenuCommandId.SortByName] = "Имя",
        [MenuCommandId.SortByDate] = "Дата изменения",
        [MenuCommandId.SortBySize] = "Размер",
        [MenuCommandId.SortByType] = "Тип",
        [MenuCommandId.SortAscending] = "По возрастанию",
        [MenuCommandId.SortFoldersFirst] = "Папки сверху",

        [MenuCommandId.Refresh] = "Обновить",
        [MenuCommandId.Undo] = "Отменить",
        [MenuCommandId.Properties] = "Свойства",
    };

    private static readonly Dictionary<MenuCommandId, string> _gestures = new() {
        [MenuCommandId.Open] = "Enter",
        [MenuCommandId.Cut] = "Ctrl+X",
        [MenuCommandId.Copy] = "Ctrl+C",
        [MenuCommandId.Paste] = "Ctrl+V",
        [MenuCommandId.CopyPath] = "Ctrl+Shift+C",
        [MenuCommandId.Rename] = "F2",
        [MenuCommandId.Delete] = "Del",
        [MenuCommandId.NewFolder] = "Ctrl+Shift+N",
        [MenuCommandId.Refresh] = "F5",
        [MenuCommandId.Undo] = "Ctrl+Z",
        [MenuCommandId.Properties] = "Alt+Enter",
    };

    /// <summary>
    /// Entries the settings dialog offers to hide, in the order they should
    /// be listed there. Submenu headers are included on purpose — hiding
    /// "Файл" removes the whole clipboard block in one click, which is
    /// exactly what a hotkey-only user wants.
    ///
    /// <para>
    /// Excluded: the View / Sort leaves — hiding individual sort keys is
    /// noise, hide the submenu instead.
    /// </para>
    /// </summary>
    public static IReadOnlyList<MenuCommandId> Hideable { get; } = new[] {
        MenuCommandId.Open,
        MenuCommandId.OpenSubmenu,
        MenuCommandId.OpenWith,
        MenuCommandId.OpenInTerminal,
        MenuCommandId.FileSubmenu,
        MenuCommandId.Cut,
        MenuCommandId.Copy,
        MenuCommandId.Paste,
        MenuCommandId.CopyPath,
        MenuCommandId.CopyName,
        MenuCommandId.CreateShortcut,
        MenuCommandId.Rename,
        MenuCommandId.Delete,
        MenuCommandId.NewFolder,
        MenuCommandId.ViewSubmenu,
        MenuCommandId.SortSubmenu,
        MenuCommandId.Refresh,
        MenuCommandId.Undo,
        MenuCommandId.Properties,
    };


    public static string Title(MenuCommandId id) {
        return _titles.TryGetValue(id, out string? title) ? title : id.ToString();
    }

    public static string? Gesture(MenuCommandId id) {
        return _gestures.TryGetValue(id, out string? gesture) ? gesture : null;
    }
}
