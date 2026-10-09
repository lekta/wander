namespace Wander.App.Resources;

/// <summary>
/// Настройки: страницы диалога, их поля и подсказки.
///
/// <para>
/// Часть <see cref="Strings"/>, разложенная по файлам: ключей больше
/// четырёхсот, и одним файлом на полторы тысячи строк пользоваться нельзя.
/// Ресурсы при этом лежат в одном <c>Strings.resx</c> — делить ещё и его
/// значило бы искать ключ по нескольким <c>ResourceManager</c> подряд, а
/// выигрыш тот же самый. Разбор — в REJECTED.md.
/// </para>
/// </summary>
public static partial class Strings {
    /// <summary>Настройки Wander</summary>
    public static string SettingsTitle => Get(nameof(SettingsTitle));

    /// <summary>Контекстное меню</summary>
    public static string SettingsCategoryContextMenu => Get(nameof(SettingsCategoryContextMenu));

    /// <summary>Отладка и сброс</summary>
    public static string SettingsCategoryDebug => Get(nameof(SettingsCategoryDebug));

    /// <summary>Последняя папка</summary>
    public static string SettingsRestoreLastFolder => Get(nameof(SettingsRestoreLastFolder));

    /// <summary>Папки больше нет — открывается ближайшая уцелевшая над ней; была на…</summary>
    public static string SettingsRestoreLastFolderHint => Get(nameof(SettingsRestoreLastFolderHint));

    /// <summary>Рабочая папка</summary>
    public static string SettingsWorkFolder => Get(nameof(SettingsWorkFolder));

    /// <summary>{0} (системные «Документы»)</summary>
    public static string SettingsWorkFolderDefault => Get(nameof(SettingsWorkFolderDefault));

    /// <summary>Выбрать рабочую папку</summary>
    public static string SettingsWorkFolderPick => Get(nameof(SettingsWorkFolderPick));

    /// <summary>Вернуть системные «Документы»</summary>
    public static string SettingsWorkFolderReset => Get(nameof(SettingsWorkFolderReset));

    /// <summary>Скрытые файлы и папки</summary>
    public static string SettingsShowHidden => Get(nameof(SettingsShowHidden));

    /// <summary>Защищённые системные файлы</summary>
    public static string SettingsShowSystem => Get(nameof(SettingsShowSystem));

    /// <summary>$RECYCLE.BIN, System Volume Information, pagefile.sys и подобные: в папки…</summary>
    public static string SettingsHideSystemRootFoldersHint => Get(nameof(SettingsHideSystemRootFoldersHint));

    /// <summary>Спрашивать при удалении в корзину</summary>
    public static string SettingsConfirmRecycle => Get(nameof(SettingsConfirmRecycle));

    /// <summary>Ctrl+Z вернёт из корзины в любом случае, а удаление навсегда (Shift+Delete)…</summary>
    public static string SettingsConfirmRecycleHint => Get(nameof(SettingsConfirmRecycleHint));

    /// <summary>Спрашивать при перемещении</summary>
    public static string SettingsConfirmMove => Get(nameof(SettingsConfirmMove));

    /// <summary>Перетаскивание с перемещением и вставка после Ctrl+X. Ctrl+Z возвраща</summary>
    public static string SettingsConfirmMoveHint => Get(nameof(SettingsConfirmMoveHint));

    /// <summary>Пропускать одинаковые файлы без вопроса</summary>
    public static string SettingsSkipIdentical => Get(nameof(SettingsSkipIdentical));

    /// <summary>Файл, совпадающий побайтово с тем, что уже лежит на месте, не копируется и…</summary>
    public static string SettingsSkipIdenticalHint => Get(nameof(SettingsSkipIdenticalHint));

    /// <summary>Входить в папку, если задержать над ней перетаскиваемый файл</summary>
    public static string SettingsDragHoverEntersFolders => Get(nameof(SettingsDragHoverEntersFolders));

