using System.Globalization;
using System.Resources;

namespace Wander.App.Resources;

/// <summary>
/// Типизированный доступ к <c>Resources/Strings.resx</c> — единственному
/// месту, где лежит пользовательский текст Wander.
///
/// <para>
/// Написан руками, а не сгенерирован: markup-компилятор WPF собирает XAML
/// во временном проекте, куда сгенерированный designer-файл не попадает, и
/// <c>{x:Static}</c> на него не разрешается. Двадцать строк ручного кода
/// дешевле, чем борьба со сборкой.
/// </para>
///
/// <para>
/// Добавить язык: положить рядом <c>Strings.&lt;culture&gt;.resx</c> с теми же
/// ключами. <see cref="ResourceManager"/> выберет его по
/// <see cref="CultureInfo.CurrentUICulture"/> сам. Сейчас язык один —
/// русский, переключателя в интерфейсе нет намеренно.
/// </para>
///
/// <para>
/// Новая строка — это строка в resx и строка здесь. Ключ не найден —
/// возвращается сам ключ: пропажу видно в интерфейсе, а не в исключении
/// посреди отрисовки.
/// </para>
/// </summary>
public static class Strings {
    private static readonly ResourceManager _resources =
        new("Wander.App.Resources.Strings", typeof(Strings).Assembly);


    public static string Get(string key) {
        return _resources.GetString(key, CultureInfo.CurrentUICulture) ?? key;
    }

    /// <summary>Выберите файл для просмотра</summary>
    public static string PreviewSelectFile => Get(nameof(PreviewSelectFile));

    /// <summary>Для этого файла просмотр недоступен</summary>
    public static string PreviewUnsupported => Get(nameof(PreviewUnsupported));

    /// <summary>Считаем…</summary>
    public static string PreviewCounting => Get(nameof(PreviewCounting));

    /// <summary>{0} файлов · {1} папок · {2}</summary>
    public static string PreviewFolderHeadline => Get(nameof(PreviewFolderHeadline));

    /// <summary>Папка слишком велика — показано не всё, числа снизу неполные</summary>
    public static string PreviewFolderTruncated => Get(nameof(PreviewFolderTruncated));

    /// <summary>{0} шт.</summary>
    public static string PreviewFolderTypeCount => Get(nameof(PreviewFolderTypeCount));

    /// <summary>Что внутри</summary>
    public static string PreviewFolderTypes => Get(nameof(PreviewFolderTypes));

    /// <summary>Папка пуста</summary>
    public static string PreviewFolderEmpty => Get(nameof(PreviewFolderEmpty));

    /// <summary>Вместе с файлом:</summary>
    public static string PreviewIntegrated => Get(nameof(PreviewIntegrated));

    /// <summary>Скопировать GUID</summary>
    public static string PreviewCopyGuid => Get(nameof(PreviewCopyGuid));

    /// <summary>Полный кадр RAW</summary>
    public static string PreviewRawToggle => Get(nameof(PreviewRawToggle));

    /// <summary>Показать полный кадр RAW вместо встроенного превью …</summary>
    public static string PreviewRawToggleHint => Get(nameof(PreviewRawToggleHint));

    /// <summary>Оценка</summary>
    public static string PreviewRating => Get(nameof(PreviewRating));

    /// <summary>Показано начало книги — дальше не разбиралось</summary>
    public static string PreviewBookTruncated => Get(nameof(PreviewBookTruncated));

    /// <summary>Ярлык на:</summary>
    public static string PreviewLinkTarget => Get(nameof(PreviewLinkTarget));

    /// <summary>Перейти к оригиналу</summary>
    public static string PreviewGoToTarget => Get(nameof(PreviewGoToTarget));

    /// <summary>Открыть папку с оригиналом и выделить его</summary>
    public static string PreviewGoToTargetHint => Get(nameof(PreviewGoToTargetHint));

    /// <summary>Оригинал не найден — ярлык ведёт в никуда</summary>
    public static string PreviewLinkBroken => Get(nameof(PreviewLinkBroken));

    /// <summary>Занято {0} из {1}</summary>
    public static string PreviewVolumeUsage => Get(nameof(PreviewVolumeUsage));

    /// <summary>Свободно {0}</summary>
    public static string PreviewVolumeFree => Get(nameof(PreviewVolumeFree));

    /// <summary>Устройство не готово</summary>
    public static string PreviewVolumeNotReady => Get(nameof(PreviewVolumeNotReady));

    /// <summary>Локальный диск</summary>
    public static string VolumeKindFixed => Get(nameof(VolumeKindFixed));

    /// <summary>Съёмный диск</summary>
    public static string VolumeKindRemovable => Get(nameof(VolumeKindRemovable));

    /// <summary>Сетевой диск</summary>
    public static string VolumeKindNetwork => Get(nameof(VolumeKindNetwork));

    /// <summary>Оптический привод</summary>
    public static string VolumeKindOptical => Get(nameof(VolumeKindOptical));

    /// <summary>Диск в памяти</summary>
    public static string VolumeKindRam => Get(nameof(VolumeKindRam));

