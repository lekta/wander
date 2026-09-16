namespace Wander.App.Resources;

/// <summary>
/// Свои действия: страницы «Действия» и «Программы» в Параметрах, запуск,
/// отчёт, встроенный кодировщик картинок.
///
/// <para>
/// Часть <see cref="Strings"/>, разложенная по файлам: ключей больше
/// четырёхсот, и одним файлом на полторы тысячи строк пользоваться нельзя.
/// Ресурсы при этом лежат в одном <c>Strings.resx</c> — делить ещё и его
/// значило бы искать ключ по нескольким <c>ResourceManager</c> подряд, а
/// выигрыш тот же самый. Разбор — в BACKLOG.md. Названия пресетов
/// (<c>ActionPreset*</c>) берёт Core через <c>Text.Get</c>, здесь их нет.
/// </para>
/// </summary>
public static partial class Strings {
    /// <summary>Действия</summary>
    public static string SettingsCategoryActions => Get(nameof(SettingsCategoryActions));

    /// <summary>Вкл</summary>
    public static string SettingsActionsColumnEnabled => Get(nameof(SettingsActionsColumnEnabled));

    /// <summary>Название</summary>
    public static string SettingsActionsColumnTitle => Get(nameof(SettingsActionsColumnTitle));

    /// <summary>Для чего</summary>
    public static string SettingsActionsColumnTypes => Get(nameof(SettingsActionsColumnTypes));

    /// <summary>Программа</summary>
    public static string SettingsActionsColumnProgram => Get(nameof(SettingsActionsColumnProgram));

    /// <summary>Аргументы</summary>
    public static string SettingsActionsColumnArguments => Get(nameof(SettingsActionsColumnArguments));

    /// <summary>Режим</summary>
    public static string SettingsActionsColumnMode => Get(nameof(SettingsActionsColumnMode));

    /// <summary>Где</summary>
    public static string SettingsActionsColumnPlacement => Get(nameof(SettingsActionsColumnPlacement));

    /// <summary>В подменю</summary>
    public static string SettingsActionsColumnSubmenu => Get(nameof(SettingsActionsColumnSubmenu));

    /// <summary>Скрыть консоль</summary>
    public static string SettingsActionsColumnHideConsole => Get(nameof(SettingsActionsColumnHideConsole));

    /// <summary>Выход</summary>
    public static string SettingsActionsColumnOutput => Get(nameof(SettingsActionsColumnOutput));

    /// <summary>Маска вместо группы, например *.psd;*.ai. Непустая маска главнее группы и к папкам не подходит</summary>
    public static string SettingsActionsMaskHint => Get(nameof(SettingsActionsMaskHint));

    /// <summary>Подстановки: {path} — файл, {name} — его имя без расширения, …</summary>
    public static string SettingsActionsPlaceholders => Get(nameof(SettingsActionsPlaceholders));

    /// <summary>Имя результата рядом с исходным, например {name}.mp4. …</summary>
    public static string SettingsActionsOutputHint => Get(nameof(SettingsActionsOutputHint));

    /// <summary>Встроенные действия не удаляются и не правятся — …</summary>
    public static string SettingsActionsHint => Get(nameof(SettingsActionsHint));

    /// <summary>на каждый файл</summary>
    public static string ActionsModePerFile => Get(nameof(ActionsModePerFile));

    /// <summary>все одной командой</summary>
    public static string ActionsModeOnce => Get(nameof(ActionsModeOnce));

    /// <summary>оба меню</summary>
    public static string ActionsPlacementBoth => Get(nameof(ActionsPlacementBoth));

    /// <summary>контекстное</summary>
    public static string ActionsPlacementContext => Get(nameof(ActionsPlacementContext));

    /// <summary>шапка</summary>
    public static string ActionsPlacementHeader => Get(nameof(ActionsPlacementHeader));

    /// <summary>Программа {0} не найдена — укажите её на странице «Программы»</summary>
    public static string ActionsToolMissingHint => Get(nameof(ActionsToolMissingHint));

    /// <summary>Добавить</summary>
    public static string ActionsAdd => Get(nameof(ActionsAdd));

    /// <summary>Копировать</summary>
    public static string ActionsCopy => Get(nameof(ActionsCopy));

    /// <summary>Удалить</summary>
    public static string ActionsRemove => Get(nameof(ActionsRemove));

    /// <summary>Встроенное действие не удаляется — его выключают галочкой</summary>
    public static string ActionsRemoveHint => Get(nameof(ActionsRemoveHint));

    /// <summary>Новое действие</summary>
    public static string ActionsNewTitle => Get(nameof(ActionsNewTitle));

    /// <summary>{0} (копия)</summary>
    public static string ActionsCopyTitle => Get(nameof(ActionsCopyTitle));

    /// <summary>Выполняется действие</summary>
    public static string ProgressRunningAction => Get(nameof(ProgressRunningAction));

    /// <summary>{0}: готово {1} из {2}</summary>
    public static string StatusActionDone => Get(nameof(StatusActionDone));

    /// <summary>{0}: не удалось — {1}</summary>
    public static string StatusActionFailed => Get(nameof(StatusActionFailed));

    /// <summary>Готово {0} из {1}. Не получилось:</summary>
    public static string ActionReportHeader => Get(nameof(ActionReportHeader));

    /// <summary>Папка для результата</summary>
    public static string ActionsPickFolderTitle => Get(nameof(ActionsPickFolderTitle));

    /// <summary>Программы</summary>
    public static string SettingsCategoryTools => Get(nameof(SettingsCategoryTools));

    /// <summary>Программы, которые нужны встроенным действиям. …</summary>
    public static string SettingsToolsHint => Get(nameof(SettingsToolsHint));

    /// <summary>видео и аудио, WebP</summary>
    public static string ToolsPurposeFfmpeg => Get(nameof(ToolsPurposeFfmpeg));

    /// <summary>документы в PDF</summary>
    public static string ToolsPurposeLibreOffice => Get(nameof(ToolsPurposeLibreOffice));

    /// <summary>Markdown ↔ DOCX</summary>
    public static string ToolsPurposePandoc => Get(nameof(ToolsPurposePandoc));

    /// <summary>Найдена в системе</summary>
    public static string ToolsStatusFound => Get(nameof(ToolsStatusFound));

    /// <summary>Указана вручную</summary>
    public static string ToolsStatusSpecified => Get(nameof(ToolsStatusSpecified));

    /// <summary>Не найдена</summary>
    public static string ToolsStatusMissing => Get(nameof(ToolsStatusMissing));

    /// <summary>По указанному пути файла нет</summary>
    public static string ToolsStatusSpecifiedMissing => Get(nameof(ToolsStatusSpecifiedMissing));

    /// <summary>Поставить: winget install {0}</summary>
    public static string ToolsInstallHint => Get(nameof(ToolsInstallHint));

    /// <summary>Указать…</summary>
    public static string ToolsPointAt => Get(nameof(ToolsPointAt));

    /// <summary>Где {0}?</summary>
    public static string ToolsPointAtTitle => Get(nameof(ToolsPointAtTitle));

    /// <summary>Программы (*.exe)|*.exe</summary>
    public static string ToolsProgramFilter => Get(nameof(ToolsProgramFilter));

    /// <summary>Сбросить</summary>
    public static string ToolsReset => Get(nameof(ToolsReset));

    /// <summary>Забыть указанный файл и снова искать программу самому</summary>
    public static string ToolsResetHint => Get(nameof(ToolsResetHint));
}