    /// <summary>Папка в списке открывается через секунду с небольшим, и файл можно бросить…</summary>
    public static string SettingsDragHoverEntersFoldersHint => Get(nameof(SettingsDragHoverEntersFoldersHint));

    /// <summary>Держать в системной папке Temp</summary>
    public static string SettingsUseSystemTemp => Get(nameof(SettingsUseSystemTemp));

    /// <summary>Папка данных в портативном режиме — data рядом с exe. Копии старше суток…</summary>
    public static string SettingsUseSystemTempHint => Get(nameof(SettingsUseSystemTempHint));

    /// <summary>Показывать файл и его спутники одним элементом</summary>
    public static string SettingsIntegrateCompanions => Get(nameof(SettingsIntegrateCompanions));

    /// <summary>Значки</summary>
    public static string SettingsLargeIconsGroup => Get(nameof(SettingsLargeIconsGroup));

    /// <summary>Ширина ячейки, px</summary>
    public static string SettingsCellWidth => Get(nameof(SettingsCellWidth));

    /// <summary>Размер значка, px</summary>
    public static string SettingsIconSize => Get(nameof(SettingsIconSize));

    /// <summary>Отступ вокруг ячейки, px</summary>
    public static string SettingsCellMargin => Get(nameof(SettingsCellMargin));

    /// <summary>Размер шрифта подписи, px</summary>
    public static string SettingsLabelFontSize => Get(nameof(SettingsLabelFontSize));

    /// <summary>Хранить миниатюры между запусками</summary>
    public static string SettingsThumbnailDiskCache => Get(nameof(SettingsThumbnailDiskCache));

    /// <summary>Не больше, МБ</summary>
    public static string SettingsThumbnailDiskMb => Get(nameof(SettingsThumbnailDiskMb));

    /// <summary>Под картинки, МБ</summary>
    public static string SettingsPictureMemory => Get(nameof(SettingsPictureMemory));

    /// <summary>Миниатюры и кадры панели просмотра вместе. 0 — шестнадцатая часть памяти компьютера, здесь {0} МБ.</summary>
    public static string SettingsPictureMemoryHint => Get(nameof(SettingsPictureMemoryHint));

    /// <summary>Очистить кэш</summary>
    public static string SettingsClearCache => Get(nameof(SettingsClearCache));

    /// <summary>Кэш на диске выключен.</summary>
    public static string SettingsCacheOff => Get(nameof(SettingsCacheOff));

    /// <summary>Занято {0} в {1}</summary>
    public static string SettingsCacheUsage => Get(nameof(SettingsCacheUsage));

    /// <summary>Загрузки</summary>
    public static string SettingsBookmarkDownloads => Get(nameof(SettingsBookmarkDownloads));

    /// <summary>Документы</summary>
    public static string SettingsBookmarkDocuments => Get(nameof(SettingsBookmarkDocuments));

    /// <summary>Изображения</summary>
    public static string SettingsBookmarkPictures => Get(nameof(SettingsBookmarkPictures));

    /// <summary>Корзина</summary>
    public static string SettingsBookmarkRecycleBin => Get(nameof(SettingsBookmarkRecycleBin));

    /// <summary>Стандартные папки берутся у системы: имя зависит от языка Windows, и папка…</summary>
    public static string SettingsBookmarksHint => Get(nameof(SettingsBookmarksHint));

    /// <summary>Показывать в меню</summary>
    public static string SettingsShellExtensions => Get(nameof(SettingsShellExtensions));

    /// <summary>Пункты других программ</summary>
    public static string SettingsShellExtensionsGroup => Get(nameof(SettingsShellExtensionsGroup));

    /// <summary>Основные пункты</summary>
    public static string SettingsOwnItemsGroup => Get(nameof(SettingsOwnItemsGroup));

    /// <summary>Показывать в главном меню</summary>
    public static string SettingsShowDebugMenu => Get(nameof(SettingsShowDebugMenu));

    /// <summary>Лог сеанса</summary>
    public static string SettingsLogGroup => Get(nameof(SettingsLogGroup));

