namespace Wander.App.Resources;

/// <summary>
/// Окно группового переименования.
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
    /// <summary>Переименовать группой — {0}</summary>
    public static string BatchRenameTitle => Get(nameof(BatchRenameTitle));

    /// <summary>Найти и заменить</summary>
    public static string BatchRenameUseFind => Get(nameof(BatchRenameUseFind));

    /// <summary>Найти</summary>
    public static string BatchRenameFind => Get(nameof(BatchRenameFind));

    /// <summary>Заменить</summary>
    public static string BatchRenameReplace => Get(nameof(BatchRenameReplace));

    /// <summary>Без учёта регистра</summary>
    public static string BatchRenameIgnoreCase => Get(nameof(BatchRenameIgnoreCase));

    /// <summary>Регулярное выражение</summary>
    public static string BatchRenameRegex => Get(nameof(BatchRenameRegex));

    /// <summary>Синтаксис .NET; в поле «Заменить» $1 — первая группа в скобках</summary>
    public static string BatchRenameRegexHint => Get(nameof(BatchRenameRegexHint));

    /// <summary>Шаблон</summary>
    public static string BatchRenameTemplate => Get(nameof(BatchRenameTemplate));

    /// <summary>[N] имя · [C] счётчик · [D] дата изменения · [D:yyyy-MM-dd] дата в своём формате · [X] дата съёмки · [P] папка. Остальное — как написано</summary>
    public static string BatchRenameTemplateHint => Get(nameof(BatchRenameTemplateHint));

    /// <summary>Счётчик [C]</summary>
    public static string BatchRenameCounter => Get(nameof(BatchRenameCounter));

    /// <summary>с</summary>
    public static string BatchRenameCounterStart => Get(nameof(BatchRenameCounterStart));

    /// <summary>шаг</summary>
    public static string BatchRenameCounterStep => Get(nameof(BatchRenameCounterStep));

    /// <summary>цифр</summary>
    public static string BatchRenameCounterWidth => Get(nameof(BatchRenameCounterWidth));

    /// <summary>Регистр имени</summary>
    public static string BatchRenameNameCase => Get(nameof(BatchRenameNameCase));

    /// <summary>Регистр расширения</summary>
    public static string BatchRenameExtensionCase => Get(nameof(BatchRenameExtensionCase));

    /// <summary>Расширение шаблоном и заменой не меняется — только регистром</summary>
    public static string BatchRenameExtensionHint => Get(nameof(BatchRenameExtensionHint));

    /// <summary>как есть</summary>
    public static string BatchRenameCaseUnchanged => Get(nameof(BatchRenameCaseUnchanged));

    /// <summary>строчные</summary>
    public static string BatchRenameCaseLower => Get(nameof(BatchRenameCaseLower));

    /// <summary>ПРОПИСНЫЕ</summary>
    public static string BatchRenameCaseUpper => Get(nameof(BatchRenameCaseUpper));

    /// <summary>Первая заглавная</summary>
    public static string BatchRenameCaseSentence => Get(nameof(BatchRenameCaseSentence));

    /// <summary>Было</summary>
    public static string BatchRenameColumnOld => Get(nameof(BatchRenameColumnOld));

    /// <summary>Станет</summary>
    public static string BatchRenameColumnNew => Get(nameof(BatchRenameColumnNew));

    /// <summary>Статус</summary>
    public static string BatchRenameColumnStatus => Get(nameof(BatchRenameColumnStatus));

    /// <summary>Нумерация — в порядке списка</summary>
    public static string BatchRenameOrderNote => Get(nameof(BatchRenameOrderNote));

    /// <summary>Дата съёмки читается…</summary>
    public static string BatchRenameReadingShotDates => Get(nameof(BatchRenameReadingShotDates));

    /// <summary>Изменится {0} из {1}, конфликтов {2}</summary>
    public static string RenameSummary => Get(nameof(RenameSummary));

    /// <summary>Без изменений</summary>
    public static string RenameStatusUnchanged => Get(nameof(RenameStatusUnchanged));

    /// <summary>Изменится</summary>
    public static string RenameStatusRenamed => Get(nameof(RenameStatusRenamed));

    /// <summary>Недопустимое имя</summary>
    public static string RenameStatusInvalidName => Get(nameof(RenameStatusInvalidName));

    /// <summary>Повтор внутри группы</summary>
    public static string RenameStatusDuplicateInBatch => Get(nameof(RenameStatusDuplicateInBatch));

    /// <summary>Имя уже занято</summary>
    public static string RenameStatusCollides => Get(nameof(RenameStatusCollides));
}
