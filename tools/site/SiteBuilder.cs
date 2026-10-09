using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Markdig;
using Markdig.Extensions.EmphasisExtras;
using Markdig.Extensions.Footnotes;
using Markdig.Extensions.Tables;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Wander.Site;

/// <summary>
/// Puts the site together in memory - guide pages, versions, landing,
/// pictures - checks every link in it and only then writes it out.
///
/// <para>
/// Every link is relative and names index.html outright, so the same files
/// work under lekta.github.io/wander/, at the root of any host and opened
/// from disk. The stylesheet is inlined into each page and there is no
/// script: a page is one request, pictures aside.
/// </para>
///
/// <para>
/// The debug build (<c>--debug</c>) is the same site for a look before a
/// push: the places GUIDE.md marks for a screenshot show as a dashed frame
/// instead of vanishing, and Program writes it even when problems were
/// found.
/// </para>
/// </summary>
internal sealed class SiteBuilder {
    public const string Repository = "https://github.com/lekta/wander";

    /// <summary>
    /// Where the site is served from, slash included: the canonical address
    /// of every page, the sitemap and the link preview name it in full.
    /// Moves with the site - another host, a domain of its own.
    /// </summary>
    public const string SiteUrl = "https://lekta.github.io/wander/";

    /// <summary>
    /// The IndexNow key: after a deployment site.yml hands the pages of the
    /// sitemap to Yandex and Bing, which check the key against the file of
    /// the same name written beside the landing. Public by design - the
    /// file is on the site - so nothing to keep secret.
    /// </summary>
    public const string IndexNowKey = "c9a50c3e765621030e3adcaa5b3da083";

    /// <summary>
    /// Cloudflare Web Analytics: the one script on the site, and only in
    /// the deployed build (--analytics). A module script, so it loads after
    /// the page is shown and never holds it up: about 10 KB over the wire,
    /// cached for a day, and one beacon. The token is per hostname and
    /// public - the digest at lekta.github.io/gamedev_digest/ shares it,
    /// the dashboard tells the sites apart by path. No cookies, nothing
    /// personal.
    /// </summary>
    private const string WebAnalyticsToken = "d74d8d284334461699e827bfc667146e";

    private const string TemplateSource = "tools/site/page.html";

    /// <summary>
    /// The picture a messenger shows for a link to the site: og:image in the
    /// landing, by its full address. JPEG, the one picture that is not WebP:
    /// not every messenger reads WebP.
    /// </summary>
    private const string LinkPreviewSource = "docs/screenshots/og.jpg";

    /// <summary>The app's icon among the screenshots: the same in every language, never a screenshot to retake.</summary>
    private const string AppIcon = "icon.webp";

    /// <summary>Where the settings pages name the guide section F1 opens.</summary>
    private const string SettingsPagesSource = "src/Wander.App/ViewModels/SettingsCategoryViewModel.cs";

    /// <summary>The text column of a guide page in pixels (44rem in site.css): a wider picture is shown shrunk to it.</summary>
    private const int ColumnWidth = 704;

    /// <summary>About what a search result shows under the title before cutting it.</summary>
    private const int DescriptionLength = 160;

    /// <summary>The settings page's F1 target in each guide: <c>guide:</c> in GUIDE.md, <c>guideEn:</c> in GUIDE.en.md.</summary>
    private static readonly Dictionary<Language, Regex> _guideArgument = new() {
        [Language.Russian] = new(@"\bguide: ""([^""]*)"""),
        [Language.English] = new(@"\bguideEn: ""([^""]*)"""),
    };

    /// <summary>
    /// A place in a guide that wants a screenshot: an HTML comment of its
    /// own, invisible on GitHub, dropped from the published site -
    /// <c>скрин:</c> in GUIDE.md, <c>screenshot:</c> in GUIDE.en.md.
    /// </summary>
    private static readonly Regex _screenshot = new(@"^<!--\s*(?:скрин|screenshot):\s*(.*?)\s*-->\s*$", RegexOptions.Singleline);

    /// <summary>
    /// A picture GUIDE.md sizes or floats itself: a line of its own holding
    /// <c>&lt;img src alt [width] [align="right"]&gt;</c>, which GitHub
    /// renders as it is.
    /// </summary>
    private static readonly Regex _imageTag = new(@"^<img\s+([^>]*?)\s*/?>\s*$", RegexOptions.Singleline);

    /// <summary>A picture of a landing, hand-written: <c>src="img/x.webp"</c> or, from en/, <c>src="../img/x.webp"</c>.</summary>
    private static readonly Regex _landingPicture = new(@"(src=""(?:\.\./)?img/)([^""/]+\.webp)""");

    private static readonly Regex _attribute = new(@"([a-z]+)=""([^""]*)""");
    private static readonly Regex _scheme = new("^[a-z][a-z0-9+.-]*:", RegexOptions.IgnoreCase);
    private static readonly Regex _placeholder = new(@"\{\{([a-z]+)\}\}");
    private static readonly Regex _link = new(@"\s(?:href|src)=""([^""]*)""");
    private static readonly Regex _id = new(@"\sid=""([^""]*)""");
    private static readonly UTF8Encoding _utf8 = new(encoderShouldEmitUTF8Identifier: false);

    private readonly string _root;
    private readonly bool _debug;
    private readonly bool _analytics;
    /// <summary>What GitHub renders in GUIDE.md and the site must too: tables, ~~strikethrough~~ and [^footnotes].</summary>
    private readonly MarkdownPipeline _pipeline = new MarkdownPipelineBuilder()
        .UsePipeTables()
        .UseEmphasisExtras(EmphasisExtraOptions.Strikethrough)
        .UseFootnotes()
        .Build();

