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
public static partial class Strings {
    private static readonly ResourceManager _resources =
        new("Wander.App.Resources.Strings", typeof(Strings).Assembly);


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

    /// <summary>Недавние папки (F4)</summary>
    public static string RecentFoldersHint => Get(nameof(RecentFoldersHint));

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

    /// <summary>Нельзя вставить</summary>
    public static string CannotPasteTitle => Get(nameof(CannotPasteTitle));

    /// <summary>Файлы идентичны</summary>
    public static string ConflictVerdictIdentical => Get(nameof(ConflictVerdictIdentical));

    /// <summary>Содержимое отличается</summary>
    public static string ConflictVerdictContentDiffers => Get(nameof(ConflictVerdictContentDiffers));

    /// <summary>Разные типы: файл и папка</summary>
    public static string ConflictVerdictDifferentKind => Get(nameof(ConflictVerdictDifferentKind));

    /// <summary>Копирование, совпадений имён: {0}</summary>
    public static string ConflictTitleCopy => Get(nameof(ConflictTitleCopy));

    /// <summary>Перемещение, совпадений имён: {0}</summary>
    public static string ConflictTitleMove => Get(nameof(ConflictTitleMove));

    /// <summary>Из: {0}</summary>
    public static string ConflictFrom => Get(nameof(ConflictFrom));

    /// <summary>Из: несколько папок</summary>
    public static string ConflictFromSeveral => Get(nameof(ConflictFromSeveral));

    /// <summary>В: {0}</summary>
    public static string ConflictTo => Get(nameof(ConflictTo));

    /// <summary>Сравниваем…</summary>
    public static string ConflictComparing => Get(nameof(ConflictComparing));

    /// <summary>заменить</summary>
    public static string ConflictChoiceReplace => Get(nameof(ConflictChoiceReplace));

    /// <summary>оставить</summary>
    public static string ConflictChoiceKeep => Get(nameof(ConflictChoiceKeep));

    /// <summary>оба</summary>
    public static string ConflictChoiceBoth => Get(nameof(ConflictChoiceBoth));

    /// <summary>слияние, читаем…</summary>
    public static string ConflictMergeScanning => Get(nameof(ConflictMergeScanning));

    /// <summary>слияние, содержимое не прочитано</summary>
    public static string ConflictMergeFailed => Get(nameof(ConflictMergeFailed));

    /// <summary>слияние: совпадений {0}, прочих файлов {1}</summary>
    public static string ConflictMergeSummary => Get(nameof(ConflictMergeSummary));

    /// <summary>Папка заменится целиком, без слияния</summary>
    public static string ConflictFolderReplaceNote => Get(nameof(ConflictFolderReplaceNote));

    /// <summary>Не спрашивать про одинаковые файлы</summary>
    public static string ConflictSkipIdentical => Get(nameof(ConflictSkipIdentical));

    /// <summary>За пары, совпадающие побайтово, оставлять то, что уже на месте.</summary>
    public static string ConflictSkipIdenticalHint => Get(nameof(ConflictSkipIdenticalHint));

    /// <summary>Применить к нерешённым:</summary>
    public static string ConflictBulkCaption => Get(nameof(ConflictBulkCaption));

    /// <summary>Заменить</summary>
    public static string ConflictBulkReplace => Get(nameof(ConflictBulkReplace));

    /// <summary>Оставить оригиналы</summary>
    public static string ConflictBulkSkip => Get(nameof(ConflictBulkSkip));

    /// <summary>Оставить оба</summary>
    public static string ConflictBulkKeepBoth => Get(nameof(ConflictBulkKeepBoth));

    /// <summary>Заменить, если новее</summary>
    public static string ConflictBulkReplaceIfNewer => Get(nameof(ConflictBulkReplaceIfNewer));

    /// <summary>Решено {0} из {1}</summary>
    public static string ConflictDecided => Get(nameof(ConflictDecided));

    /// <summary>Папка</summary>
    public static string KindFolderNoun => Get(nameof(KindFolderNoun));

    /// <summary>Файл — колонка «Тип»</summary>
    public static string ColumnTypeFile => Get(nameof(ColumnTypeFile));

    /// <summary>Папка — колонка «Тип»</summary>
    public static string ColumnTypeFolder => Get(nameof(ColumnTypeFolder));

    /// <summary>Диск — колонка «Тип»</summary>
    public static string ColumnTypeDrive => Get(nameof(ColumnTypeDrive));

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

    /// <summary>Заменить все</summary>
    public static string ConflictReplaceAll => Get(nameof(ConflictReplaceAll));

    /// <summary>Пропустить все</summary>
    public static string ConflictSkipAll => Get(nameof(ConflictSkipAll));