    /// <summary>Записывать клавиши, клики и выделение</summary>
    public static string SettingsLogActions => Get(nameof(SettingsLogActions));

    /// <summary>Каждое нажатие, клик и смена выделения в списке и в панелях папок…</summary>
    public static string SettingsLogActionsHint => Get(nameof(SettingsLogActionsHint));

    /// <summary>Писать настоящие пути к файлам</summary>
    public static string SettingsLogPaths => Get(nameof(SettingsLogPaths));

    /// <summary>Одна папка в пределах сеанса — одна метка. Первые строки сеанса, до чтения…</summary>
    public static string SettingsLogPathsHint => Get(nameof(SettingsLogPathsHint));

    /// <summary>Сброс настроек</summary>
    public static string SettingsResetGroup => Get(nameof(SettingsResetGroup));

    /// <summary>Сбросить все настройки…</summary>
    public static string SettingsResetAll => Get(nameof(SettingsResetAll));

    /// <summary>Сбросить все настройки к стандартным значениям?  Снова включатся…</summary>
    public static string SettingsResetConfirm => Get(nameof(SettingsResetConfirm));

    /// <summary>Показывать сразу</summary>
    public static string SettingsAutoRefresh => Get(nameof(SettingsAutoRefresh));

    /// <summary>Выключено — только по F5.</summary>
    public static string SettingsAutoRefreshHint => Get(nameof(SettingsAutoRefreshHint));

    /// <summary>Сначала то, что видно на экране</summary>
    public static string SettingsVisibleFirstLoading => Get(nameof(SettingsVisibleFirstLoading));

    /// <summary>Значки и миниатюры того, что видно на экране, читаются раньше всего остального…</summary>
    public static string SettingsVisibleFirstLoadingHint => Get(nameof(SettingsVisibleFirstLoadingHint));

    /// <summary>Таблица</summary>
    public static string SettingsDetailsGroup => Get(nameof(SettingsDetailsGroup));

    /// <summary>Плитка</summary>
    public static string SettingsTilesGroup => Get(nameof(SettingsTilesGroup));

    /// <summary>Высота строки, px</summary>
    public static string SettingsRowHeight => Get(nameof(SettingsRowHeight));

    /// <summary>Ширина плитки, px</summary>
    public static string SettingsTileWidth => Get(nameof(SettingsTileWidth));

    /// <summary>Галерея</summary>
    public static string SettingsCategoryGallery => Get(nameof(SettingsCategoryGallery));

    /// <summary>Галерея</summary>
    public static string SettingsGalleryGroup => Get(nameof(SettingsGalleryGroup));

    /// <summary>Фон галереи</summary>
    public static string SettingsGalleryBackground => Get(nameof(SettingsGalleryBackground));

    /// <summary>Включать галерею, если снимков в папке больше</summary>
    public static string SettingsAutoGallery => Get(nameof(SettingsAutoGallery));

    /// <summary>Папки с изображениями</summary>
    public static string SettingsPhotoFoldersGroup => Get(nameof(SettingsPhotoFoldersGroup));

    /// <summary>%</summary>
    public static string SettingsAutoGalleryUnit => Get(nameof(SettingsAutoGalleryUnit));

    /// <summary>Спрашивать перед созданием файла оценки</summary>
    public static string SettingsConfirmCreateSidecar => Get(nameof(SettingsConfirmCreateSidecar));

    /// <summary>Первая оценка снимка без спутника создаёт файл рядом с ним. Снятая г</summary>
    public static string SettingsConfirmCreateSidecarHint => Get(nameof(SettingsConfirmCreateSidecarHint));

    /// <summary>Файл оценки для снимка без спутника</summary>
    public static string SettingsRawRatingFormat => Get(nameof(SettingsRawRatingFormat));

    /// <summary>.xmp — Adobe, darktable, RawTherapee 5.11+</summary>
    public static string SettingsRawRatingFormatXmp => Get(nameof(SettingsRawRatingFormatXmp));