    /// <summary>Диск</summary>
    public static string VolumeKindUnknown => Get(nameof(VolumeKindUnknown));

    /// <summary>Удалён</summary>
    public static string SummaryDeleted => Get(nameof(SummaryDeleted));

    /// <summary>Откуда</summary>
    public static string SummaryDeletedFrom => Get(nameof(SummaryDeletedFrom));

    /// <summary>Изменён</summary>
    public static string SummaryModified => Get(nameof(SummaryModified));

    /// <summary>Размер</summary>
    public static string SummarySize => Get(nameof(SummarySize));

    /// <summary>Выбрано: {0} — считаем…</summary>
    public static string SummarySelectedCounting => Get(nameof(SummarySelectedCounting));

    /// <summary>Выбрано: {0} — внутри {1} файлов, {2}</summary>
    public static string SummarySelected => Get(nameof(SummarySelected));

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

    /// <summary>Создать папку</summary>
    public static string MenuNewFolder => Get(nameof(MenuNewFolder));

    /// <summary>Область просмотра</summary>
    public static string MenuQuickPreview => Get(nameof(MenuQuickPreview));

    /// <summary>Настройки...</summary>
    public static string MenuOptions => Get(nameof(MenuOptions));

    /// <summary>Сообщить о проблеме или идее...</summary>
    public static string MenuReportIssue => Get(nameof(MenuReportIssue));

    /// <summary>Откроет в браузере новую задачу на GitHub — баг или пожел…</summary>
    public static string MenuReportIssueHint => Get(nameof(MenuReportIssueHint));

    /// <summary>Отладка</summary>
    public static string MenuDebug => Get(nameof(MenuDebug));

    /// <summary>Журнал</summary>
    public static string MenuLogs => Get(nameof(MenuLogs));

    /// <summary>Открыть файл журнала текущего сеанса</summary>
    public static string MenuLogsHint => Get(nameof(MenuLogsHint));

    /// <summary>Выход</summary>
    public static string MenuExit => Get(nameof(MenuExit));

    /// <summary>Фильтр: имя, либо имя:текст (Ctrl+F). Окно поиска — Ctrl+Shift+F. Esc — сбросить</summary>
    public static string SearchHint => Get(nameof(SearchHint));

    /// <summary>Фильтр: имя или имя:текст</summary>
    public static string SearchPlaceholder => Get(nameof(SearchPlaceholder));

    /// <summary>Недавние папки (F4)</summary>
    public static string RecentFoldersHint => Get(nameof(RecentFoldersHint));

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

    /// <summary>Путь не найден: {0}</summary>
    public static string StatusPathNotFound => Get(nameof(StatusPathNotFound));

    /// <summary>Не удалось открыть: {0}</summary>
    public static string StatusOpenFailed => Get(nameof(StatusOpenFailed));

    /// <summary>Некуда перетаскивать: целевая папка не определена.</summary>
    public static string StatusNoDropTarget => Get(nameof(StatusNoDropTarget));

    /// <summary>Перетаскивание не удалось: {0}</summary>
    public static string StatusDropFailed => Get(nameof(StatusDropFailed));

    /// <summary>Ярлыки на этой платформе не поддерживаются.</summary>
    public static string StatusShortcutsUnsupported => Get(nameof(StatusShortcutsUnsupported));

    /// <summary>Не удалось создать ярлык для «{0}»: {1}</summary>
    public static string StatusShortcutFailed => Get(nameof(StatusShortcutFailed));

    /// <summary>Создано ярлыков: {0} в {1}</summary>
    public static string StatusShortcutsCreated => Get(nameof(StatusShortcutsCreated));

    /// <summary>Не удалось открыть браузер: {0}</summary>
    public static string StatusBrowserFailed => Get(nameof(StatusBrowserFailed));

    /// <summary>Журнал не настроен.</summary>
    public static string StatusNoLogging => Get(nameof(StatusNoLogging));

    /// <summary>Файл журнала не найден.</summary>
    public static string StatusNoLogFile => Get(nameof(StatusNoLogFile));

    /// <summary>Не удалось открыть журнал: {0}</summary>
    public static string StatusOpenLogFailed => Get(nameof(StatusOpenLogFailed));

    /// <summary>Не удалось показать свойства: {0}</summary>
    public static string StatusPropertiesFailed => Get(nameof(StatusPropertiesFailed));

    /// <summary>Не удалось открыть с помощью: {0}</summary>
    public static string StatusOpenWithFailed => Get(nameof(StatusOpenWithFailed));

    /// <summary>Не удалось открыть терминал: {0}</summary>
    public static string StatusTerminalFailed => Get(nameof(StatusTerminalFailed));

    /// <summary>Скопировано в буфер обмена: {0}</summary>
    public static string StatusCopiedToClipboard => Get(nameof(StatusCopiedToClipboard));

    /// <summary>Буфер обмена занят: {0}</summary>
    public static string StatusClipboardBusy => Get(nameof(StatusClipboardBusy));

    /// <summary>Удаление не удалось: {0}</summary>
    public static string StatusDeleteFailed => Get(nameof(StatusDeleteFailed));

