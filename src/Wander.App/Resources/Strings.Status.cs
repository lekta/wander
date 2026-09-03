namespace Wander.App.Resources;

/// <summary>
/// Статусная строка, подтверждения и отчёты об ошибках.
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

    /// <summary>Уже в этой папке, вырезание снято</summary>
    public static string StatusCutAlreadyHere => Get(nameof(StatusCutAlreadyHere));

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

    /// <summary>Элементов: {0:N0}</summary>
    public static string StatusItems => Get(nameof(StatusItems));

    /// <summary>Элементов: {0:N0} (скрыто: {1:N0})</summary>
    public static string StatusItemsWithHidden => Get(nameof(StatusItemsWithHidden));

    /// <summary>Подходит {0:N0} из {1:N0} по запросу «{2}»</summary>
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

    /// <summary>Переместить?</summary>
    public static string ConfirmMoveTitle => Get(nameof(ConfirmMoveTitle));

    /// <summary>Переместить элемент?  Откуда: {0} Куда:   {1}</summary>
    public static string ConfirmMoveOne => Get(nameof(ConfirmMoveOne));

    /// <summary>Переместить {0} элем. в: {1}?</summary>
    public static string ConfirmMoveMany => Get(nameof(ConfirmMoveMany));

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

    /// <summary>В буфере обмена файл, которого нет на диске (вложение письма, файл внутри архива) — вставить его Wander не может</summary>
    public static string StatusClipboardVirtualFiles => Get(nameof(StatusClipboardVirtualFiles));

    /// <summary>Скопировано внутри Wander: системный буфер обмена сейчас занят другим приложением</summary>
    public static string StatusClipboardNotShared => Get(nameof(StatusClipboardNotShared));

    /// <summary>Выбрано: {0} — {1}</summary>
    public static string StatusSelection => Get(nameof(StatusSelection));

    /// <summary> (папок: {0}, размер не считается)</summary>
    public static string StatusSelectionFolders => Get(nameof(StatusSelectionFolders));

    /// <summary>{0}: {1} px · Ctrl + нажать колёсико — вернуть {2} px</summary>
    public static string StatusViewSize => Get(nameof(StatusViewSize));

    /// <summary>{0}: {1} px (стандартный)</summary>
    public static string StatusViewSizeDefault => Get(nameof(StatusViewSizeDefault));

    /// <summary>Подходит {0} из {1} по фильтру оценок</summary>
    public static string StatusRatingFilterMatches => Get(nameof(StatusRatingFilterMatches));

    /// <summary>Создать файл оценки?</summary>
    public static string ConfirmCreateSidecarTitle => Get(nameof(ConfirmCreateSidecarTitle));

    /// <summary>У снимка «{1}» нет файла с оценкой. Рядом будет создан «{0}» — в нём и сохранится оценка.  Отменить создание можно через Ctrl+Z.</summary>
    public static string ConfirmCreateSidecar => Get(nameof(ConfirmCreateSidecar));

    /// <summary>Внимание: RawTherapee применяет профиль по-умолчанию (Auto-Matched Curve) только к снимкам без .pp3. Как только .pp3 появится, снимок начнёт открываться в RawTherapee с нейтральных значений, а не с автоподбора. Формат меняется в настройках, раздел «Галерея».</summary>
    public static string ConfirmCreateSidecarPp3Warning => Get(nameof(ConfirmCreateSidecarPp3Warning));

    /// <summary>Поиск: найдено {0:N0}, просмотрено {1:N0}</summary>
    public static string StatusSearching => Get(nameof(StatusSearching));

    /// <summary>Найдено {0:N0} по запросу «{1}» (просмотрено файлов: {2:N0})</summary>
    public static string StatusSearchFound => Get(nameof(StatusSearchFound));

    /// <summary>Ничего не найдено по запросу «{0}»</summary>
    public static string StatusSearchNothing => Get(nameof(StatusSearchNothing));

    /// <summary>Поиск остановлен, найдено: {0:N0}</summary>
    public static string StatusSearchStopped => Get(nameof(StatusSearchStopped));

    /// <summary>— показаны первые {0:N0}</summary>
    public static string StatusSearchTruncated => Get(nameof(StatusSearchTruncated));

    /// <summary> — не удалось прочитать файлов: {0}</summary>
    public static string StatusSearchUnreadable => Get(nameof(StatusSearchUnreadable));

    /// <summary>У {0} снимков нет файла с оценкой. Рядом с каждым будет создан файл «{1}» — в нём и сохранится оценка.  Отменить создание можно через Ctrl+Z, одним нажатием на всю пачку.</summary>
    public static string ConfirmCreateSidecarMany => Get(nameof(ConfirmCreateSidecarMany));

    /// <summary>Оценка {0} — файлов: {1}</summary>
    public static string StatusRatingApplied => Get(nameof(StatusRatingApplied));

    /// <summary>Оценка снята — файлов: {0}</summary>
    public static string StatusRatingCleared => Get(nameof(StatusRatingCleared));
}