    /// <summary>.pp3 — RawTherapee</summary>
    public static string SettingsRawRatingFormatPp3 => Get(nameof(SettingsRawRatingFormatPp3));

    /// <summary>.xmp ни на что, кроме оценки, не влияет. .pp3 — файл настроек RawTherapee…</summary>
    public static string SettingsRawRatingFormatHint => Get(nameof(SettingsRawRatingFormatHint));

    /// <summary>Яркость серого, 0–255</summary>
    public static string SettingsGalleryGreyLevel => Get(nameof(SettingsGalleryGreyLevel));

    /// <summary>Яркость тёмного, 0–255</summary>
    public static string SettingsGalleryDarkLevel => Get(nameof(SettingsGalleryDarkLevel));

    /// <summary>Лупа по Z, пока указатель спрятан</summary>
    public static string SettingsZoomSpotGroup => Get(nameof(SettingsZoomSpotGroup));

    /// <summary>На самом резком месте снимка</summary>
    public static string SettingsZoomSpotSharp => Get(nameof(SettingsZoomSpotSharp));

    /// <summary>На точке фокусировки камеры</summary>
    public static string SettingsZoomSpotAf => Get(nameof(SettingsZoomSpotAf));

    /// <summary>Вид по умолчанию</summary>
    public static string SettingsDefaultViewMode => Get(nameof(SettingsDefaultViewMode));

    /// <summary>Клавиатура</summary>
    public static string SettingsCategoryHotkeys => Get(nameof(SettingsCategoryHotkeys));

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

    /// <summary>ОС</summary>
    public static string ShellAppOs => Get(nameof(ShellAppOs));

    /// <summary>Вкл</summary>
    public static string SettingsColumnOn => Get(nameof(SettingsColumnOn));

    /// <summary>Пункт</summary>
    public static string SettingsShellColumnItem => Get(nameof(SettingsShellColumnItem));

    /// <summary>Программа</summary>
    public static string SettingsShellColumnApp => Get(nameof(SettingsShellColumnApp));

    /// <summary>Для чего</summary>
    public static string SettingsShellColumnScopes => Get(nameof(SettingsShellColumnScopes));

    /// <summary>Добавить программу или тип файла…</summary>
    public static string SettingsShellAdd => Get(nameof(SettingsShellAdd));

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

    /// <summary>Открывать папку, на которую перешли с клавиатуры</summary>
    public static string SettingsTreeKeyboardNavigates => Get(nameof(SettingsTreeKeyboardNavigates));

    /// <summary>Стрелки, PgUp, PgDn, Home, End. Как в Проводнике, только зажатая клавиша…</summary>
    public static string SettingsTreeKeyboardNavigatesHint => Get(nameof(SettingsTreeKeyboardNavigatesHint));

    /// <summary>Прокручивать вбок к длинному имени</summary>
    public static string SettingsTreeScrollsSideways => Get(nameof(SettingsTreeScrollsSideways));

    /// <summary>Имя папки шире панели — панель уезжает вправо, чтобы показать его цел</summary>
    public static string SettingsTreeScrollsSidewaysHint => Get(nameof(SettingsTreeScrollsSidewaysHint));

    /// <summary>Показывать скрытые файлы</summary>
    public static string PanelShowHidden => Get(nameof(PanelShowHidden));

    /// <summary>Показывать системные файлы</summary>
    public static string PanelShowSystem => Get(nameof(PanelShowSystem));

    /// <summary>Открывать папку, на которую перешли с клавиатуры</summary>
    public static string PanelTreeKeyboardNavigates => Get(nameof(PanelTreeKeyboardNavigates));

    /// <summary>Как в Проводнике. Выключено — клавиши только двигают курсор, открывает…</summary>
    public static string PanelTreeKeyboardNavigatesHint => Get(nameof(PanelTreeKeyboardNavigatesHint));

    /// <summary>Спрашивать при удалении в корзину</summary>
    public static string PanelConfirmRecycle => Get(nameof(PanelConfirmRecycle));

