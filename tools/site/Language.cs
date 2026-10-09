namespace Wander.Site;

/// <summary>
/// One language of the site: where its sources are, where its pages go and
/// the words of the pages' frame - header, footer, the versions page. Russian
/// is the site's root, English lives under en/ (2026-10-08); the two guides
/// pair page for page, which is what the language switch on every page
/// leads by.
/// </summary>
internal sealed record Language {
    public static readonly Language Russian = new() {
        Code = "ru",
        Folder = "",
        Name = "Русский",
        Locale = "ru_RU",
        GuideSource = "docs/GUIDE.md",
        LandingSource = "docs/site/index.html",
        Changelog = "docs/CHANGELOG.md",
        Guide = "Руководство",
        Versions = "Версии",
        Sections = "Разделы",
        SectionsLabel = "Разделы руководства",
        License = "Лицензия PolyForm Noncommercial",
        Report = "Сообщить о проблеме",
        BuildTitle = "Когда и из какого коммита собран сайт",
        Toc = "Содержание",
        TocLabel = "Содержание страницы",
        Screenshot = "Скриншот",
        Close = "Закрыть",
        VersionsDescription = "Все выпуски Wander: дата, страница релиза и руководство каждой версии.",
        VersionsIntro = "Руководство на сайте описывает последний релиз. Руководство прошлой версии открывается из её строки, список изменений в ",
        VersionColumns = new[] { "Версия", "Дата", "Релиз", "Руководство" },
        Download = "скачать",
        Open = "открыть",
        OtherGuide = "",
        DateFormat = "dd.MM.yyyy",
    };

    public static readonly Language English = new() {
        Code = "en",
        Folder = "en/",
        Name = "English",
        Locale = "en_US",
        GuideSource = "docs/GUIDE.en.md",
        LandingSource = "docs/site/index.en.html",
        Changelog = "docs/CHANGELOG.en.md",
        Guide = "Guide",
        Versions = "Versions",
        Sections = "Contents",
        SectionsLabel = "Guide contents",
        License = "PolyForm Noncommercial license",
        Report = "Report a problem",
        BuildTitle = "When and from which commit the site was built",
        Toc = "On this page",
        TocLabel = "On this page",
        Screenshot = "Screenshot",
        Close = "Close",
        VersionsDescription = "Every Wander release: its date, release page and the guide of each version.",
        VersionsIntro = "The guide on this site describes the latest release. An earlier version's guide opens from its row; what changed is in the ",
        VersionColumns = new[] { "Version", "Date", "Release", "Guide" },
        Download = "download",
        Open = "open",
        OtherGuide = "in Russian",
        DateFormat = "yyyy-MM-dd",
    };

    public static readonly Language[] All = { Russian, English };


    /// <summary>The html lang attribute and hreflang.</summary>
    public required string Code { get; init; }

    /// <summary>Where the language's pages start, from the site's root: "" or "en/".</summary>
    public required string Folder { get; init; }

    /// <summary>The language's own name: what the switch on the other language's pages says.</summary>
    public required string Name { get; init; }

    public required string Locale { get; init; }

    public required string GuideSource { get; init; }

    public required string LandingSource { get; init; }

    /// <summary>What the versions page sends to for what changed: the English one starts at 0.6.</summary>
    public required string Changelog { get; init; }

    public required string Guide { get; init; }

    public required string Versions { get; init; }

    public required string Sections { get; init; }

    public required string SectionsLabel { get; init; }

    public required string License { get; init; }

    public required string Report { get; init; }

    public required string BuildTitle { get; init; }

    public required string Toc { get; init; }

    public required string TocLabel { get; init; }

    public required string Screenshot { get; init; }

    public required string Close { get; init; }

    public required string VersionsDescription { get; init; }

    /// <summary>The versions page's paragraph, up to the CHANGELOG link it ends with.</summary>
    public required string VersionsIntro { get; init; }

    public required string[] VersionColumns { get; init; }

    public required string Download { get; init; }

    public required string Open { get; init; }

    /// <summary>
    /// A release older than this language's guide links the Russian one,
    /// saying so; empty for Russian, which every release has.
    /// </summary>
    public required string OtherGuide { get; init; }

    public required string DateFormat { get; init; }
}