    /// <summary>Отменено: {0}</summary>
    public static string StatusUndone => Get(nameof(StatusUndone));

    /// <summary>Не удалось отменить: {0}</summary>
    public static string StatusUndoFailed => Get(nameof(StatusUndoFailed));

    /// <summary>Переименовано вместе со спутниками: {0}</summary>
    public static string StatusRenamedWithCompanions => Get(nameof(StatusRenamedWithCompanions));

    /// <summary>Переименование не удалось: {0}</summary>
    public static string StatusRenameFailed => Get(nameof(StatusRenameFailed));

    /// <summary>Скопировано элементов: {0}</summary>
    public static string StatusCopied => Get(nameof(StatusCopied));

    /// <summary>Вырезано элементов: {0}</summary>
    public static string StatusCut => Get(nameof(StatusCut));

    /// <summary>Вставка не удалась: {0}</summary>
    public static string StatusPasteFailed => Get(nameof(StatusPasteFailed));

    /// <summary>Не удалось создать: {0}</summary>
    public static string StatusCreateFailed => Get(nameof(StatusCreateFailed));

    /// <summary>Ошибка: {0}</summary>
    public static string StatusError => Get(nameof(StatusError));

    /// <summary>Операция отменена.</summary>
    public static string StatusCancelled => Get(nameof(StatusCancelled));

    /// <summary>Папка уже в закладках.</summary>
    public static string StatusAlreadyBookmarked => Get(nameof(StatusAlreadyBookmarked));

    /// <summary>Добавлено в закладки: {0}</summary>
    public static string StatusBookmarkAdded => Get(nameof(StatusBookmarkAdded));

    /// <summary>Элементов: {0}</summary>
    public static string StatusItems => Get(nameof(StatusItems));

    /// <summary>Элементов: {0} (скрыто: {1})</summary>
    public static string StatusItemsWithHidden => Get(nameof(StatusItemsWithHidden));

    /// <summary>Подходит {0} из {1} по запросу «{2}»</summary>
    public static string StatusFilterMatches => Get(nameof(StatusFilterMatches));

    /// <summary>Удалено безвозвратно: {0}</summary>
    public static string StatusDeleted => Get(nameof(StatusDeleted));

    /// <summary>Удалено безвозвратно: {0}, с ошибкой: {1}{2}</summary>
    public static string StatusDeletedPartly => Get(nameof(StatusDeletedPartly));

    /// <summary>В корзину отправлено: {0}</summary>
    public static string StatusRecycled => Get(nameof(StatusRecycled));

    /// <summary>В корзину отправлено: {0}, с ошибкой: {1}{2}</summary>
    public static string StatusRecycledPartly => Get(nameof(StatusRecycledPartly));

    /// <summary>{0} {1} элем. в {2}</summary>
    public static string StatusBatchDone => Get(nameof(StatusBatchDone));

    /// <summary>пропущено {0}</summary>
    public static string StatusBatchSkipped => Get(nameof(StatusBatchSkipped));

    /// <summary>отменено {0}</summary>
    public static string StatusBatchCancelled => Get(nameof(StatusBatchCancelled));

    /// <summary>с ошибкой {0}{1}</summary>
    public static string StatusBatchFailed => Get(nameof(StatusBatchFailed));

    /// <summary>Восстановлено: {0}</summary>
    public static string StatusRestored => Get(nameof(StatusRestored));

    /// <summary>Восстановлено: {0}, не удалось: {1} (например, «{2}»)</summary>
    public static string StatusRestoredPartly => Get(nameof(StatusRestoredPartly));

    /// <summary>Скопировано</summary>
    public static string VerbCopied => Get(nameof(VerbCopied));

    /// <summary>Перемещено</summary>
    public static string VerbMoved => Get(nameof(VerbMoved));

    /// <summary>Новая папка</summary>
    public static string NewFolderName => Get(nameof(NewFolderName));

    /// <summary>файл</summary>
    public static string KindFile => Get(nameof(KindFile));

    /// <summary>папку</summary>
    public static string KindFolder => Get(nameof(KindFolder));

    /// <summary>… и ещё {0}</summary>
    public static string AndMore => Get(nameof(AndMore));

    /// <summary>Удалить безвозвратно?</summary>
    public static string ConfirmDeleteTitle => Get(nameof(ConfirmDeleteTitle));

    /// <summary>Отправить в корзину?</summary>
    public static string ConfirmRecycleTitle => Get(nameof(ConfirmRecycleTitle));

    /// <summary>Удалить безвозвратно {0} «{1}»?  {2}</summary>
    public static string ConfirmDeleteOne => Get(nameof(ConfirmDeleteOne));

    /// <summary>Отправить в корзину {0} «{1}»?  {2}</summary>
    public static string ConfirmRecycleOne => Get(nameof(ConfirmRecycleOne));

    /// <summary>Удалить безвозвратно {0} элем.?  {1}</summary>
    public static string ConfirmDeleteMany => Get(nameof(ConfirmDeleteMany));

