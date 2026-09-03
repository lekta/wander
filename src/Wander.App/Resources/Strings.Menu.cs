namespace Wander.App.Resources;

/// <summary>
/// Меню и навигация: главное меню, контекстное, адресная строка, закладки.
///
/// <para>
/// Часть <see cref="Strings"/>, разложенная по файлам: ключей больше
/// четырёхсот, и одним файлом на полторы тысячи строк пользоваться нельзя.
/// Ресурсы при этом лежат в одном <c>Strings.resx</c> — делить ещё и его
/// значило бы искать ключ по нескольким <c>ResourceManager</c> подряд, а
/// выигрыш тот же самый. Разбор — в BACKLOG.md.
/// </para>
/// </summary>
public static partial class Strings {
    /// <summary>Назад (Alt+←)</summary>
    public static string NavBack => Get(nameof(NavBack));

    /// <summary>Вперёд (Alt+→)</summary>
    public static string NavForward => Get(nameof(NavForward));

    /// <summary>Вверх (Alt+↑ / Backspace)</summary>
    public static string NavUp => Get(nameof(NavUp));

    /// <summary>Вид</summary>
    public static string MenuView => Get(nameof(MenuView));

    /// <summary>Таблица</summary>
    public static string MenuViewDetails => Get(nameof(MenuViewDetails));

    /// <summary>Плитка</summary>
    public static string MenuViewTiles => Get(nameof(MenuViewTiles));

    /// <summary>Крупные значки</summary>
    public static string MenuViewLargeIcons => Get(nameof(MenuViewLargeIcons));

    /// <summary>Сортировка</summary>
    public static string MenuSortBy => Get(nameof(MenuSortBy));

    /// <summary>Имя</summary>
    public static string MenuSortName => Get(nameof(MenuSortName));

    /// <summary>Дата изменения</summary>
    public static string MenuSortDate => Get(nameof(MenuSortDate));

    /// <summary>Размер</summary>
    public static string MenuSortSize => Get(nameof(MenuSortSize));

    /// <summary>Тип</summary>
    public static string MenuSortType => Get(nameof(MenuSortType));

    /// <summary>По возрастанию</summary>
    public static string MenuSortAscending => Get(nameof(MenuSortAscending));

    /// <summary>Папки сверху</summary>
    public static string MenuSortFoldersFirst => Get(nameof(MenuSortFoldersFirst));

    /// <summary>Обновить</summary>
    public static string MenuRefresh => Get(nameof(MenuRefresh));

    /// <summary>Область просмотра</summary>
    public static string MenuQuickPreview => Get(nameof(MenuQuickPreview));

    /// <summary>Параметры</summary>
    public static string MenuOptions => Get(nameof(MenuOptions));

    /// <summary>Обратная связь</summary>
    public static string MenuReportIssue => Get(nameof(MenuReportIssue));

    /// <summary>Откроет на GitHub форму — баг или пожелание</summary>
    public static string MenuReportIssueHint => Get(nameof(MenuReportIssueHint));

    /// <summary>Отладка</summary>
    public static string MenuDebug => Get(nameof(MenuDebug));

    /// <summary>Журнал</summary>
    public static string MenuLogs => Get(nameof(MenuLogs));

    /// <summary>Открыть файл журнала текущего сеанса</summary>
    public static string MenuLogsHint => Get(nameof(MenuLogsHint));

    /// <summary>Журнал действий за сеанс — открыть в текстовом просмотрщике</summary>
    public static string JournalTooltip => Get(nameof(JournalTooltip));

    /// <summary>Открыта папка: {0}</summary>
    public static string JournalOpenedFolder => Get(nameof(JournalOpenedFolder));

    /// <summary>Выход</summary>
    public static string MenuExit => Get(nameof(MenuExit));

    /// <summary>Имя</summary>
    public static string ColumnName => Get(nameof(ColumnName));

    /// <summary>Тип</summary>
    public static string ColumnType => Get(nameof(ColumnType));

    /// <summary>Размер</summary>
    public static string ColumnSize => Get(nameof(ColumnSize));

    /// <summary>Изменён</summary>
    public static string ColumnModified => Get(nameof(ColumnModified));

    /// <summary>Копировать</summary>
    public static string ActionCopy => Get(nameof(ActionCopy));

    /// <summary>ОК</summary>
    public static string ActionOk => Get(nameof(ActionOk));

    /// <summary>Отмена</summary>
    public static string ActionCancel => Get(nameof(ActionCancel));

    /// <summary>Открыть с помощью</summary>
    public static string MenuCmdOpenSubmenu => Get(nameof(MenuCmdOpenSubmenu));

    /// <summary>Файл</summary>
    public static string MenuCmdFileSubmenu => Get(nameof(MenuCmdFileSubmenu));

    /// <summary>Открыть</summary>
    public static string MenuCmdOpen => Get(nameof(MenuCmdOpen));

    /// <summary>Выбрать приложение...</summary>
    public static string MenuCmdOpenWith => Get(nameof(MenuCmdOpenWith));

    /// <summary>Открыть в терминале</summary>
    public static string MenuCmdOpenInTerminal => Get(nameof(MenuCmdOpenInTerminal));

    /// <summary>Вырезать</summary>
    public static string MenuCmdCut => Get(nameof(MenuCmdCut));

