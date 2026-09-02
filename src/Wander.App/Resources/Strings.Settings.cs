namespace Wander.App.Resources;

/// <summary>
/// Настройки: страницы диалога, их поля и подсказки.
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
    /// <summary>Настройки Wander</summary>
    public static string SettingsTitle => Get(nameof(SettingsTitle));

    /// <summary>Основное</summary>
    public static string SettingsCategoryGeneral => Get(nameof(SettingsCategoryGeneral));

    /// <summary>Размеры</summary>
    public static string SettingsCategoryLayout => Get(nameof(SettingsCategoryLayout));

    /// <summary>Миниатюры</summary>
    public static string SettingsCategoryThumbnails => Get(nameof(SettingsCategoryThumbnails));

    /// <summary>Закладки</summary>
    public static string SettingsCategoryBookmarks => Get(nameof(SettingsCategoryBookmarks));

    /// <summary>Контекстное меню</summary>
    public static string SettingsCategoryContextMenu => Get(nameof(SettingsCategoryContextMenu));

    /// <summary>Отладка</summary>
    public static string SettingsCategoryDebug => Get(nameof(SettingsCategoryDebug));

    /// <summary>Открывать последнюю папку при запуске</summary>
    public static string SettingsRestoreLastFolder => Get(nameof(SettingsRestoreLastFolder));

    /// <summary>Иначе — первый доступный диск.</summary>
    public static string SettingsRestoreLastFolderHint => Get(nameof(SettingsRestoreLastFolderHint));

    /// <summary>Скрытые файлы и папки</summary>
    public static string SettingsShowHidden => Get(nameof(SettingsShowHidden));

    /// <summary>Защищённые системные файлы</summary>
    public static string SettingsShowSystem => Get(nameof(SettingsShowSystem));

    /// <summary>$RECYCLE.BIN, System Volume Information, Recovery и подобные. Внутрь н</summary>
    public static string SettingsHideSystemRootFoldersHint => Get(nameof(SettingsHideSystemRootFoldersHint));

    /// <summary>Спрашивать при удалении в корзину</summary>
    public static string SettingsConfirmRecycle => Get(nameof(SettingsConfirmRecycle));

    /// <summary>Ctrl+Z возвращает из корзины в любом случае. Shift+Delete спрашивает в</summary>
    public static string SettingsConfirmRecycleHint => Get(nameof(SettingsConfirmRecycleHint));

    /// <summary>Спрашивать при перемещении</summary>
    public static string SettingsConfirmMove => Get(nameof(SettingsConfirmMove));

    /// <summary>Перетаскивание с перемещением и вставка после Ctrl+X. Ctrl+Z возвраща</summary>
    public static string SettingsConfirmMoveHint => Get(nameof(SettingsConfirmMoveHint));

    /// <summary>Файл и его спутники — одним элементом</summary>
    public static string SettingsIntegrateCompanions => Get(nameof(SettingsIntegrateCompanions));

    /// <summary>Спутник — служебный файл рядом с основным: Unity-шный «.meta», настрой</summary>
    public static string SettingsCompanionsHint => Get(nameof(SettingsCompanionsHint));

    /// <summary>Режим «Крупные значки»</summary>
    public static string SettingsLargeIconsGroup => Get(nameof(SettingsLargeIconsGroup));

    /// <summary>Ширина ячейки (px)</summary>
    public static string SettingsCellWidth => Get(nameof(SettingsCellWidth));

    /// <summary>Размер иконки (px)</summary>
    public static string SettingsIconSize => Get(nameof(SettingsIconSize));

    /// <summary>Отступ между ячейками (px)</summary>
    public static string SettingsCellMargin => Get(nameof(SettingsCellMargin));

    /// <summary>Размер шрифта подписи</summary>
    public static string SettingsLabelFontSize => Get(nameof(SettingsLabelFontSize));

    /// <summary>Хранить миниатюры на диске между запусками</summary>
    public static string SettingsThumbnailDiskCache => Get(nameof(SettingsThumbnailDiskCache));

    /// <summary>Кэш на диске, не больше (МБ)</summary>
    public static string SettingsThumbnailDiskMb => Get(nameof(SettingsThumbnailDiskMb));

    /// <summary>Кэш в памяти (миниатюр)</summary>
    public static string SettingsThumbnailMemory => Get(nameof(SettingsThumbnailMemory));

    /// <summary>Очистить кэш</summary>
    public static string SettingsClearCache => Get(nameof(SettingsClearCache));

    /// <summary>Миниатюра строится один раз на файл и переиспользуется, п…</summary>
    public static string SettingsThumbnailsHint => Get(nameof(SettingsThumbnailsHint));

    /// <summary>Значения зажимаются в разумные пределы автоматически: 16……</summary>
    public static string SettingsThumbnailsLimitsHint => Get(nameof(SettingsThumbnailsLimitsHint));

    /// <summary>Кэш на диске выключен.</summary>
    public static string SettingsCacheOff => Get(nameof(SettingsCacheOff));

    /// <summary>Сейчас занято: {0} {1}</summary>
    public static string SettingsCacheUsage => Get(nameof(SettingsCacheUsage));

    /// <summary>Загрузки</summary>
    public static string SettingsBookmarkDownloads => Get(nameof(SettingsBookmarkDownloads));

    /// <summary>Документы</summary>
    public static string SettingsBookmarkDocuments => Get(nameof(SettingsBookmarkDocuments));

    /// <summary>Изображения</summary>
    public static string SettingsBookmarkPictures => Get(nameof(SettingsBookmarkPictures));

    /// <summary>Корзина</summary>
    public static string SettingsBookmarkRecycleBin => Get(nameof(SettingsBookmarkRecycleBin));

    /// <summary>Спец-папки берутся у системы: имя зависит от языка Windows, и папку мо</summary>
    public static string SettingsBookmarksHint => Get(nameof(SettingsBookmarksHint));

    /// <summary>Показывать пункты сторонних приложений</summary>
    public static string SettingsShellExtensions => Get(nameof(SettingsShellExtensions));

    /// <summary>Пункты сторонних приложений</summary>
    public static string SettingsShellExtensionsGroup => Get(nameof(SettingsShellExtensionsGroup));

    /// <summary>Наведите на строку — увидите, что делает пункт. «Добавить» вытаскивает</summary>
    public static string SettingsShellExtensionsHint => Get(nameof(SettingsShellExtensionsHint));

    /// <summary>Пункты Wander</summary>
    public static string SettingsOwnItemsGroup => Get(nameof(SettingsOwnItemsGroup));

    /// <summary>Скрытый пункт пропадает из меню, хоткей продолжает работать. Подменю с</summary>
    public static string SettingsOwnItemsHint => Get(nameof(SettingsOwnItemsHint));

    /// <summary>Показывать меню «Отладка» в главном меню</summary>
    public static string SettingsShowDebugMenu => Get(nameof(SettingsShowDebugMenu));

    /// <summary>Когда выключено, в основном меню больше не появится пункт…</summary>
    public static string SettingsDebugHint => Get(nameof(SettingsDebugHint));

    /// <summary>Сброс настроек</summary>
    public static string SettingsResetGroup => Get(nameof(SettingsResetGroup));

    /// <summary>Сбросить всё</summary>
    public static string SettingsResetAll => Get(nameof(SettingsResetAll));

    /// <summary>Вернёт все настройки к стандартным значениям и спросит по…</summary>
    public static string SettingsResetHint => Get(nameof(SettingsResetHint));

    /// <summary>Сбросить все настройки к стандартным значениям?  Вернутся…</summary>
    public static string SettingsResetConfirm => Get(nameof(SettingsResetConfirm));

    /// <summary>Обновлять список автоматически</summary>
    public static string SettingsAutoRefresh => Get(nameof(SettingsAutoRefresh));

    /// <summary>Список перерисовывается, когда файлы меняет кто-то другой. Выключено —</summary>
    public static string SettingsAutoRefreshHint => Get(nameof(SettingsAutoRefreshHint));

    /// <summary>Быстрое чтение первых файлов при открытии папки</summary>
    public static string SettingsVisibleFirstLoading => Get(nameof(SettingsVisibleFirstLoading));

    /// <summary>Значки и миниатюры того, что видно на экране, читаются раньше всего остального…</summary>
    public static string SettingsVisibleFirstLoadingHint => Get(nameof(SettingsVisibleFirstLoadingHint));

    /// <summary>Режим «Таблица»</summary>
    public static string SettingsDetailsGroup => Get(nameof(SettingsDetailsGroup));

    /// <summary>Режим «Плитки»</summary>
    public static string SettingsTilesGroup => Get(nameof(SettingsTilesGroup));

    /// <summary>Высота строки (px)</summary>
    public static string SettingsRowHeight => Get(nameof(SettingsRowHeight));

    /// <summary>Ширина плитки (px)</summary>
    public static string SettingsTileWidth => Get(nameof(SettingsTileWidth));

    /// <summary>То же самое делает Ctrl + колесо мыши прямо в списке — каждый вид меняется отдельно, и то, что накрутили колесом, попадает в эти же поля.</summary>
    public static string SettingsZoomHint => Get(nameof(SettingsZoomHint));

    /// <summary>Галерея</summary>
    public static string SettingsCategoryGallery => Get(nameof(SettingsCategoryGallery));

    /// <summary>Режим «Галерея»</summary>
    public static string SettingsGalleryGroup => Get(nameof(SettingsGalleryGroup));

    /// <summary>Фон</summary>
    public static string SettingsGalleryBackground => Get(nameof(SettingsGalleryBackground));

    /// <summary>Включать галерею в папках со снимками</summary>
    public static string SettingsAutoGallery => Get(nameof(SettingsAutoGallery));

    /// <summary>Если больше половины содержательных файлов в папке — изображения, вид переключается на галерею сам. Сайдкары (.pp3, .xmp, .meta) и резервные копии в счёт не идут. Стоит выбрать вид в папке вручную — и в ней автоматика больше не вмешивается.</summary>
    public static string SettingsAutoGalleryHint => Get(nameof(SettingsAutoGalleryHint));

    /// <summary>Оценки</summary>
    public static string SettingsRatingGroup => Get(nameof(SettingsRatingGroup));

    /// <summary>Спрашивать перед созданием файла оценки</summary>
    public static string SettingsConfirmCreateSidecar => Get(nameof(SettingsConfirmCreateSidecar));

    /// <summary>Первая оценка снимка без сайдкара создаёт файл рядом с ним. Снятая г</summary>
    public static string SettingsConfirmCreateSidecarHint => Get(nameof(SettingsConfirmCreateSidecarHint));

    /// <summary>Формат оценки, если сайдкара нет</summary>
    public static string SettingsRawRatingFormat => Get(nameof(SettingsRawRatingFormat));

    /// <summary>.xmp — Adobe, darktable, RawTherapee 5.11+</summary>
    public static string SettingsRawRatingFormatXmp => Get(nameof(SettingsRawRatingFormatXmp));

    /// <summary>.pp3 — RawTherapee</summary>
    public static string SettingsRawRatingFormatPp3 => Get(nameof(SettingsRawRatingFormatPp3));

    /// <summary>Wander не создаёт файлы, о которых не просили: первая оценка снимка без сайдкара спрашивает подтверждение. Формат по-умолчанию — .xmp, потому что он ни на что, кроме оценки, не влияет. Появившийся .pp3 отменяет применение профиля по-умолчанию в RawTherapee.</summary>
    public static string SettingsRawRatingFormatHint => Get(nameof(SettingsRawRatingFormatHint));

    /// <summary>Яркость серого фона (0–255)</summary>
    public static string SettingsGalleryGreyLevel => Get(nameof(SettingsGalleryGreyLevel));

    /// <summary>Яркость тёмного фона (0–255)</summary>
    public static string SettingsGalleryDarkLevel => Get(nameof(SettingsGalleryDarkLevel));

    /// <summary>Доля изображений для включения (%)</summary>
    public static string SettingsAutoGalleryPercent => Get(nameof(SettingsAutoGalleryPercent));

    /// <summary>Клавиатура</summary>
    public static string SettingsCategoryHotkeys => Get(nameof(SettingsCategoryHotkeys));

    /// <summary>Полный список сочетаний. Переназначение пока не поддерживается — сочетания заданы приложением.</summary>
    public static string SettingsHotkeysHint => Get(nameof(SettingsHotkeysHint));

    /// <summary>Поиск по сочетанию или по действию</summary>
    public static string SettingsHotkeysSearch => Get(nameof(SettingsHotkeysSearch));

    /// <summary>Ничего не нашлось.</summary>
    public static string SettingsHotkeysNoMatch => Get(nameof(SettingsHotkeysNoMatch));

    /// <summary>все файлы</summary>
    public static string ScopeAllFiles => Get(nameof(ScopeAllFiles));

    /// <summary>файлы и папки</summary>
    public static string ScopeAllObjects => Get(nameof(ScopeAllObjects));

    /// <summary>папки</summary>
    public static string ScopeDirectory => Get(nameof(ScopeDirectory));

    /// <summary>фон папки</summary>
    public static string ScopeBackground => Get(nameof(ScopeBackground));

    /// <summary>папки и архивы</summary>
    public static string ScopeFolder => Get(nameof(ScopeFolder));

    /// <summary>диски</summary>
    public static string ScopeDrive => Get(nameof(ScopeDrive));

    /// <summary>Скрыть</summary>
    public static string SettingsShellColumnHide => Get(nameof(SettingsShellColumnHide));

    /// <summary>Пункт</summary>
    public static string SettingsShellColumnItem => Get(nameof(SettingsShellColumnItem));

    /// <summary>Приложение</summary>
    public static string SettingsShellColumnApp => Get(nameof(SettingsShellColumnApp));

    /// <summary>Где показывается</summary>
    public static string SettingsShellColumnScopes => Get(nameof(SettingsShellColumnScopes));

    /// <summary>Добавить...</summary>
    public static string SettingsShellAdd => Get(nameof(SettingsShellAdd));

    /// <summary>Показывать системные</summary>
    public static string SettingsShellShowSystem => Get(nameof(SettingsShellShowSystem));

    /// <summary>—</summary>
    public static string SettingsShellScopeUnknown => Get(nameof(SettingsShellScopeUnknown));

    /// <summary>Весь раздел меню этого приложения. Что именно оно нарисует внутри, решается в момент открытия меню — заранее этого не знает никто.</summary>
    public static string SettingsShellAppSection => Get(nameof(SettingsShellAppSection));

    /// <summary>Добавить в таблицу</summary>
    public static string PickerTitle => Get(nameof(PickerTitle));

    /// <summary>Выберите приложение — попадут все его расширения — либо отдельные типы</summary>
    public static string PickerHint => Get(nameof(PickerHint));

    /// <summary>Приложение</summary>
    public static string PickerByApp => Get(nameof(PickerByApp));

    /// <summary>Тип файла</summary>
    public static string PickerByType => Get(nameof(PickerByType));

    /// <summary>недавно открывали</summary>
    public static string PickerRecentNote => Get(nameof(PickerRecentNote));

    /// <summary>Рабочий стол</summary>
    public static string SettingsBookmarkDesktop => Get(nameof(SettingsBookmarkDesktop));

    /// <summary>Музыка</summary>
    public static string SettingsBookmarkMusic => Get(nameof(SettingsBookmarkMusic));

    /// <summary>Видео</summary>
    public static string SettingsBookmarkVideos => Get(nameof(SettingsBookmarkVideos));

    /// <summary>Стрелки в дереве открывают папку</summary>
    public static string SettingsTreeKeyboardNavigates => Get(nameof(SettingsTreeKeyboardNavigates));

    /// <summary>Поведение проводника. Выключено — стрелки двигают курсор, открывает En</summary>
    public static string SettingsTreeKeyboardNavigatesHint => Get(nameof(SettingsTreeKeyboardNavigatesHint));

    /// <summary>Показывать скрытые файлы</summary>
    public static string PanelShowHidden => Get(nameof(PanelShowHidden));

    /// <summary>Показывать системные файлы</summary>
    public static string PanelShowSystem => Get(nameof(PanelShowSystem));

    /// <summary>Стрелки в дереве открывают папку</summary>
    public static string PanelTreeKeyboardNavigates => Get(nameof(PanelTreeKeyboardNavigates));

    /// <summary>Поведение проводника. Выключено — стрелки только двигают курсор, откры</summary>
    public static string PanelTreeKeyboardNavigatesHint => Get(nameof(PanelTreeKeyboardNavigatesHint));

    /// <summary>Спрашивать при удалении в корзину</summary>
    public static string PanelConfirmRecycle => Get(nameof(PanelConfirmRecycle));

    /// <summary>Ctrl+Z возвращает из корзины в любом случае; Shift+Delete спрашивает в</summary>
    public static string PanelConfirmRecycleHint => Get(nameof(PanelConfirmRecycleHint));

    /// <summary>Обновлять список автоматически</summary>
    public static string PanelAutoRefresh => Get(nameof(PanelAutoRefresh));

    /// <summary>Отображение</summary>
    public static string SettingsCategoryVisibility => Get(nameof(SettingsCategoryVisibility));

    /// <summary>Свои закладки добавляются перетаскиванием в панель.</summary>
    public static string SettingsBookmarksAddHint => Get(nameof(SettingsBookmarksAddHint));

    /// <summary>Хоткей</summary>
    public static string SettingsOwnColumnGesture => Get(nameof(SettingsOwnColumnGesture));

    /// <summary>Приложение и «где показывается» приходят из реестра, названия — из сам</summary>
    public static string SettingsShellExtensionsTableHint => Get(nameof(SettingsShellExtensionsTableHint));

    /// <summary>Служебные папки в корне дисков</summary>
    public static string SettingsShowSystemRootFolders => Get(nameof(SettingsShowSystemRootFolders));

    /// <summary>Показывать</summary>
    public static string SettingsShowGroup => Get(nameof(SettingsShowGroup));

    /// <summary>Документ.txt</summary>
    public static string SettingsPreviewName => Get(nameof(SettingsPreviewName));

    /// <summary>Фотография.jpg</summary>
    public static string SettingsPreviewName2 => Get(nameof(SettingsPreviewName2));

    /// <summary>14 КБ · Текстовый файл</summary>
    public static string SettingsPreviewMeta => Get(nameof(SettingsPreviewMeta));

    /// <summary>Сбросить</summary>
    public static string SettingsShellReset => Get(nameof(SettingsShellReset));

    /// <summary>Вернуть настройки контекстного меню к исходным</summary>
    public static string SettingsShellResetHint => Get(nameof(SettingsShellResetHint));

    /// <summary>Сбросить настройки контекстного меню?  Снимутся все галочки «скрыть», </summary>
    public static string SettingsShellResetConfirm => Get(nameof(SettingsShellResetConfirm));
}