    /// <summary>Ctrl+Z возвращает из корзины в любом случае; Shift+Delete спрашивает в</summary>
    public static string PanelConfirmRecycleHint => Get(nameof(PanelConfirmRecycleHint));

    /// <summary>Сразу показывать изменения от других программ</summary>
    public static string PanelAutoRefresh => Get(nameof(PanelAutoRefresh));

    /// <summary>Клавиши</summary>
    public static string SettingsOwnColumnGesture => Get(nameof(SettingsOwnColumnGesture));

    /// <summary>Служебные папки и файлы в корне дисков</summary>
    public static string SettingsShowSystemRootFolders => Get(nameof(SettingsShowSystemRootFolders));

    /// <summary>Показывать в списке</summary>
    public static string SettingsShowGroup => Get(nameof(SettingsShowGroup));

    /// <summary>Документ.txt</summary>
    public static string SettingsPreviewName => Get(nameof(SettingsPreviewName));

    /// <summary>Фотография.jpg</summary>
    public static string SettingsPreviewName2 => Get(nameof(SettingsPreviewName2));

    /// <summary>14 КБ · Текстовый файл</summary>
    public static string SettingsPreviewMeta => Get(nameof(SettingsPreviewMeta));

    /// <summary>Сбросить настройки контекстного меню…</summary>
    public static string SettingsShellReset => Get(nameof(SettingsShellReset));

    /// <summary>Сбросить настройки контекстного меню?  Все пункты — других программ и основные…</summary>
    public static string SettingsShellResetConfirm => Get(nameof(SettingsShellResetConfirm));

    /// <summary>Папки и закладки</summary>
    public static string SettingsCategoryFolders => Get(nameof(SettingsCategoryFolders));

    /// <summary>Список файлов</summary>
    public static string SettingsCategoryList => Get(nameof(SettingsCategoryList));

    /// <summary>Вид</summary>
    public static string SettingsCategoryViews => Get(nameof(SettingsCategoryViews));

    /// <summary>Размеры</summary>
    public static string SettingsCategorySizes => Get(nameof(SettingsCategorySizes));

    /// <summary>Файловые операции</summary>
    public static string SettingsCategoryOperations => Get(nameof(SettingsCategoryOperations));

    /// <summary>Оценки</summary>
    public static string SettingsCategoryRatings => Get(nameof(SettingsCategoryRatings));

    /// <summary>Кэш и память</summary>
    public static string SettingsCategoryCache => Get(nameof(SettingsCategoryCache));

    /// <summary>Интерфейс</summary>
    public static string SettingsCategoryInterface => Get(nameof(SettingsCategoryInterface));

    /// <summary>Цветовая схема</summary>
    public static string SettingsThemeGroup => Get(nameof(SettingsThemeGroup));

    /// <summary>Как в Windows: {0}</summary>
    public static string SettingsThemeSystem => Get(nameof(SettingsThemeSystem));

    /// <summary>Светлая</summary>
    public static string SettingsThemeLight => Get(nameof(SettingsThemeLight));

    /// <summary>Тёмная</summary>
    public static string SettingsThemeDark => Get(nameof(SettingsThemeDark));

    /// <summary>Язык (Language)</summary>
    public static string SettingsLanguageGroup => Get(nameof(SettingsLanguageGroup));

    /// <summary>Как в Windows: {0}</summary>
    public static string SettingsLanguageSystem => Get(nameof(SettingsLanguageSystem));

    /// <summary>Русский</summary>
    public static string SettingsLanguageRussian => Get(nameof(SettingsLanguageRussian));

    /// <summary>English</summary>
    public static string SettingsLanguageEnglish => Get(nameof(SettingsLanguageEnglish));

    /// <summary>Применится после перезапуска Wander.</summary>
    public static string SettingsLanguageNote => Get(nameof(SettingsLanguageNote));

    /// <summary>При запуске</summary>
    public static string SettingsStartupGroup => Get(nameof(SettingsStartupGroup));