    /// <summary>Site path (relative, forward slashes) -> the page; the sitemap and the IndexNow key file go the same way.</summary>
    private readonly Dictionary<string, string> _pages = new(StringComparer.Ordinal);

    /// <summary>Site path -> the file copied there.</summary>
    private readonly Dictionary<string, string> _files = new(StringComparer.Ordinal);

    /// <summary>Each language's guide; the switch on a guide page leads to the page of the same number in the other.</summary>
    private readonly Dictionary<Language, Guide> _guides = new();

    /// <summary>The language whose pages are being rendered.</summary>
    private Language _language = Language.Russian;

    /// <summary>A screenshot file -> the languages whose pages showed it for want of their own (<see cref="Localized"/>).</summary>
    private readonly SortedDictionary<string, HashSet<Language>> _sharedShots = new(StringComparer.Ordinal);

    private Release? _latest;
    private int _screenshots;

    /// <summary>Screenshots on the page being rendered: each one's popover needs an id of its own.</summary>
    private int _shotsOnPage;


    public SiteBuilder(string root, bool debug, bool analytics) {
        _root = root;
        _debug = debug;
        _analytics = analytics;
    }


    public List<string> Errors { get; } = new();

    /// <summary>There is a site to write, problems or not: no guide pages or no release leave nothing.</summary>
    public bool IsBuilt => _pages.ContainsKey("index.html");


    public void Build() {
        string css = Minify(Read("tools/site/site.css"));
        string template = Read(TemplateSource);
        foreach (Language language in Language.All) {
            _guides[language] = Guide.Parse(Markdown.Parse(Read(language.GuideSource), _pipeline), language.GuideSource, Errors);
        }
        var releases = Releases.Read(_root, Errors);
        if (_guides.Values.Any(guide => guide.Pages.Count == 0) || releases.Count == 0) {
            return;
        }
        CheckPairs();

        _latest = releases[0];
        // The footer says when and from what the site was built, so a stale
        // cache is told from a fresh page at a glance: the build time in UTC
        // and the commit, as a link to it.
        string built = DateTime.UtcNow.ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture) + " UTC";
        string head = Releases.Head(_root, Errors) ?? "";
        string analytics = _analytics
            ? "<script type=\"module\" src=\"https://static.cloudflareinsights.com/beacon.min.js\" data-cf-beacon='{\"token\": \"" + WebAnalyticsToken + "\"}'></script>\n"
            : "";
        foreach (string picture in Directory.GetFiles(Path.Combine(_root, "docs", "screenshots"), "*.webp")) {
            _files["img/" + Path.GetFileName(picture)] = picture;
        }
        foreach (string icon in Directory.GetFiles(Path.Combine(_root, "docs", "icons"), "*.svg")) {
            _files["img/icons/" + Path.GetFileName(icon)] = icon;
        }
        string preview = Path.Combine(_root, LinkPreviewSource);
        if (File.Exists(preview)) {
            _files["img/og.jpg"] = preview;
        } else {
            Errors.Add($"{LinkPreviewSource}: missing - the landing's og:image names it");
        }

        foreach (Language language in Language.All) {
            _language = language;
            BuildLanguage(template, css, $"{built}, {head}", head, analytics, releases);
        }

        // What the search engines get to crawl: every page by its canonical
        // address, the folder form Pages serves, the landing first (site.yml
        // reads the site's address off it). The forwarding pages at guide/
        // are no pages of their own. No robots.txt: engines read one at the
        // root of the host only, and the site lives in a folder of it - the
        // sitemap is handed to them in their consoles (docs/PROMOTION.md).
        var canonical = new[] { "index.html" }
            .Concat(_pages.Keys.Where(path => path != "index.html" && !Language.All.Any(language => path == language.Folder + "guide/index.html")))
            .Select(path => SiteUrl + path[..^"index.html".Length]);
        _pages["sitemap.xml"] = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n"
            + "<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">\n"
            + string.Concat(canonical.Select(url => $"<url><loc>{url}</loc></url>\n"))
            + "</urlset>\n";
        _pages[IndexNowKey + ".txt"] = IndexNowKey;

