namespace Wander.App.Resources;

/// <summary>
/// Панель просмотра и поиск.
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
    /// <summary>Выберите файл для просмотра</summary>
    public static string PreviewSelectFile => Get(nameof(PreviewSelectFile));

    /// <summary>Для этого файла просмотр недоступен</summary>
    public static string PreviewUnsupported => Get(nameof(PreviewUnsupported));

    /// <summary>Анализ содержимого</summary>
    public static string PreviewCounting => Get(nameof(PreviewCounting));

    /// <summary>{0:N0} файлов · {1:N0} папок · {2}</summary>
    public static string PreviewFolderHeadline => Get(nameof(PreviewFolderHeadline));

    /// <summary>Слишком глубокая вложенность — вглубь пройдено не всё, числа неполные</summary>
    public static string PreviewFolderTruncated => Get(nameof(PreviewFolderTruncated));

    /// <summary>{0:N0} шт.</summary>
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

    /// <summary>Фильтр: имя, либо имя:текст (Ctrl+F). Окно поиска — Ctrl+Shift+F. Esc — сбросить</summary>
    public static string SearchHint => Get(nameof(SearchHint));

    /// <summary>Фильтр: имя или имя:текст</summary>
    public static string SearchPlaceholder => Get(nameof(SearchPlaceholder));

    /// <summary>Воспроизведение недоступно</summary>
    public static string PreviewVideoUnavailable => Get(nameof(PreviewVideoUnavailable));

    /// <summary>Воспроизведение / пауза (Пробел)</summary>
    public static string PreviewPlayPause => Get(nameof(PreviewPlayPause));

    /// <summary>Оценки пока нет. Щелчок по звезде создаст файл рядом со снимком — с подтверждением.</summary>
    public static string PreviewRatingUnsaved => Get(nameof(PreviewRatingUnsaved));

    /// <summary>Окно поиска (Ctrl+Shift+F)</summary>
    public static string SearchOptions => Get(nameof(SearchOptions));

    /// <summary>Искать в подпапках</summary>
    public static string SearchScopeSubfolders => Get(nameof(SearchScopeSubfolders));

    /// <summary>Остановить поиск</summary>
    public static string SearchStop => Get(nameof(SearchStop));

    /// <summary>Очистить поиск</summary>
    public static string SearchClear => Get(nameof(SearchClear));

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

    /// <summary>… показано начало файла, всего {0}</summary>
    public static string PreviewClipped => Get(nameof(PreviewClipped));

    /// <summary>{0} кбит/с</summary>
    public static string PreviewAudioBitrate => Get(nameof(PreviewAudioBitrate));

    /// <summary>{0:0.#} кГц</summary>
    public static string PreviewAudioSampleRate => Get(nameof(PreviewAudioSampleRate));

    /// <summary>моно</summary>
    public static string PreviewAudioMono => Get(nameof(PreviewAudioMono));

    /// <summary>стерео</summary>
    public static string PreviewAudioStereo => Get(nameof(PreviewAudioStereo));

    /// <summary>{0:N0} треугольников · {1:N0} вершин</summary>
    public static string PreviewModelDetail => Get(nameof(PreviewModelDetail));
}