    /// <summary>Заменяет все файлы, включая уже отвеченные, и сразу закрывает окно. Заменённые уходят в корзину — Ctrl+Z вернёт всё обратно.</summary>
    public static string ConflictReplaceAllHint => Get(nameof(ConflictReplaceAllHint));

    /// <summary>Ничего не переносит и сразу закрывает окно.</summary>
    public static string ConflictSkipAllHint => Get(nameof(ConflictSkipAllHint));

    /// <summary>Свернуть</summary>
    public static string ActionMinimize => Get(nameof(ActionMinimize));

    /// <summary>Показать</summary>
    public static string ActionShow => Get(nameof(ActionShow));

    /// <summary>Файлов: {0} из {1}</summary>
    public static string OperationItems => Get(nameof(OperationItems));

    /// <summary>{0} из {1}</summary>
    public static string OperationBytes => Get(nameof(OperationBytes));

    /// <summary>{0}/с</summary>
    public static string OperationSpeed => Get(nameof(OperationSpeed));

    /// <summary>осталось ~ {0}</summary>
    public static string OperationRemaining => Get(nameof(OperationRemaining));

    /// <summary>{0} %</summary>
    public static string OperationPercent => Get(nameof(OperationPercent));

    /// <summary>{0}: {1} %</summary>
    public static string OperationOne => Get(nameof(OperationOne));

    /// <summary>Операций: {0} — {1} %</summary>
    public static string OperationMany => Get(nameof(OperationMany));

    /// <summary>{0} с</summary>
    public static string DurationSeconds => Get(nameof(DurationSeconds));

    /// <summary>{0} мин {1} с</summary>
    public static string DurationMinutes => Get(nameof(DurationMinutes));

    /// <summary>{0} ч {1} мин</summary>
    public static string DurationHours => Get(nameof(DurationHours));

    /// <summary>«{0}»</summary>
    public static string DragOneItem => Get(nameof(DragOneItem));

    /// <summary>Оценка</summary>
    public static string FilterRatingLabel => Get(nameof(FilterRatingLabel));

    /// <summary>Показывать только снимки с этой цветовой меткой. Повторный щелчок снимает фильтр.</summary>
    public static string FilterColorHint => Get(nameof(FilterColorHint));

    /// <summary>Сбросить фильтр</summary>
    public static string FilterClear => Get(nameof(FilterClear));

    /// <summary>Показывать снимки с этой оценкой и выше. С Ctrl — добавить или убрать одну оценку. Повторный щелчок снимает фильтр.</summary>
    public static string FilterRatingHint => Get(nameof(FilterRatingHint));

    /// <summary>Показывать снимки без оценки. С Ctrl — добавить их к уже выбранным оценкам.</summary>
    public static string FilterUnratedHint => Get(nameof(FilterUnratedHint));

    /// <summary>Архив «{0}» не читается: повреждён или недоступен</summary>
    public static string StatusArchiveUnreadable => Get(nameof(StatusArchiveUnreadable));

    /// <summary>«{0}»: пусто или защищено паролем</summary>
    public static string StatusArchiveEmptyOrLocked => Get(nameof(StatusArchiveEmptyOrLocked));

    /// <summary>Скопировано из архива: {0}. В другие программы — через «Извлечь…»</summary>
    public static string StatusArchiveCopied => Get(nameof(StatusArchiveCopied));

    /// <summary>Открыта временная копия «{0}»; изменения в архив не попадут</summary>
    public static string StatusArchiveTempCopy => Get(nameof(StatusArchiveTempCopy));

    /// <summary>Не извлечь: архив защищён паролем</summary>
    public static string StatusArchiveLocked => Get(nameof(StatusArchiveLocked));

    /// <summary>Извлечение не удалось: {0}</summary>
    public static string StatusExtractFailed => Get(nameof(StatusExtractFailed));

    /// <summary>Извлечено</summary>
    public static string VerbExtracted => Get(nameof(VerbExtracted));

    /// <summary>Извлечение</summary>
    public static string ProgressExtracting => Get(nameof(ProgressExtracting));

    /// <summary>Куда извлечь</summary>
    public static string ExtractPickFolderTitle => Get(nameof(ExtractPickFolderTitle));

    /// <summary>Файл внутри архива больше 32 МБ</summary>
    public static string PreviewArchiveTooBig => Get(nameof(PreviewArchiveTooBig));

    /// <summary>В архиве</summary>
    public static string SummaryInsideArchive => Get(nameof(SummaryInsideArchive));


    public static string Get(string key) {
        return _resources.GetString(key, CultureInfo.CurrentUICulture) ?? key;
    }
}