        // After the sources are clean: a link a guide got wrong is already
        // reported with its line, and would come back here once per page.
        if (Errors.Count == 0) {
            CheckLinks();
            CheckSettingsPages();
        }
    }

    /// <summary>
    /// Writes the site into <paramref name="output"/>. Only what the
    /// generator makes is cleared first: a mistyped --out must not take
    /// anything else with it.
    /// </summary>
    public void Write(string output) {
        foreach (string owned in new[] { "guide", "versions", "img", "en" }) {
            string dir = Path.Combine(output, owned);
            if (Directory.Exists(dir)) {
                Directory.Delete(dir, recursive: true);
            }
        }

        foreach (var (path, html) in _pages) {
            string file = Path.Combine(output, path);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, html, _utf8);
        }
        foreach (var (path, source) in _files) {
            string file = Path.Combine(output, path);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.Copy(source, file, overwrite: true);
        }
    }

    /// <summary>Sizes against the budget: a guide page 10-30 KB, the landing with its pictures under 300 KB.</summary>
    public IEnumerable<string> Summary() {
        foreach (Language language in Language.All) {
            // guide/index.html only forwards to the first page.
            string guide = language.Folder + "guide/";
            var guidePages = _pages
                .Where(p => p.Key.StartsWith(guide, StringComparison.Ordinal) && p.Key != guide + "index.html")
                .ToList();
            var sizes = guidePages.ToDictionary(p => p.Key, p => (long)_utf8.GetByteCount(p.Value));
            var largest = sizes.MaxBy(p => p.Value);
            long smallest = sizes.Values.Min();
            // The landing's budget counts the pictures it shows, not the guide's.
            string path = language.Folder + "index.html";
            string landing = _pages[path];
            long page = _utf8.GetByteCount(landing);
            long shown = _link.Matches(landing)
                .Select(m => Resolve(path, m.Groups[1].Value))
                .Distinct()
                .Where(target => target is not null && _files.ContainsKey(target))
                .Sum(target => new FileInfo(_files[target!]).Length);

            yield return $"{language.Code}: {guidePages.Count} guide pages, {Kb(smallest)} to {Kb(largest.Value)} of 10-30 KB (largest {largest.Key})"
                + Over(smallest < 10 * 1024 || largest.Value > 30 * 1024);
            yield return $"{language.Code}: landing with its pictures {Kb(page + shown)} of 300 KB (the page {Kb(page)})" + Over(page + shown > 300 * 1024);
        }

        long pictures = _files.Values.Sum(file => new FileInfo(file).Length);
        yield return $"all pictures {Kb(pictures)}; download {_latest!.Tag}";
        if (_screenshots > 0) {
            yield return $"{_screenshots} place(s) marked for a screenshot" + (_debug ? ", shown" : ", shown with --debug");
        }
        var shared = _sharedShots.Where(shot => shot.Value.Count == Language.All.Length).Select(shot => shot.Key).ToList();
        if (shared.Count > 0) {
            yield return $"{shared.Count} screenshot(s) shared by both languages, no .ru / .en pair: {string.Join(", ", shared)}";
        }
    }


    /// <summary>
    /// The pages of <see cref="_language"/>: its guide, the forwarding page
    /// at guide/, its versions and its landing, all under its folder.
    /// </summary>
    private void BuildLanguage(string template, string css, string built, string head, string analytics, List<Release> releases) {
        Language language = _language;
        Guide guide = _guides[language];
        string build = $"<a href=\"{Repository}/commit/{head}\" title=\"{Escape(language.BuildTitle)}\">{built}</a>";
        string home = $"guide/{guide.Pages[0].Slug}/index.html";
        for (int i = 0; i < guide.Pages.Count; i++) {
            GuidePage page = guide.Pages[i];
            string folder = $"{language.Folder}guide/{page.Slug}/";
            string path = folder + "index.html";
            // Inside the guide its own link in the header leads nowhere new:
            // the navigation beside the text is the guide.
            _pages[path] = Fill(template, TemplateSource, Frame(path, i, css, build, analytics, new() {
                ["title"] = Escape(page.Title) + " | Wander",
                ["description"] = Escape(Description(page)),
                ["url"] = SiteUrl + folder,
                ["guidelink"] = "",
                ["nav"] = Nav(guide, page, "../"),
                ["main"] = RenderPage(guide, i),
            }));
        }
        // What the app's "Help" opens (CrashReporter.GuideUrl): the guide
        // has no page of its own at guide/, so this one sends the reader on
        // to the first - a refresh and a link, no script.
        string first = $"{guide.Pages[0].Slug}/index.html";
        _pages[language.Folder + "guide/index.html"] =
            $"<!doctype html>\n<html lang=\"{language.Code}\">\n<head>\n<meta charset=\"utf-8\">\n" +
            $"<meta http-equiv=\"refresh\" content=\"0; url={first}\">\n<title>{language.Guide} | Wander</title>\n</head>\n" +
            $"<body><a href=\"{first}\">{language.Guide}</a></body>\n</html>\n";
        string versions = language.Folder + "versions/index.html";
        _pages[versions] = Fill(template, TemplateSource, Frame(versions, null, css, build, analytics, new() {
            ["title"] = $"{language.Versions} | Wander",
            ["description"] = Escape(language.VersionsDescription),
            ["url"] = SiteUrl + language.Folder + "versions/",
            ["guidelink"] = $"<a class=\"wide\" href=\"../{home}\">{language.Guide}</a>\n",
            ["nav"] = Nav(guide, null, "../guide/"),
            ["main"] = RenderVersions(releases),
        }));
        string landing = language.Folder + "index.html";
        _pages[landing] = LocalizedPictures(Fill(Read(language.LandingSource), language.LandingSource, Frame(landing, null, css, build, analytics, new() {
            ["guide"] = home,
            ["version"] = _latest!.Version,
            ["download"] = $"{Repository}/releases/download/{_latest.Tag}/Wander.exe",
            ["release"] = $"{Repository}/releases/tag/{_latest.Tag}",
        })));
    }

    /// <summary>
    /// What every page of <see cref="_language"/> at <paramref name="path"/>
    /// fills in besides <paramref name="own"/>: the frame's words, the ways
    /// up to the site's root (pictures) and to the language's (landing,
    /// versions), the other language's addresses for search engines and the
    /// switch to it. <paramref name="guidePage"/> is the number of a guide
    /// page - its twin in the other guide has the same one - or null.
    /// </summary>
    private Dictionary<string, string> Frame(
        string path, int? guidePage, string css, string build, string analytics, Dictionary<string, string> own) {

        Language language = _language;
        Language other = Language.All.First(l => l != language);
        string twin = guidePage is { } index
            ? $"{other.Folder}guide/{_guides[other].Pages[Math.Min(index, _guides[other].Pages.Count - 1)].Slug}/index.html"
            : other.Folder + path[language.Folder.Length..];
        string ownPath = path[language.Folder.Length..];
        // Every language's address of the page for search engines; a reader
        // of neither language gets the English one (x-default).
        string alternates = string.Join("\n", Language.All
            .Select(l => (Code: l.Code, Path: l == language ? path : twin))
            .Append((Code: "x-default", Path: language == Language.English ? path : twin))
            .Select(a => $"<link rel=\"alternate\" hreflang=\"{a.Code}\" href=\"{SiteUrl}{a.Path[..^"index.html".Length]}\">"));

        var values = new Dictionary<string, string>(own) {
            ["css"] = css,
            ["site"] = SiteUrl,
            ["build"] = build,
            ["analytics"] = analytics,
            ["lang"] = language.Code,
            ["locale"] = language.Locale,
            ["root"] = Up(path),
            ["home"] = Up(ownPath),
            ["versions"] = language.Versions,
            ["sections"] = language.Sections,
            ["sectionslabel"] = language.SectionsLabel,
            ["license"] = language.License,
            ["report"] = language.Report,
            ["alternates"] = alternates,
            ["switch"] = $"<a class=\"lang\" href=\"{Up(path)}{twin}\" hreflang=\"{other.Code}\" lang=\"{other.Code}\">{other.Name}</a>",
        };

        return values;
    }

    /// <summary>The way from the page at <paramref name="path"/> (from the site's root) back up to the root: "../" a folder.</summary>
    private static string Up(string path) {
        return string.Concat(Enumerable.Repeat("../", path.Count(c => c == '/')));
    }

    /// <summary>img/ seen from a guide page of <see cref="_language"/>: the pictures are one set for both languages.</summary>
    private string ImageFolder => "../../" + Up(_language.Folder) + "img/";

    /// <summary>
    /// The two guides pair page for page - the switch on a page leads to
    /// the page of the same number in the other language - so they must
    /// have the same pages in the same places: as many, and each a ## or a
    /// ### where the other has one. A page added to one guide only is
    /// caught here, not by a reader landing on the wrong page.
    /// </summary>
    private void CheckPairs() {
        Guide russian = _guides[Language.Russian];
        Guide english = _guides[Language.English];
        if (russian.Pages.Count != english.Pages.Count) {
            Errors.Add($"{Language.English.GuideSource}: {english.Pages.Count} pages, {Language.Russian.GuideSource} {russian.Pages.Count} - the guides pair page for page");

            return;
        }

        for (int i = 0; i < russian.Pages.Count; i++) {
            if (russian.Pages[i].Heading.Level != english.Pages[i].Heading.Level) {
                Errors.Add($"{Language.English.GuideSource}:{english.Pages[i].Heading.Line + 1}: '{english.Pages[i].Title}' pairs with "
                    + $"'{russian.Pages[i].Title}' ({Language.Russian.GuideSource}:{russian.Pages[i].Heading.Line + 1}) but is not the same level");
            }
        }
    }

    private string RenderPage(Guide guide, int index) {
        GuidePage page = guide.Pages[index];
        _shotsOnPage = 0;
        // The footnotes the page refers to, in the order of their numbers:
        // the parser keeps them all at the end of the document, the site
        // shows each under the text of its own page.
        var notes = page.Blocks
            .SelectMany(Inlines<FootnoteLink>)
            .Where(link => !link.IsBackLink)
            .Select(link => link.Footnote)
            .Distinct()
            .OrderBy(note => note.Order)
            .ToList();
        foreach (Block block in page.Blocks.Concat(notes)) {
            // A list, not the live walk: a screenshot is replaced by its
            // button on the way, which changes the tree being walked.
            foreach (LinkInline link in Inlines<LinkInline>(block).ToList()) {
                link.Url = Rewrite(link, page, guide);
            }
        }

        // Above the title: the group on the left, leading to its opening
        // page, back / next on the right. The opening page is the group.
        var main = new StringBuilder("<div class=\"head\">");
        if (page.Group is not null && !page.IsGroupPage) {
            main.Append($"<p class=\"group\"><a href=\"../{page.Group.Page.Slug}/index.html\">")
                .Append(Escape(page.Group.Title))
                .Append("</a></p>");
        }
        main.Append(Pager(guide, index, "pager")).Append("</div>\n")
            .Append("<h1>").Append(Escape(page.Title)).Append("</h1>\n");
        // The page's own sections, as a table of contents under the title.
        var toc = page.Sections.Where(s => s.Level == 4).ToList();
        if (toc.Count > 0) {
            main.Append($"<nav class=\"toc\" aria-label=\"{_language.TocLabel}\"><div class=\"toc-title\">{_language.Toc}</div><ul>")
                .AppendJoin("", toc.Select(s => $"<li><a href=\"#{s.Anchor}\">{Escape(s.Title)}</a></li>"))
                .Append("</ul></nav>\n");
        }

        using var writer = new StringWriter();
        var renderer = new HtmlRenderer(writer);
        _pipeline.Setup(renderer);
        foreach (Block block in page.Blocks) {
            if (block is HtmlBlock html && _screenshot.Match(html.Lines.ToString()) is { Success: true } shot) {
                _screenshots++;
                if (_debug) {
                    renderer.WriteLine($"<p class=\"shot-todo\">{_language.Screenshot}: {Escape(shot.Groups[1].Value)}</p>");
                }

                continue;
            }
            if (block is HtmlBlock sized && _imageTag.Match(sized.Lines.ToString()) is { Success: true } tag) {
                renderer.WriteLine(SizedPicture(tag.Groups[1].Value, $"{_language.GuideSource}:{sized.Line + 1}"));

                continue;
            }
            // A paragraph that opens with a bold phrase starts a topic of its
            // own; the stylesheet gives it air above.
            if (block is ParagraphBlock { Inline.FirstChild: EmphasisInline { DelimiterCount: 2 } } topic) {
                topic.GetAttributes().AddClass("topic");
            }
            renderer.Render(block);
        }
        if (notes.Count > 0) {
            // The ids are the ones the references and the ways back carry
            // (fn:N, fnref:N); the numbers run through the whole guide, as
            // they do on GitHub.
            renderer.WriteLine("<ol class=\"notes\">");
            foreach (Footnote note in notes) {
                renderer.Write($"<li id=\"fn:{note.Order}\" value=\"{note.Order}\">");
                renderer.WriteChildren(note);
                renderer.WriteLine("</li>");
            }
            renderer.WriteLine("</ol>");
        }
        writer.Flush();

        // Back / next once more under the text: a narrow screen has no room
        // for them beside the group, and the stylesheet shows only this one.
        return main.Append(writer.ToString()).Append(Pager(guide, index, "pager end")).ToString();
    }

    /// <summary>
    /// What a search result shows under the page's title: the page's opening
    /// - its paragraphs and list items before the first section, tables
    /// aside - cut to about <see cref="DescriptionLength"/> characters at
    /// the end of a sentence when one falls in the second half, else at a
    /// word with an ellipsis. A page that opens with no text has nothing to
    /// show, and the guide's rules want an opening anyway.
    /// </summary>
    private string Description(GuidePage page) {
        var opening = new StringBuilder();
        foreach (Block block in page.Blocks.TakeWhile(b => b is not HeadingBlock)) {
            if (block is Table) {
                continue;
            }
            foreach (ParagraphBlock paragraph in Paragraphs(block)) {
                opening.Append(Guide.PlainText(paragraph.Inline)).Append(' ');
            }
            if (opening.Length > DescriptionLength) {
                break;
            }
        }

        string text = Regex.Replace(opening.ToString(), @"\s+", " ").Trim();
        if (text.Length == 0) {
            Errors.Add($"{_language.GuideSource}:{page.Heading.Line + 1}: the page opens with no text - a search result shows its first sentences");

            return page.Title;
        }
        if (text.Length <= DescriptionLength) {
            return text;
        }

        // A sentence ends where its mark is followed by a capital: "(см."
        // before a link is not an end.
        var ends = Regex.Matches(text, @"[.!?](?=\s\p{Lu})")
            .Select(m => m.Index + 1)
            .Where(end => end <= DescriptionLength)
            .ToList();
        if (ends.Count > 0 && ends[^1] >= DescriptionLength / 2) {
            return text[..ends[^1]];
        }
        int cut = text.LastIndexOf(' ', DescriptionLength - 1);
        // Not on a dangling "(см." or a separator.
        string head = Regex.Replace(text[..(cut > 0 ? cut : DescriptionLength - 1)], @"(\s*\([^)]*|[\s,;:—-]+)$", "");

        return head + "…";
    }

    /// <summary>The paragraphs of a block: itself, or those of its items and cells.</summary>
    private static IEnumerable<ParagraphBlock> Paragraphs(Block block) {
        return block switch {
            ParagraphBlock paragraph => new[] { paragraph },
            ContainerBlock container => container.Descendants<ParagraphBlock>(),
            _ => Enumerable.Empty<ParagraphBlock>(),
        };
    }

    private static string Pager(Guide guide, int index, string classes) {
        var pager = new StringBuilder($"<nav class=\"{classes}\">");
        if (index > 0) {
            GuidePage previous = guide.Pages[index - 1];
            pager.Append($"<a rel=\"prev\" href=\"../{previous.Slug}/index.html\">← {Escape(previous.Title)}</a>");
        }
        if (index + 1 < guide.Pages.Count) {
            GuidePage next = guide.Pages[index + 1];
            pager.Append($"<a rel=\"next\" href=\"../{next.Slug}/index.html\">{Escape(next.Title)} →</a>");
        }

        return pager.Append("</nav>").ToString();
    }

    private string RenderVersions(List<Release> releases) {
        Language language = _language;
        var main = new StringBuilder()
            .Append($"<h1>{language.Versions}</h1>\n")
            .Append($"<p>{language.VersionsIntro}")
            .Append($"<a href=\"{Repository}/blob/master/{language.Changelog}\">CHANGELOG</a>.</p>\n")
            .Append("<table>\n<thead><tr>")
            .AppendJoin("", language.VersionColumns.Select(column => $"<th>{column}</th>"))
            .Append("</tr></thead>\n<tbody>\n");
        foreach (Release release in releases) {
            string date = DateOnly.ParseExact(release.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture)
                .ToString(language.DateFormat, CultureInfo.InvariantCulture);
            main.Append($"<tr><td><b>{release.Version}</b></td><td>{date}</td>")
                .Append($"<td><a href=\"{Repository}/releases/tag/{release.Tag}\">{language.Download}</a></td><td>");
            // A release older than this language's guide offers the Russian one, saying so.
            if (release.Guides.Contains(language.GuideSource)) {
                main.Append($"<a href=\"{Repository}/blob/{release.Tag}/{language.GuideSource}\">{language.Open}</a>");
            } else if (release.Guides.Contains(Language.Russian.GuideSource)) {
                main.Append($"<a href=\"{Repository}/blob/{release.Tag}/{Language.Russian.GuideSource}\">{language.OtherGuide}</a>");
            }
            main.Append("</td></tr>\n");
        }

        return main.Append("</tbody>\n</table>").ToString();
    }

    /// <summary>
    /// Groups and pages in file order; <paramref name="prefix"/> leads from
    /// the page to guide/. A group folds (details, no script) under its title,
    /// which is a link to the group's opening page - a click on the link
    /// follows it, one beside it folds. Only the group of the open page
    /// starts unfolded, since nothing carries what the reader folded over to
    /// the next page.
    /// </summary>
    private static string Nav(Guide guide, GuidePage? current, string prefix) {
        var nav = new StringBuilder("<ul>\n");
        GuideGroup? group = null;
        foreach (GuidePage page in guide.Pages) {
            if (!ReferenceEquals(page.Group, group)) {
                if (group is not null) {
                    nav.Append("</ul></details></li>\n");
                }
                group = page.Group;
                // A group starts with its opening page, the ## above its
                // pages: that page is the group's title, not a row under it.
                if (page.IsGroupPage) {
                    bool open = ReferenceEquals(current?.Group, group);
                    nav.Append(open ? "<li><details open><summary>" : "<li><details><summary>")
                        .Append(NavLink(page, current, prefix))
                        .Append("</summary><ul>\n");

                    continue;
                }
            }
            nav.Append("<li>").Append(NavLink(page, current, prefix)).Append("</li>\n");
        }
        if (group is not null) {
            nav.Append("</ul></details></li>\n");
        }

        return nav.Append("</ul>").ToString();
    }

    private static string NavLink(GuidePage page, GuidePage? current, string prefix) {
        string here = page == current ? " aria-current=\"page\"" : "";

        return $"<a href=\"{prefix}{page.Slug}/index.html\"{here}>{Escape(page.Title)}</a>";
    }

    /// <summary>
    /// Markdig's Descendants of a leaf block (a paragraph) finds nothing: the
    /// inlines hang off the block's Inline, not off the block.
    /// </summary>
    private static IEnumerable<T> Inlines<T>(Block block) where T : Inline {
        return block switch {
            LeafBlock { Inline: not null } leaf => leaf.Inline.Descendants<T>(),
            ContainerBlock container => container.Descendants<T>(),
            _ => Enumerable.Empty<T>(),
        };
    }

    /// <summary>
    /// A link as GUIDE.md has it -> as the site needs it: a GitHub anchor to
    /// the page and section it landed in, a picture to img/, any other file of
    /// the repository to GitHub. Anything that leads nowhere is an error.
    /// </summary>
    private string Rewrite(LinkInline link, GuidePage page, Guide guide) {
        string url = link.Url ?? "";
        string where = $"{_language.GuideSource}:{link.Line + 1}";
        if (url.StartsWith('#')) {
            return Anchor(url[1..], page, guide, where);
        }
        if (_scheme.IsMatch(url)) {
            return url;
        }

        int hash = url.IndexOf('#', StringComparison.Ordinal);
        string fragment = hash < 0 ? "" : url[hash..];
        string full = Path.GetFullPath(Path.Combine(_root, "docs", Uri.UnescapeDataString(hash < 0 ? url : url[..hash])));
        if (link.IsImage) {
            // The page's own language of a screenshot, when it has one.
            full = Localized(full);
        }
        string relative = Path.GetRelativePath(_root, full).Replace('\\', '/');
        if (relative.StartsWith("../", StringComparison.Ordinal) || Path.IsPathRooted(relative) || !Path.Exists(full)) {
            Errors.Add($"{where}: broken link {url}");

            return url;
        }
        if (relative == _language.GuideSource) {
            return fragment.Length > 1 ? Anchor(fragment[1..], page, guide, where) : $"../{guide.Pages[0].Slug}/index.html";
        }
        if (link.IsImage) {
            return Picture(link, full, relative, where);
        }

        return $"{Repository}/{(Directory.Exists(full) ? "tree" : "blob")}/master/{relative}{fragment}";
    }

    private string Anchor(string anchor, GuidePage from, Guide guide, string where) {
        if (!guide.Anchors.TryGetValue(Uri.UnescapeDataString(anchor), out LinkTarget? target)) {
            Errors.Add($"{where}: no heading for #{anchor}");

            return "#" + anchor;
        }
        if (target.Page == from && target.Anchor is not null) {
            return "#" + target.Anchor;
        }

        return $"../{target.Page.Slug}/index.html" + (target.Anchor is null ? "" : "#" + target.Anchor);
    }

    /// <summary>
    /// A picture keeps its file name under img/ and gets its size, so the
    /// page does not jump when it arrives. A screenshot is shown shrunk in
    /// the column and opens full size over the page (<see cref="Zoomable"/>) -
    /// unless the column shows it whole already (<see cref="Zooms"/>). An
    /// icon of the interface (docs/icons, redrawn from the app's XAML) sits
    /// in the line of text and opens nothing.
    /// </summary>
    private string Picture(LinkInline link, string full, string relative, string where) {
        var attributes = link.GetAttributes();
        if (relative.StartsWith("docs/icons/", StringComparison.Ordinal) && relative.EndsWith(".svg", StringComparison.Ordinal)) {
            if (Svg.Size(full) is not { } icon) {
                Errors.Add($"{where}: {relative} has no width and height on its <svg>");

                return link.Url ?? "";
            }

            attributes.AddClass("icon");
            attributes.AddProperty("width", icon.Width.ToString(CultureInfo.InvariantCulture));
            attributes.AddProperty("height", icon.Height.ToString(CultureInfo.InvariantCulture));

            return ImageFolder + "icons/" + Path.GetFileName(full);
        }
        if (Screenshot(full, relative, where) is not { } size) {
            return link.Url ?? "";
        }

        string url = ImageFolder + Path.GetFileName(full);
        if (link.Parent is LinkInline || !Zooms(size.Width, size.Width)) {
            // Inside a link of its own, that link is what a click does; a
            // small picture has nothing more to open.
            attributes.AddProperty("width", size.Width.ToString(CultureInfo.InvariantCulture));
            attributes.AddProperty("height", size.Height.ToString(CultureInfo.InvariantCulture));
            attributes.AddProperty("loading", "lazy");

            return url;
        }

        // The alt is in the HTML already: the text inside the brackets must
        // not follow the picture into the paragraph.
        link.ReplaceBy(new HtmlInline(Zoomable(url, AltText(link), size, size)), copyChildren: false);

        return url;
    }

    /// <summary>
    /// A screenshot as a button that opens it full size over the page: a
    /// popover, so no script - a click beside the picture or on it, or Esc,
    /// closes it, and the page stays where it was. A browser without
    /// popovers shows the picture in the column and nothing more.
    /// </summary>
    private string Zoomable(string url, string alt, (int Width, int Height) shown, (int Width, int Height) full) {
        string id = $"shot-{++_shotsOnPage}";

        return $"<button type=\"button\" class=\"zoom\" popovertarget=\"{id}\">"
            + $"<img src=\"{url}\" alt=\"{Escape(alt)}\" width=\"{shown.Width}\" height=\"{shown.Height}\" loading=\"lazy\"></button>"
            + $"<span class=\"full\" id=\"{id}\" popover>"
            + $"<button type=\"button\" popovertarget=\"{id}\" popovertargetaction=\"hide\" aria-label=\"{_language.Close}\">"
            + $"<img src=\"{url}\" alt=\"\" width=\"{full.Width}\" height=\"{full.Height}\" loading=\"lazy\"></button></span>";
    }

    /// <summary>
    /// Whether the full size is worth opening: at least a tenth wider than
    /// the picture as the column shows it. A click that opens the same
    /// picture again looks broken.
    /// </summary>
    private static bool Zooms(int shown, int full) {
        return full * 10 > Math.Min(shown, ColumnWidth) * 11;
    }

    /// <summary>A markdown picture's alt: the text inside its brackets.</summary>
    private static string AltText(LinkInline link) {
        return string.Concat(link.Descendants<LiteralInline>().Select(literal => literal.Content.ToString()));
    }

    /// <summary>
    /// The HTML of a picture GUIDE.md sizes or floats itself
    /// (<see cref="_imageTag"/>): src and alt as a markdown picture has them,
    /// width in pixels - the height follows the picture - and align="right"
    /// to float it beside the text. Anything else in the tag is an error: the
    /// same line has to mean the same on GitHub.
    /// </summary>
    private string SizedPicture(string tag, string where) {
        var attributes = _attribute.Matches(tag)
            .ToDictionary(m => m.Groups[1].Value, m => WebUtility.HtmlDecode(m.Groups[2].Value), StringComparer.Ordinal);
        bool known = attributes.Keys.All(key => key is "src" or "alt" or "width" or "align");
        if (!known || _attribute.Replace(tag, "").Trim().Length > 0
            || !attributes.TryGetValue("src", out string? src) || !attributes.TryGetValue("alt", out string? alt) || alt.Length == 0) {
            Errors.Add($"{where}: <img> takes src, alt, width and align=\"right\", nothing else");

            return "";
        }

        string full = Localized(Path.GetFullPath(Path.Combine(_root, "docs", Uri.UnescapeDataString(src))));
        string relative = Path.GetRelativePath(_root, full).Replace('\\', '/');
        if (Screenshot(full, relative, where) is not { } size) {
            return "";
        }

        int width = size.Width;
        if (attributes.TryGetValue("width", out string? asked)
            && !(int.TryParse(asked, NumberStyles.None, CultureInfo.InvariantCulture, out width) && width > 0)) {
            Errors.Add($"{where}: <img width=\"{asked}\"> - pixels, a whole number");

            return "";
        }
        bool right = attributes.TryGetValue("align", out string? align);
        if (right && align != "right") {
            Errors.Add($"{where}: <img align=\"{align}\"> - only \"right\" floats");

            return "";
        }

        string url = ImageFolder + Path.GetFileName(full);
        int height = (int)Math.Round((double)width * size.Height / size.Width);
        string picture = Zooms(width, size.Width)
            ? Zoomable(url, alt, (width, height), size)
            : $"<img src=\"{url}\" alt=\"{Escape(alt)}\" width=\"{width}\" height=\"{height}\" loading=\"lazy\">";

        // Floated, the picture is a box beside the text; otherwise it opens a
        // paragraph of its own, as a markdown picture does.
        return right
            ? $"<div class=\"right\">{picture}</div>"
            : $"<p>{picture}</p>";
    }

    /// <summary>
    /// The screenshot in the language being rendered (2026-10-09): a pair
    /// <c>x.ru.webp</c> / <c>x.en.webp</c> gives each language its own, a
    /// single <c>x.webp</c> serves both. The text may name the picture with
    /// its language or without - the page gets its own language's file; none
    /// found leaves the path as it was, for the link check to report. Other
    /// files pass through. A shared one is noted for the summary: before a
    /// release it is the list of pictures that may still show the other
    /// language's interface.
    /// </summary>
    private string Localized(string full) {
        string folder = Path.Combine(_root, "docs", "screenshots");
        if (!string.Equals(Path.GetDirectoryName(full), folder, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(Path.GetExtension(full), ".webp", StringComparison.OrdinalIgnoreCase)) {
            return full;
        }

        string name = Path.GetFileNameWithoutExtension(full);
        string? suffix = Language.All.Select(l => "." + l.Code).FirstOrDefault(s => name.EndsWith(s, StringComparison.Ordinal));
        string bare = suffix is null ? name : name[..^suffix.Length];
        string own = Path.Combine(folder, $"{bare}.{_language.Code}.webp");
        if (File.Exists(own)) {
            return own;
        }

        string shared = Path.Combine(folder, bare + ".webp");
        string found = File.Exists(shared) ? shared : full;
        if (File.Exists(found) && Path.GetFileName(found) != AppIcon) {
            string file = Path.GetFileName(found);
            if (!_sharedShots.TryGetValue(file, out var languages)) {
                _sharedShots[file] = languages = new HashSet<Language>();
            }
            languages.Add(_language);
        }

        return found;
    }

    /// <summary>A landing's pictures (<c>img/x.webp</c> in its HTML) in the landing's language - see <see cref="Localized"/>.</summary>
    private string LocalizedPictures(string html) {
        return _landingPicture.Replace(html, m =>
            m.Groups[1].Value + Path.GetFileName(Localized(Path.Combine(_root, "docs", "screenshots", m.Groups[2].Value))) + "\"");
    }

    /// <summary>The size of a screenshot; anything but docs/screenshots/*.webp is an error, and null.</summary>
    private (int Width, int Height)? Screenshot(string full, string relative, string where) {
        var size = File.Exists(full) ? Webp.Size(full) : null;
        if (size is null || !relative.StartsWith("docs/screenshots/", StringComparison.Ordinal)) {
            Errors.Add($"{where}: pictures are docs/screenshots/*.webp and docs/icons/*.svg, not {relative}");

            return null;
        }

        return size;
    }

    /// <summary>
    /// Every relative href and src of every page leads to a file of the site,
    /// and every #fragment to an id there: the landing's hand-written links
    /// and the template's are checked the same way as the guide's.
    /// </summary>
    private void CheckLinks() {
        var ids = _pages.ToDictionary(
            p => p.Key,
            p => _id.Matches(p.Value).Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal),
            StringComparer.Ordinal);
        foreach (var (path, html) in _pages) {
            foreach (Match match in _link.Matches(html)) {
                string value = WebUtility.HtmlDecode(match.Groups[1].Value);
                if (_scheme.IsMatch(value)) {
                    continue;
                }

                int hash = value.IndexOf('#', StringComparison.Ordinal);
                string fragment = hash < 0 ? "" : value[(hash + 1)..];
                string? target = Resolve(path, hash < 0 ? value : value[..hash]);
                if (target is null || !(_pages.ContainsKey(target) || _files.ContainsKey(target))) {
                    Errors.Add($"{path}: broken link {value}");
                } else if (fragment.Length > 0 && !(ids.TryGetValue(target, out var there) && there.Contains(fragment))) {
                    Errors.Add($"{path}: no #{fragment} in {target}");
                }
            }
        }
    }

    /// <summary>
    /// F1 on a settings page opens the site's guide at the page the settings
    /// page names (<c>guide: "slug"</c> or <c>"slug#section"</c>, and
    /// <c>guideEn:</c> the same in the English guide). A heading renamed in
    /// a guide renames the page, and F1 would land on a 404, so every such
    /// target has to be a built page, and its section an id on it - and
    /// there have to be some in each language: a rename of the argument must
    /// not pass as "nothing to check".
    /// </summary>
    private void CheckSettingsPages() {
        string[] lines = Read(SettingsPagesSource).Split('\n');
        foreach (var (language, argument) in _guideArgument) {
            int found = 0;
            for (int i = 0; i < lines.Length; i++) {
                foreach (Match match in argument.Matches(lines[i])) {
                    found++;
                    string target = match.Groups[1].Value;
                    int hash = target.IndexOf('#', StringComparison.Ordinal);
                    string page = $"{language.Folder}guide/{(hash < 0 ? target : target[..hash])}/index.html";
                    string section = hash < 0 ? "" : target[(hash + 1)..];
                    string where = $"{SettingsPagesSource}:{i + 1}: F1 opens {target}";
                    if (!_pages.TryGetValue(page, out string? html)) {
                        Errors.Add($"{where}, no {page} in the site");
                    } else if (section.Length > 0 && !_id.Matches(html).Any(id => id.Groups[1].Value == section)) {
                        Errors.Add($"{where}, no #{section} in {page}");
                    }
                }
            }
            if (found == 0) {
                Errors.Add($"{SettingsPagesSource}: no {argument} targets found - the settings pages' F1 is not checked in {language.Code}");
            }
        }
    }

    /// <summary>A link on the page at <paramref name="from"/> as a site path; null when it leaves the site.</summary>
    private static string? Resolve(string from, string link) {
        if (link.Length == 0) {
            return from;
        }
        if (link.StartsWith('/')) {
            return null;
        }

        var parts = from.Split('/').SkipLast(1).ToList();
        foreach (string part in link.Split('/')) {
            if (part == "..") {
                if (parts.Count == 0) {
                    return null;
                }
                parts.RemoveAt(parts.Count - 1);
            } else if (part != "." && part.Length > 0) {
                parts.Add(part);
            }
        }
        if (link.EndsWith('/')) {
            parts.Add("index.html");
        }

        return string.Join('/', parts);
    }

    private string Fill(string text, string source, Dictionary<string, string> values) {
        return _placeholder.Replace(text, match => {
            if (values.TryGetValue(match.Groups[1].Value, out string? value)) {
                return value;
            }

            Errors.Add($"{source}: unknown placeholder {match.Value}");

            return match.Value;
        });
    }

    private string Read(string relative) {
        return File.ReadAllText(Path.Combine(_root, relative));
    }

    /// <summary>Comments out, runs of whitespace to one space, none around braces and separators.</summary>
    private static string Minify(string css) {
        css = Regex.Replace(css, @"/\*.*?\*/", "", RegexOptions.Singleline);
        css = Regex.Replace(css, @"\s+", " ");

        return Regex.Replace(css, @" ?([{};,>]) ?", "$1").Trim();
    }

    private static string Escape(string text) {
        return text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
    }

    private static string Kb(long bytes) {
        return $"{(bytes + 512) / 1024} KB";
    }

    private static string Over(bool outside) {
        return outside ? " - OUT OF BUDGET" : "";
    }
}