    /// <summary>Копировать</summary>
    public static string MenuCmdCopy => Get(nameof(MenuCmdCopy));

    /// <summary>Вставить</summary>
    public static string MenuCmdPaste => Get(nameof(MenuCmdPaste));

    /// <summary>Копировать путь</summary>
    public static string MenuCmdCopyPath => Get(nameof(MenuCmdCopyPath));

    /// <summary>Копировать имя</summary>
    public static string MenuCmdCopyName => Get(nameof(MenuCmdCopyName));

    /// <summary>Создать ярлык</summary>
    public static string MenuCmdCreateShortcut => Get(nameof(MenuCmdCreateShortcut));

    /// <summary>Переименовать</summary>
    public static string MenuCmdRename => Get(nameof(MenuCmdRename));

    /// <summary>Удалить</summary>
    public static string MenuCmdDelete => Get(nameof(MenuCmdDelete));

    /// <summary>Папка</summary>
    public static string MenuCmdNewFolder => Get(nameof(MenuCmdNewFolder));

    /// <summary>Восстановить</summary>
    public static string MenuCmdRestore => Get(nameof(MenuCmdRestore));

    /// <summary>Свойства</summary>
    public static string MenuCmdProperties => Get(nameof(MenuCmdProperties));

    /// <summary>Применить</summary>
    public static string ActionApply => Get(nameof(ActionApply));

    /// <summary>Закладки</summary>
    public static string BookmarksHeader => Get(nameof(BookmarksHeader));

    /// <summary>Свернуть / развернуть закладки</summary>
    public static string BookmarksToggleHint => Get(nameof(BookmarksToggleHint));

    /// <summary>Убрать из закладок</summary>
    public static string BookmarksRemove => Get(nameof(BookmarksRemove));

    /// <summary>Переместить вверх</summary>
    public static string BookmarksMoveUp => Get(nameof(BookmarksMoveUp));

    /// <summary>Переместить вниз</summary>
    public static string BookmarksMoveDown => Get(nameof(BookmarksMoveDown));

    /// <summary>Указать расположение…</summary>
    public static string BookmarksLocate => Get(nameof(BookmarksLocate));

    /// <summary>Где теперь эта папка?</summary>
    public static string BookmarksLocateTitle => Get(nameof(BookmarksLocateTitle));

    /// <summary>Папка была удалена или недоступна</summary>
    public static string MissingFolderTitle => Get(nameof(MissingFolderTitle));

    /// <summary>Перетащите сюда папку, чтобы добавить её в закладки</summary>
    public static string BookmarksAddHint => Get(nameof(BookmarksAddHint));

    /// <summary>Действия с закладкой</summary>
    public static string BookmarksRowMenuHint => Get(nameof(BookmarksRowMenuHint));

    /// <summary>В закладки можно перетаскивать только папки.</summary>
    public static string BookmarksFoldersOnly => Get(nameof(BookmarksFoldersOnly));

    /// <summary>Загрузки</summary>
    public static string SpecialFolderDownloads => Get(nameof(SpecialFolderDownloads));

    /// <summary>Документы</summary>
    public static string SpecialFolderDocuments => Get(nameof(SpecialFolderDocuments));

    /// <summary>Изображения</summary>
    public static string SpecialFolderPictures => Get(nameof(SpecialFolderPictures));

    /// <summary>Корзина</summary>
    public static string SpecialFolderRecycleBin => Get(nameof(SpecialFolderRecycleBin));

    /// <summary>Галерея</summary>
    public static string MenuViewGallery => Get(nameof(MenuViewGallery));

    /// <summary>Оценка</summary>
    public static string MenuSortRating => Get(nameof(MenuSortRating));

    /// <summary>Оценка</summary>
    public static string ColumnRating => Get(nameof(ColumnRating));

    /// <summary>Светлый</summary>
    public static string GalleryBackgroundLight => Get(nameof(GalleryBackgroundLight));

    /// <summary>Серый</summary>
    public static string GalleryBackgroundGrey => Get(nameof(GalleryBackgroundGrey));

    /// <summary>Тёмный</summary>
    public static string GalleryBackgroundDark => Get(nameof(GalleryBackgroundDark));

    /// <summary>Папка</summary>
    public static string ColumnFolder => Get(nameof(ColumnFolder));

    /// <summary>Совпадение</summary>
    public static string ColumnMatch => Get(nameof(ColumnMatch));

    /// <summary>О Wander</summary>
    public static string MenuAbout => Get(nameof(MenuAbout));

    /// <summary>Помощь</summary>
    public static string MenuHelp => Get(nameof(MenuHelp));

    /// <summary>Откроет руководство в браузере: что умеет Wander и какие есть хоткеи</summary>
    public static string MenuHelpHint => Get(nameof(MenuHelpHint));

    /// <summary>Версия {0}</summary>
    public static string MenuVersion => Get(nameof(MenuVersion));

    /// <summary>Создать</summary>
    public static string MenuCmdNewSubmenu => Get(nameof(MenuCmdNewSubmenu));

    /// <summary>Рабочий стол</summary>
    public static string SpecialFolderDesktop => Get(nameof(SpecialFolderDesktop));

    /// <summary>Музыка</summary>
    public static string SpecialFolderMusic => Get(nameof(SpecialFolderMusic));

    /// <summary>Видео</summary>
    public static string SpecialFolderVideos => Get(nameof(SpecialFolderVideos));
}
