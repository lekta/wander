namespace Wander.App.Resources;

/// <summary>
/// Справочник горячих клавиш.
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
    /// <summary>Навигация</summary>
    public static string HotkeyGroupNavigation => Get(nameof(HotkeyGroupNavigation));

    /// <summary>Назад</summary>
    public static string HotkeyBack => Get(nameof(HotkeyBack));

    /// <summary>Вперёд</summary>
    public static string HotkeyForward => Get(nameof(HotkeyForward));

    /// <summary>На уровень вверх</summary>
    public static string HotkeyUp => Get(nameof(HotkeyUp));

    /// <summary>На уровень вверх</summary>
    public static string HotkeyUpBackspace => Get(nameof(HotkeyUpBackspace));

    /// <summary>Открыть выбранное: папку — войти, файл — системной ассоциацией</summary>
    public static string HotkeyOpen => Get(nameof(HotkeyOpen));

    /// <summary>Правка пути в адресной строке, текст выделяется целиком</summary>
    public static string HotkeyAddressBar => Get(nameof(HotkeyAddressBar));

    /// <summary>Список последних посещённых папок</summary>
    public static string HotkeyRecent => Get(nameof(HotkeyRecent));

    /// <summary>Перейти по введённому пути</summary>
    public static string HotkeyAddressGo => Get(nameof(HotkeyAddressGo));

    /// <summary>Отменить правку, вернуть крошки и фокус в список</summary>
    public static string HotkeyAddressCancel => Get(nameof(HotkeyAddressCancel));

    /// <summary>Обновить содержимое папки</summary>
    public static string HotkeyRefresh => Get(nameof(HotkeyRefresh));

    /// <summary>Перемещение по окну</summary>
    public static string HotkeyGroupPanes => Get(nameof(HotkeyGroupPanes));

    /// <summary>Следующая / предыдущая область окна</summary>
    public static string HotkeyNextPane => Get(nameof(HotkeyNextPane));

    /// <summary>В список файлов</summary>
    public static string HotkeyToList => Get(nameof(HotkeyToList));

    /// <summary>В панель папок, на узел текущей папки. Повторное нажатие — в другую панель (закладки ↔ компьютер)</summary>
    public static string HotkeyToTree => Get(nameof(HotkeyToTree));

    /// <summary>Показать текущую папку в дереве и встать на неё</summary>
    public static string HotkeyRevealInTree => Get(nameof(HotkeyRevealInTree));

    /// <summary>Показать или убрать панель быстрого просмотра</summary>
    public static string HotkeyTogglePreview => Get(nameof(HotkeyTogglePreview));

    /// <summary>Свернуть или раскрыть ветку</summary>
    public static string HotkeyTreeExpand => Get(nameof(HotkeyTreeExpand));

    /// <summary>Перейти в папку под курсором</summary>
    public static string HotkeyTreeEnter => Get(nameof(HotkeyTreeEnter));

    /// <summary>Вернуть фокус в список</summary>
    public static string HotkeyTreeEscape => Get(nameof(HotkeyTreeEscape));

    /// <summary>Файловые операции</summary>
    public static string HotkeyGroupFileOps => Get(nameof(HotkeyGroupFileOps));

    /// <summary>Копировать</summary>
    public static string HotkeyCopy => Get(nameof(HotkeyCopy));

    /// <summary>Копировать полный путь в буфер обмена</summary>
    public static string HotkeyCopyPath => Get(nameof(HotkeyCopyPath));

    /// <summary>Вырезать</summary>
    public static string HotkeyCut => Get(nameof(HotkeyCut));

    /// <summary>Вставить</summary>
    public static string HotkeyPaste => Get(nameof(HotkeyPaste));

    /// <summary>Удалить в корзину</summary>
    public static string HotkeyDelete => Get(nameof(HotkeyDelete));

    /// <summary>Удалить безвозвратно: всегда спрашивает, не откатывается</summary>
    public static string HotkeyDeleteForever => Get(nameof(HotkeyDeleteForever));

    /// <summary>Переименовать прямо в строке списка: Enter — применить, Esc — отменить</summary>
    public static string HotkeyRename => Get(nameof(HotkeyRename));

    /// <summary>Создать папку</summary>
    public static string HotkeyNewFolder => Get(nameof(HotkeyNewFolder));

    /// <summary>Отменить последнюю операцию</summary>
    public static string HotkeyUndo => Get(nameof(HotkeyUndo));

    /// <summary>Выделение и поиск</summary>
    public static string HotkeyGroupSearch => Get(nameof(HotkeyGroupSearch));

    /// <summary>Выделить всё</summary>
    public static string HotkeySelectAll => Get(nameof(HotkeySelectAll));

    /// <summary>Фокус в поле фильтра</summary>
    public static string HotkeyFilter => Get(nameof(HotkeyFilter));

    /// <summary>Открыть окно поиска</summary>
    public static string HotkeySearchWindow => Get(nameof(HotkeySearchWindow));

    /// <summary>Искать сразу, не дожидаясь паузы и не считая символы</summary>
    public static string HotkeySearchNow => Get(nameof(HotkeySearchNow));

    /// <summary>Закрыть окно, клавиатура возвращается в список файлов</summary>
    public static string HotkeySearchClose => Get(nameof(HotkeySearchClose));

    /// <summary>Одним нажатием: остановить поиск, сбросить фильтр, вернуть клавиатуру в список</summary>
    public static string HotkeyFilterEscape => Get(nameof(HotkeyFilterEscape));

    /// <summary>Повторить поиск</summary>
    public static string HotkeySearchRepeat => Get(nameof(HotkeySearchRepeat));

    /// <summary>Снять выделение</summary>
    public static string HotkeyClearSelection => Get(nameof(HotkeyClearSelection));

    /// <summary>Перейти к файлу, имя которого с них начинается. Пауза в секунду начинает набор заново, повтор той же буквы перебирает файлы на неё</summary>
    public static string HotkeyTypeAhead => Get(nameof(HotkeyTypeAhead));

    /// <summary>Обычное перемещение, плюс переходы через край строки: → справа — на следующую строку, ← слева — на предыдущую, ↑ в верхней — к первому файлу, ↓ в нижней — к последнему. С Shift выделение растягивается</summary>
    public static string HotkeyGridArrows => Get(nameof(HotkeyGridArrows));

    /// <summary>Свойства выбранного</summary>
    public static string HotkeyProperties => Get(nameof(HotkeyProperties));

    /// <summary>Вид</summary>
    public static string HotkeyGroupView => Get(nameof(HotkeyGroupView));

    /// <summary>Галерея</summary>
    public static string HotkeyViewGallery => Get(nameof(HotkeyViewGallery));

    /// <summary>Оценка выделенным файлам, 0 — снять</summary>
    public static string HotkeyRateInGallery => Get(nameof(HotkeyRateInGallery));

    /// <summary>Крупные значки</summary>
    public static string HotkeyViewLargeIcons => Get(nameof(HotkeyViewLargeIcons));

    /// <summary>Таблица</summary>
    public static string HotkeyViewDetails => Get(nameof(HotkeyViewDetails));

    /// <summary>Плитки</summary>
    public static string HotkeyViewTiles => Get(nameof(HotkeyViewTiles));
}