    /// <summary>Отправить в корзину {0} элем.?  {1}</summary>
    public static string ConfirmRecycleMany => Get(nameof(ConfirmRecycleMany));

    /// <summary>Вместе с ними уедут файлы-спутники: {0}.</summary>
    public static string ConfirmWithCompanions => Get(nameof(ConfirmWithCompanions));

    /// <summary>Это действие нельзя отменить.</summary>
    public static string ConfirmIrreversible => Get(nameof(ConfirmIrreversible));

    /// <summary>Только для чтения</summary>
    public static string ConfirmReadOnlyTitle => Get(nameof(ConfirmReadOnlyTitle));

    /// <summary>Элемент помечен «только для чтения»:  {0}  Всё равно удал…</summary>
    public static string ConfirmReadOnlyOne => Get(nameof(ConfirmReadOnlyOne));

    /// <summary>Эти элементы помечены «только для чтения»:  {0}  Всё равн…</summary>
    public static string ConfirmReadOnlyMany => Get(nameof(ConfirmReadOnlyMany));

    /// <summary>Нельзя вставить</summary>
    public static string CannotPasteTitle => Get(nameof(CannotPasteTitle));

    /// <summary>Переместить?</summary>
    public static string ConfirmMoveTitle => Get(nameof(ConfirmMoveTitle));

    /// <summary>Переместить элемент?  Откуда: {0} Куда:   {1}</summary>
    public static string ConfirmMoveOne => Get(nameof(ConfirmMoveOne));

    /// <summary>Переместить {0} элем. в: {1}?</summary>
    public static string ConfirmMoveMany => Get(nameof(ConfirmMoveMany));

    /// <summary>Заменить или пропустить</summary>
    public static string ConflictTitle => Get(nameof(ConflictTitle));

    /// <summary>Здесь уже есть файл с именем «{0}».</summary>
    public static string ConflictHeader => Get(nameof(ConflictHeader));

    /// <summary>Что копируем</summary>
    public static string ConflictSource => Get(nameof(ConflictSource));

    /// <summary>Что уже на месте</summary>
    public static string ConflictTarget => Get(nameof(ConflictTarget));

    /// <summary>Заменить</summary>
    public static string ConflictReplace => Get(nameof(ConflictReplace));

    /// <summary>Пропустить</summary>
    public static string ConflictSkip => Get(nameof(ConflictSkip));

    /// <summary>Оставить оба</summary>
    public static string ConflictKeepBoth => Get(nameof(ConflictKeepBoth));

    /// <summary>Сохранить копию с номером в имени, например «имя (1).txt»</summary>
    public static string ConflictKeepBothHint => Get(nameof(ConflictKeepBothHint));

    /// <summary>Совпадения имён</summary>
    public static string BatchConflictTitle => Get(nameof(BatchConflictTitle));

    /// <summary>Совпадений: {0}</summary>
    public static string BatchConflictHeader => Get(nameof(BatchConflictHeader));

    /// <summary>Что сделать с файлами, которые уже есть на месте:</summary>
    public static string BatchConflictPrompt => Get(nameof(BatchConflictPrompt));

    /// <summary>Заменить все</summary>
    public static string BatchReplaceAll => Get(nameof(BatchReplaceAll));

    /// <summary>Пропустить все</summary>
    public static string BatchSkipAll => Get(nameof(BatchSkipAll));

    /// <summary>Решать по каждому</summary>
    public static string BatchResolveEach => Get(nameof(BatchResolveEach));

    /// <summary>Папка</summary>
    public static string KindFolderNoun => Get(nameof(KindFolderNoun));

    /// <summary>ОК</summary>
    public static string ActionOk => Get(nameof(ActionOk));

    /// <summary>Отмена</summary>
    public static string ActionCancel => Get(nameof(ActionCancel));

    /// <summary>Переименовать</summary>
    public static string RenameTitle => Get(nameof(RenameTitle));

    /// <summary>Новое имя:</summary>
    public static string RenamePrompt => Get(nameof(RenamePrompt));

    /// <summary>В имени файла нельзя использовать символы: </summary>
    public static string InvalidFileNameChars => Get(nameof(InvalidFileNameChars));

    /// <summary>Переместить</summary>
    public static string DragMove => Get(nameof(DragMove));

    /// <summary>Копировать</summary>
    public static string DragCopy => Get(nameof(DragCopy));

    /// <summary>Создать ярлык на</summary>
    public static string DragLink => Get(nameof(DragLink));

    /// <summary>{0} элем.</summary>
    public static string DragItems => Get(nameof(DragItems));

    /// <summary>в {0}</summary>
    public static string DragTarget => Get(nameof(DragTarget));

    /// <summary>Добавить {0} в закладки</summary>
    public static string DragAddToBookmarks => Get(nameof(DragAddToBookmarks));

    /// <summary>Wander — отчёт об ошибке</summary>
    public static string CrashTitle => Get(nameof(CrashTitle));

    /// <summary>Wander аварийно завершается.</summary>
    public static string CrashFatal => Get(nameof(CrashFatal));

    /// <summary>В Wander произошла неожиданная ошибка.</summary>
    public static string CrashNonFatal => Get(nameof(CrashNonFatal));

