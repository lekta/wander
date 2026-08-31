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


    public static string Get(string key) {
        return _resources.GetString(key, CultureInfo.CurrentUICulture) ?? key;
    }

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
}