    /// <summary>Панель папок</summary>
    public static string SettingsTreeGroup => Get(nameof(SettingsTreeGroup));

    /// <summary>Стандартные закладки</summary>
    public static string SettingsBookmarksGroup => Get(nameof(SettingsBookmarksGroup));

    /// <summary>Скрытые и системные разом, как pagefile.sys; один системный атрибут ничего…</summary>
    public static string SettingsShowSystemHint => Get(nameof(SettingsShowSystemHint));

    /// <summary>Спутники</summary>
    public static string SettingsCompanionsGroup => Get(nameof(SettingsCompanionsGroup));

    /// <summary>Переименование, перенос и удаление уносят спутники — .meta, .xmp, .pp3…</summary>
    public static string SettingsCompanionsNote => Get(nameof(SettingsCompanionsNote));

    /// <summary>Отслеживание изменений от других программ</summary>
    public static string SettingsRefreshGroup => Get(nameof(SettingsRefreshGroup));

    /// <summary>Размер картинки, px</summary>
    public static string SettingsPictureSize => Get(nameof(SettingsPictureSize));

    /// <summary>Подтверждения</summary>
    public static string SettingsConfirmGroup => Get(nameof(SettingsConfirmGroup));

    /// <summary>Совпадения имён при копировании и перемещении</summary>
    public static string SettingsConflictsGroup => Get(nameof(SettingsConflictsGroup));

    /// <summary>Перетаскивание</summary>
    public static string SettingsDragGroup => Get(nameof(SettingsDragGroup));

    /// <summary>Выключено — меню не загружает расширения оболочки вовсе: быстрее, и чужой…</summary>
    public static string SettingsShellExtensionsSwitchHint => Get(nameof(SettingsShellExtensionsSwitchHint));

    /// <summary>Поиск по пункту, программе или расширению</summary>
    public static string SettingsShellFilter => Get(nameof(SettingsShellFilter));

    /// <summary>Расширение с точкой (.mp4) находит и пункты для всех файлов: в меню .mp4 они тоже есть.</summary>
    public static string SettingsShellFilterHint => Get(nameof(SettingsShellFilterHint));

    /// <summary>Справка по этой странице (F1)</summary>
    public static string SettingsGuide => Get(nameof(SettingsGuide));

    /// <summary>Очистить</summary>
    public static string FieldClear => Get(nameof(FieldClear));

    /// <summary>Поиск по расширению</summary>
    public static string PickerTypeFilter => Get(nameof(PickerTypeFilter));

    /// <summary>Кэш миниатюр на диске</summary>
    public static string SettingsThumbnailCacheGroup => Get(nameof(SettingsThumbnailCacheGroup));

    /// <summary>16…8192 МБ, стандартно 256; сверх предела уходят давно не открывавшиеся.</summary>
    public static string SettingsThumbnailDiskMbHint => Get(nameof(SettingsThumbnailDiskMbHint));

    /// <summary>Память</summary>
    public static string SettingsMemoryGroup => Get(nameof(SettingsMemoryGroup));

    /// <summary>128…65536 МБ или 0, стандартно 0.</summary>
    public static string SettingsPictureMemoryLimits => Get(nameof(SettingsPictureMemoryLimits));

    /// <summary>Временные копии</summary>
    public static string SettingsTempGroup => Get(nameof(SettingsTempGroup));

    /// <summary>Копии файлов из архивов — для просмотра и «Открыть». Снято — в папке данных…</summary>
    public static string SettingsUseSystemTempNote => Get(nameof(SettingsUseSystemTempNote));

    /// <summary>Загрузка значков</summary>
    public static string SettingsLoadingGroup => Get(nameof(SettingsLoadingGroup));

    /// <summary>Выключено — вместо путей метки: диск, глубина и расширение видны, имена нет.…</summary>
    public static string SettingsLogPathsNote => Get(nameof(SettingsLogPathsNote));

    /// <summary>Меню «Отладка»</summary>
    public static string SettingsDebugMenuGroup => Get(nameof(SettingsDebugMenuGroup));
}