    /// <summary>В Wander произошла неожиданная ошибка.  {0}: {1}  Подгото…</summary>
    public static string CrashFallbackPrompt => Get(nameof(CrashFallbackPrompt));

    /// <summary>Wander может собрать архив с отчётом (zip), показать его …</summary>
    public static string CrashExplain => Get(nameof(CrashExplain));

    /// <summary>Приложить журнал этого сеанса — с ним разбираться гораздо…</summary>
    public static string CrashIncludeLog => Get(nameof(CrashIncludeLog));

    /// <summary>Подготовить отчёт</summary>
    public static string CrashPrepare => Get(nameof(CrashPrepare));

    /// <summary>Закрыть</summary>
    public static string CrashClose => Get(nameof(CrashClose));

    /// <summary>файл открыт в: {0}</summary>
    public static string ErrorFileInUse => Get(nameof(ErrorFileInUse));

    /// <summary>Открыть с помощью</summary>
    public static string MenuCmdOpenSubmenu => Get(nameof(MenuCmdOpenSubmenu));

    /// <summary>Файл</summary>
    public static string MenuCmdFileSubmenu => Get(nameof(MenuCmdFileSubmenu));

    /// <summary>Вид</summary>
    public static string MenuCmdViewSubmenu => Get(nameof(MenuCmdViewSubmenu));

    /// <summary>Сортировка</summary>
    public static string MenuCmdSortSubmenu => Get(nameof(MenuCmdSortSubmenu));

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

    /// <summary>Создать папку</summary>
    public static string MenuCmdNewFolder => Get(nameof(MenuCmdNewFolder));

    /// <summary>Таблица</summary>
    public static string MenuCmdViewDetails => Get(nameof(MenuCmdViewDetails));

    /// <summary>Плитка</summary>
    public static string MenuCmdViewTiles => Get(nameof(MenuCmdViewTiles));

    /// <summary>Крупные значки</summary>
    public static string MenuCmdViewLargeIcons => Get(nameof(MenuCmdViewLargeIcons));

    /// <summary>Область просмотра</summary>
    public static string MenuCmdTogglePreview => Get(nameof(MenuCmdTogglePreview));

    /// <summary>Имя</summary>
    public static string MenuCmdSortByName => Get(nameof(MenuCmdSortByName));

    /// <summary>Дата изменения</summary>
    public static string MenuCmdSortByDate => Get(nameof(MenuCmdSortByDate));

    /// <summary>Размер</summary>
    public static string MenuCmdSortBySize => Get(nameof(MenuCmdSortBySize));

    /// <summary>Тип</summary>
    public static string MenuCmdSortByType => Get(nameof(MenuCmdSortByType));

    /// <summary>По возрастанию</summary>
    public static string MenuCmdSortAscending => Get(nameof(MenuCmdSortAscending));

    /// <summary>Папки сверху</summary>
    public static string MenuCmdSortFoldersFirst => Get(nameof(MenuCmdSortFoldersFirst));

    /// <summary>Восстановить</summary>
    public static string MenuCmdRestore => Get(nameof(MenuCmdRestore));

    /// <summary>Обновить</summary>
    public static string MenuCmdRefresh => Get(nameof(MenuCmdRefresh));

    /// <summary>Отменить</summary>
    public static string MenuCmdUndo => Get(nameof(MenuCmdUndo));

    /// <summary>Свойства</summary>
    public static string MenuCmdProperties => Get(nameof(MenuCmdProperties));

    /// <summary>это</summary>
    public static string DropThis => Get(nameof(DropThis));

    /// <summary>Нельзя переместить «{0}» сам в себя</summary>
    public static string DropOntoItself => Get(nameof(DropOntoItself));

    /// <summary>«{0}» уже лежит в «{1}»</summary>
    public static string DropAlreadyThere => Get(nameof(DropAlreadyThere));

    /// <summary>Нельзя переместить «{0}» в собственную подпапку «{1}»</summary>
    public static string DropIntoOwnSubfolder => Get(nameof(DropIntoOwnSubfolder));

    /// <summary>Сюда перетащить нельзя</summary>
    public static string DropNotAllowed => Get(nameof(DropNotAllowed));

    /// <summary>Настройки Wander</summary>
    public static string SettingsTitle => Get(nameof(SettingsTitle));

    /// <summary>Применить</summary>
    public static string ActionApply => Get(nameof(ActionApply));

    /// <summary>Основное</summary>
    public static string SettingsCategoryGeneral => Get(nameof(SettingsCategoryGeneral));

    /// <summary>Безопасность</summary>
    public static string SettingsCategorySafety => Get(nameof(SettingsCategorySafety));

    /// <summary>Файловые операции</summary>
    public static string SettingsCategoryFileOps => Get(nameof(SettingsCategoryFileOps));

    /// <summary>Интегрированные элементы</summary>
    public static string SettingsCategoryCompanions => Get(nameof(SettingsCategoryCompanions));

    /// <summary>Вёрстка</summary>
    public static string SettingsCategoryLayout => Get(nameof(SettingsCategoryLayout));

    /// <summary>Миниатюры</summary>
    public static string SettingsCategoryThumbnails => Get(nameof(SettingsCategoryThumbnails));

    /// <summary>Закладки</summary>
    public static string SettingsCategoryBookmarks => Get(nameof(SettingsCategoryBookmarks));

    /// <summary>Контекстное меню</summary>
    public static string SettingsCategoryContextMenu => Get(nameof(SettingsCategoryContextMenu));

    /// <summary>Отладка</summary>
    public static string SettingsCategoryDebug => Get(nameof(SettingsCategoryDebug));

    /// <summary>Восстанавливать последнюю папку при запуске</summary>
    public static string SettingsRestoreLastFolder => Get(nameof(SettingsRestoreLastFolder));

    /// <summary>Когда выключено, Wander открывает первый доступный диск в…</summary>
    public static string SettingsRestoreLastFolderHint => Get(nameof(SettingsRestoreLastFolderHint));

    /// <summary>Показывать скрытые файлы и папки</summary>
    public static string SettingsShowHidden => Get(nameof(SettingsShowHidden));

    /// <summary>Показывать защищённые системные файлы</summary>
    public static string SettingsShowSystem => Get(nameof(SettingsShowSystem));

    /// <summary>Не показывать системные папки в корне дисков</summary>
    public static string SettingsHideSystemRootFolders => Get(nameof(SettingsHideSystemRootFolders));

    /// <summary>$RECYCLE.BIN, System Volume Information, Recovery, …</summary>
    public static string SettingsHideSystemRootFoldersHint => Get(nameof(SettingsHideSystemRootFoldersHint));

    /// <summary>Изменения применяются мгновенно: список файлов в текущей …</summary>
    public static string SettingsHiddenHint => Get(nameof(SettingsHiddenHint));

    /// <summary>Спрашивать подтверждение при удалении в корзину</summary>
    public static string SettingsConfirmRecycle => Get(nameof(SettingsConfirmRecycle));

    /// <summary>Когда выключено, Delete отправляет элементы в корзину сра…</summary>
    public static string SettingsConfirmRecycleHint => Get(nameof(SettingsConfirmRecycleHint));

    /// <summary>Показывать файл вместе со спутниками как один элемент</summary>
    public static string SettingsIntegrateCompanions => Get(nameof(SettingsIntegrateCompanions));

    /// <summary>Спутник — служебный файл рядом с основным: Unity-шный «.m…</summary>
    public static string SettingsCompanionsHint => Get(nameof(SettingsCompanionsHint));

    /// <summary>Когда выключено — обычное поведение: каждый файл сам по с…</summary>
    public static string SettingsCompanionsOffHint => Get(nameof(SettingsCompanionsOffHint));

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

    /// <summary>Значения зажимаются в разумные пределы автоматически. Ста…</summary>
    public static string SettingsLayoutHint => Get(nameof(SettingsLayoutHint));

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

    /// <summary>Показывать «Загрузки»</summary>
    public static string SettingsBookmarkDownloads => Get(nameof(SettingsBookmarkDownloads));

    /// <summary>Показывать «Документы»</summary>
    public static string SettingsBookmarkDocuments => Get(nameof(SettingsBookmarkDocuments));

    /// <summary>Показывать «Изображения»</summary>
    public static string SettingsBookmarkPictures => Get(nameof(SettingsBookmarkPictures));

    /// <summary>Показывать «Корзину»</summary>
    public static string SettingsBookmarkRecycleBin => Get(nameof(SettingsBookmarkRecycleBin));

    /// <summary>Спец-папки появляются в верхней панели слева. Любую папку…</summary>
    public static string SettingsBookmarksHint => Get(nameof(SettingsBookmarksHint));

    /// <summary>Показывать пункты сторонних приложений (7-Zip, TortoiseGit…)</summary>
    public static string SettingsShellExtensions => Get(nameof(SettingsShellExtensions));

    /// <summary>Приложения</summary>
    public static string SettingsShellExtensionsGroup => Get(nameof(SettingsShellExtensionsGroup));

    /// <summary>Список наполняется по мере работы: Wander узнаёт про расш…</summary>
    public static string SettingsShellExtensionsHint => Get(nameof(SettingsShellExtensionsHint));

    /// <summary>Пункты Wander</summary>
    public static string SettingsOwnItemsGroup => Get(nameof(SettingsOwnItemsGroup));

    /// <summary>Снятая галочка убирает пункт из меню; хоткей продолжает р…</summary>
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

    /// <summary>Закладки</summary>
    public static string BookmarksHeader => Get(nameof(BookmarksHeader));

    /// <summary>Свернуть / развернуть закладки</summary>
    public static string BookmarksToggleHint => Get(nameof(BookmarksToggleHint));

    /// <summary>Убрать из закладок</summary>
    public static string BookmarksRemove => Get(nameof(BookmarksRemove));

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

    /// <summary>Копирование</summary>
    public static string ProgressCopying => Get(nameof(ProgressCopying));

    /// <summary>Перемещение</summary>
    public static string ProgressMoving => Get(nameof(ProgressMoving));

    /// <summary>Удаление</summary>
    public static string ProgressDeleting => Get(nameof(ProgressDeleting));

    /// <summary>В корзину</summary>
    public static string ProgressRecycling => Get(nameof(ProgressRecycling));

    /// <summary>Отмена…</summary>
    public static string ProgressCancelling => Get(nameof(ProgressCancelling));

    /// <summary>Воспроизведение недоступно</summary>
    public static string PreviewVideoUnavailable => Get(nameof(PreviewVideoUnavailable));

    /// <summary>«{0}»</summary>
    public static string DragOneItem => Get(nameof(DragOneItem));

    /// <summary>Воспроизведение / пауза (Пробел)</summary>
    public static string PreviewPlayPause => Get(nameof(PreviewPlayPause));

    /// <summary>В буфере обмена файл, которого нет на диске (вложение письма, файл внутри архива) — вставить его Wander не может</summary>
    public static string StatusClipboardVirtualFiles => Get(nameof(StatusClipboardVirtualFiles));

    /// <summary>Скопировано внутри Wander: системный буфер обмена сейчас занят другим приложением</summary>
    public static string StatusClipboardNotShared => Get(nameof(StatusClipboardNotShared));
    /// <summary>Выбрано: {0} — {1}</summary>
    public static string StatusSelection => Get(nameof(StatusSelection));

    /// <summary> (папок: {0}, размер не считается)</summary>
    public static string StatusSelectionFolders => Get(nameof(StatusSelectionFolders));
    /// <summary>Обновлять список автоматически</summary>
    public static string SettingsAutoRefresh => Get(nameof(SettingsAutoRefresh));

    /// <summary>Wander следит за открытой папкой и перерисовывает список, когда файлы в ней меняет кто-то другой — программа, загрузка, Проводник. Когда выключено, список обновляется только по F5.</summary>
    public static string SettingsAutoRefreshHint => Get(nameof(SettingsAutoRefreshHint));

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
    /// <summary>{0}: {1} px · Ctrl + нажать колёсико — вернуть {2} px</summary>
    public static string StatusViewSize => Get(nameof(StatusViewSize));

    /// <summary>{0}: {1} px (стандартный)</summary>
    public static string StatusViewSizeDefault => Get(nameof(StatusViewSizeDefault));

    /// <summary>Галерея</summary>
    public static string MenuViewGallery => Get(nameof(MenuViewGallery));

    /// <summary>Галерея</summary>
    public static string MenuCmdViewGallery => Get(nameof(MenuCmdViewGallery));

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

    /// <summary>Оценка</summary>
    public static string FilterRatingLabel => Get(nameof(FilterRatingLabel));


    /// <summary>Показывать только снимки с этой цветовой меткой. Повторный щелчок снимает фильтр.</summary>
    public static string FilterColorHint => Get(nameof(FilterColorHint));

    /// <summary>Сбросить фильтр</summary>
    public static string FilterClear => Get(nameof(FilterClear));

    /// <summary>Подходит {0} из {1} по фильтру оценок</summary>
    public static string StatusRatingFilterMatches => Get(nameof(StatusRatingFilterMatches));

    /// <summary>Создать файл оценки?</summary>
    public static string ConfirmCreateSidecarTitle => Get(nameof(ConfirmCreateSidecarTitle));

    /// <summary>У снимка «{1}» нет файла с оценкой. Рядом будет создан «{0}» — в нём и сохранится оценка.  Отменить создание можно через Ctrl+Z.</summary>
    public static string ConfirmCreateSidecar => Get(nameof(ConfirmCreateSidecar));

    /// <summary>Внимание: RawTherapee применяет профиль по-умолчанию (Auto-Matched Curve) только к снимкам без .pp3. Как только .pp3 появится, снимок начнёт открываться в RawTherapee с нейтральных значений, а не с автоподбора. Формат меняется в настройках, раздел «Галерея».</summary>
    public static string ConfirmCreateSidecarPp3Warning => Get(nameof(ConfirmCreateSidecarPp3Warning));


    /// <summary>Оценки пока нет. Щелчок по звезде создаст файл рядом со снимком — с подтверждением.</summary>
    public static string PreviewRatingUnsaved => Get(nameof(PreviewRatingUnsaved));

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

    /// <summary>Формат оценки, если сайдкара нет</summary>
    public static string SettingsRawRatingFormat => Get(nameof(SettingsRawRatingFormat));

    /// <summary>.xmp — Adobe, darktable, RawTherapee 5.11+</summary>
    public static string SettingsRawRatingFormatXmp => Get(nameof(SettingsRawRatingFormatXmp));

    /// <summary>.pp3 — RawTherapee</summary>
    public static string SettingsRawRatingFormatPp3 => Get(nameof(SettingsRawRatingFormatPp3));

    /// <summary>Wander не создаёт файлы, о которых не просили: первая оценка снимка без сайдкара спрашивает подтверждение. Формат по-умолчанию — .xmp, потому что он ни на что, кроме оценки, не влияет. Появившийся .pp3 отменяет применение профиля по-умолчанию в RawTherapee.</summary>
    public static string SettingsRawRatingFormatHint => Get(nameof(SettingsRawRatingFormatHint));

    /// <summary>Поиск: найдено {0}, просмотрено {1}</summary>
    public static string StatusSearching => Get(nameof(StatusSearching));

    /// <summary>Найдено {0} по запросу «{1}» (просмотрено файлов: {2})</summary>
    public static string StatusSearchFound => Get(nameof(StatusSearchFound));

    /// <summary>Ничего не найдено по запросу «{0}»</summary>
    public static string StatusSearchNothing => Get(nameof(StatusSearchNothing));

    /// <summary>Поиск остановлен, найдено: {0}</summary>
    public static string StatusSearchStopped => Get(nameof(StatusSearchStopped));

    /// <summary> — показаны первые {0}</summary>
    public static string StatusSearchTruncated => Get(nameof(StatusSearchTruncated));

    /// <summary> — не удалось прочитать файлов: {0}</summary>
    public static string StatusSearchUnreadable => Get(nameof(StatusSearchUnreadable));

    /// <summary>Окно поиска (Ctrl+Shift+F)</summary>
    public static string SearchOptions => Get(nameof(SearchOptions));

    /// <summary>Искать в подпапках</summary>
    public static string SearchScopeSubfolders => Get(nameof(SearchScopeSubfolders));

    /// <summary>Остановить поиск</summary>
    public static string SearchStop => Get(nameof(SearchStop));

    /// <summary>Очистить поиск</summary>
    public static string SearchClear => Get(nameof(SearchClear));

    /// <summary>Папка</summary>
    public static string ColumnFolder => Get(nameof(ColumnFolder));

    /// <summary>Совпадение</summary>
    public static string ColumnMatch => Get(nameof(ColumnMatch));

    /// <summary>У {0} снимков нет файла с оценкой. Рядом с каждым будет создан файл «{1}» — в нём и сохранится оценка.  Отменить создание можно через Ctrl+Z, одним нажатием на всю пачку.</summary>
    public static string ConfirmCreateSidecarMany => Get(nameof(ConfirmCreateSidecarMany));



    /// <summary>Яркость серого фона (0–255)</summary>
    public static string SettingsGalleryGreyLevel => Get(nameof(SettingsGalleryGreyLevel));

    /// <summary>Яркость тёмного фона (0–255)</summary>
    public static string SettingsGalleryDarkLevel => Get(nameof(SettingsGalleryDarkLevel));

    /// <summary>Доля изображений для включения (%)</summary>
    public static string SettingsAutoGalleryPercent => Get(nameof(SettingsAutoGalleryPercent));

    /// <summary>Оценка {0} — файлов: {1}</summary>
    public static string StatusRatingApplied => Get(nameof(StatusRatingApplied));

    /// <summary>Оценка снята — файлов: {0}</summary>
    public static string StatusRatingCleared => Get(nameof(StatusRatingCleared));

    /// <summary>Показывать снимки с этой оценкой и выше. С Ctrl — добавить или убрать одну оценку. Повторный щелчок снимает фильтр.</summary>
    public static string FilterRatingHint => Get(nameof(FilterRatingHint));

    /// <summary>Показывать снимки без оценки. С Ctrl — добавить их к уже выбранным оценкам.</summary>
    public static string FilterUnratedHint => Get(nameof(FilterUnratedHint));

    /// <summary>«{0}» с текстом «{1}»</summary>
    public static string SearchDescriptionBoth => Get(nameof(SearchDescriptionBoth));

    /// <summary>тексту «{0}»</summary>
    public static string SearchDescriptionText => Get(nameof(SearchDescriptionText));

    /// <summary>Поиск</summary>
    public static string SearchWindowTitle => Get(nameof(SearchWindowTitle));

    /// <summary>Имя</summary>
    public static string SearchFieldName => Get(nameof(SearchFieldName));

    /// <summary>Часть имени, либо маска с * и ?: *.cs;*.xaml. Пусто — любое имя</summary>
    public static string SearchFieldNameHint => Get(nameof(SearchFieldNameHint));

    /// <summary>Текст</summary>
    public static string SearchFieldText => Get(nameof(SearchFieldText));

    /// <summary>Текст внутри файлов. Пусто — искать только по имени; оба поля заполнены — оба условия должны совпасть</summary>
    public static string SearchFieldTextHint => Get(nameof(SearchFieldTextHint));

    /// <summary>Искать и в двоичных файлах</summary>
    public static string SearchBinaries => Get(nameof(SearchBinaries));

    /// <summary>Побайтовый поиск в файлах, которые не являются текстом (exe, dll, ресурсы). Только латиница и цифры: в двоичном файле нечего декодировать, и угадывать кодировку было бы обманом</summary>
    public static string SearchBinariesHint => Get(nameof(SearchBinariesHint));

    /// <summary>Искать</summary>
    public static string SearchRun => Get(nameof(SearchRun));

    /// <summary>Папка</summary>
    public static string SearchFieldFolder => Get(nameof(SearchFieldFolder));
}
