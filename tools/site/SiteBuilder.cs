using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Markdig;
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

    private const string GuideSource = "docs/GUIDE.md";
    private const string LandingSource = "docs/site/index.html";
    private const string TemplateSource = "tools/site/page.html";

    /// <summary>Where the settings pages name the guide section F1 opens.</summary>
    private const string SettingsPagesSource = "src/Wander.App/ViewModels/SettingsCategoryViewModel.cs";

    private static readonly Regex _guideArgument = new(@"\bguide: ""([^""]*)""");

    /// <summary>
    /// A place in GUIDE.md that wants a screenshot: an HTML comment of its
    /// own, invisible on GitHub, dropped from the published site.
    /// </summary>
    private static readonly Regex _screenshot = new(@"^<!--\s*скрин:\s*(.*?)\s*-->\s*$", RegexOptions.Singleline);

    /// <summary>
    /// A picture GUIDE.md sizes or floats itself: a line of its own holding
    /// <c>&lt;img src alt [width] [align="right"]&gt;</c>, which GitHub
    /// renders as it is.
    /// </summary>
    private static readonly Regex _imageTag = new(@"^<img\s+([^>]*?)\s*/?>\s*$", RegexOptions.Singleline);

    private static readonly Regex _attribute = new(@"([a-z]+)=""([^""]*)""");
    private static readonly Regex _scheme = new("^[a-z][a-z0-9+.-]*:", RegexOptions.IgnoreCase);
    private static readonly Regex _placeholder = new(@"\{\{([a-z]+)\}\}");
    private static readonly Regex _link = new(@"\s(?:href|src)=""([^""]*)""");
    private static readonly Regex _id = new(@"\sid=""([^""]*)""");
    private static readonly UTF8Encoding _utf8 = new(encoderShouldEmitUTF8Identifier: false);

    private readonly string _root;
    private readonly bool _debug;
    private readonly MarkdownPipeline _pipeline = new MarkdownPipelineBuilder().UsePipeTables().Build();

    /// <summary>Site path (relative, forward slashes) -> the page.</summary>
    private readonly Dictionary<string, string> _pages = new(StringComparer.Ordinal);

    /// <summary>Site path -> the file copied there.</summary>
    private readonly Dictionary<string, string> _files = new(StringComparer.Ordinal);

    private Release? _latest;
    private int _screenshots;


    public SiteBuilder(string root, bool debug) {
        _root = root;
        _debug = debug;
    }


    public List<string> Errors { get; } = new();

    /// <summary>There is a site to write, problems or not: no guide pages or no release leave nothing.</summary>
    public bool IsBuilt => _pages.ContainsKey("index.html");


    public void Build() {
        string css = Minify(Read("tools/site/site.css"));
        string template = Read(TemplateSource);
        var guide = Guide.Parse(Markdown.Parse(Read(GuideSource), _pipeline), GuideSource, Errors);
        var releases = Releases.Read(_root, Errors);
        if (guide.Pages.Count == 0 || releases.Count == 0) {
            return;
        }

        _latest = releases[0];
        foreach (string picture in Directory.GetFiles(Path.Combine(_root, "docs", "screenshots"), "*.webp")) {
            _files["img/" + Path.GetFileName(picture)] = picture;
        }
        foreach (string icon in Directory.GetFiles(Path.Combine(_root, "docs", "icons"), "*.svg")) {
            _files["img/icons/" + Path.GetFileName(icon)] = icon;
        }

        string home = $"guide/{guide.Pages[0].Slug}/index.html";
        for (int i = 0; i < guide.Pages.Count; i++) {
            GuidePage page = guide.Pages[i];
            _pages[$"guide/{page.Slug}/index.html"] = Fill(template, TemplateSource, new() {
                ["title"] = Escape(page.Title) + " | Wander",
                ["css"] = css,
                ["root"] = "../../",
                ["guide"] = home,
                ["nav"] = Nav(guide, page, "../"),
                ["main"] = RenderPage(guide, i),
            });
        }
        // What the app's "Помощь" opens (CrashReporter.GuideUrl): the guide
        // has no page of its own at guide/, so this one sends the reader on
        // to the first - a refresh and a link, no script.
        string first = $"{guide.Pages[0].Slug}/index.html";
        _pages["guide/index.html"] =
            "<!doctype html>\n<html lang=\"ru\">\n<head>\n<meta charset=\"utf-8\">\n" +
            $"<meta http-equiv=\"refresh\" content=\"0; url={first}\">\n<title>Руководство | Wander</title>\n</head>\n" +
            $"<body><a href=\"{first}\">Руководство</a></body>\n</html>\n";
        _pages["versions/index.html"] = Fill(template, TemplateSource, new() {
            ["title"] = "Версии | Wander",
            ["css"] = css,
            ["root"] = "../",
            ["guide"] = home,
            ["nav"] = Nav(guide, null, "../guide/"),
            ["main"] = RenderVersions(releases),
        });
        _pages["index.html"] = Fill(Read(LandingSource), LandingSource, new() {
            ["css"] = css,
            ["guide"] = home,
            ["version"] = _latest.Version,
            ["download"] = $"{Repository}/releases/download/{_latest.Tag}/Wander.exe",
            ["release"] = $"{Repository}/releases/tag/{_latest.Tag}",
        });

        // After the sources are clean: a link GUIDE.md got wrong is already
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
        foreach (string owned in new[] { "guide", "versions", "img" }) {
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
        // guide/index.html only forwards to the first page.
        var guidePages = _pages
            .Where(p => p.Key.StartsWith("guide/", StringComparison.Ordinal) && p.Key != "guide/index.html")
            .ToList();
        var largest = guidePages.MaxBy(p => _utf8.GetByteCount(p.Value));
        long pictures = _files.Values.Sum(file => new FileInfo(file).Length);

        yield return $"{guidePages.Count} guide pages, largest {Kb(_utf8.GetByteCount(largest.Value))} ({largest.Key})";
        yield return $"landing {Kb(_utf8.GetByteCount(_pages["index.html"]))}, pictures {Kb(pictures)}; download {_latest!.Tag}";
        if (_screenshots > 0) {
            yield return $"{_screenshots} place(s) marked for a screenshot" + (_debug ? ", shown" : ", shown with --debug");
        }
    }


    private string RenderPage(Guide guide, int index) {
        GuidePage page = guide.Pages[index];
        foreach (Block block in page.Blocks) {
            // A list, not the live walk: a screenshot gets wrapped in a link
            // on the way, which changes the tree being walked.
            foreach (LinkInline link in Links(block).ToList()) {
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
        main.Append("<nav class=\"pager\">");
        if (index > 0) {
            GuidePage previous = guide.Pages[index - 1];
            main.Append($"<a rel=\"prev\" href=\"../{previous.Slug}/index.html\">← {Escape(previous.Title)}</a>");
        }
        if (index + 1 < guide.Pages.Count) {
            GuidePage next = guide.Pages[index + 1];
            main.Append($"<a rel=\"next\" href=\"../{next.Slug}/index.html\">{Escape(next.Title)} →</a>");
        }
        main.Append("</nav></div>\n")
            .Append("<h1>").Append(Escape(page.Title)).Append("</h1>\n");
        var toc = page.Sections.Where(s => s.Level == 4).ToList();
        if (toc.Count > 0) {
            main.Append("<p class=\"toc\"><span>На странице:</span>")
                .AppendJoin("", toc.Select(s => $"<a href=\"#{s.Anchor}\">{Escape(s.Title)}</a>"))
                .Append("</p>\n");
        }

        using var writer = new StringWriter();
        var renderer = new HtmlRenderer(writer);
        _pipeline.Setup(renderer);
        foreach (Block block in page.Blocks) {
            if (block is HtmlBlock html && _screenshot.Match(html.Lines.ToString()) is { Success: true } shot) {
                _screenshots++;
                if (_debug) {
                    renderer.WriteLine($"<p class=\"shot-todo\">Скриншот: {Escape(shot.Groups[1].Value)}</p>");
                }

                continue;
            }
            if (block is HtmlBlock sized && _imageTag.Match(sized.Lines.ToString()) is { Success: true } tag) {
                renderer.WriteLine(SizedPicture(tag.Groups[1].Value, $"{GuideSource}:{sized.Line + 1}"));

                continue;
            }
            // A paragraph that opens with a bold phrase starts a topic of its
            // own; the stylesheet gives it air above.
            if (block is ParagraphBlock { Inline.FirstChild: EmphasisInline { DelimiterCount: 2 } } topic) {
                topic.GetAttributes().AddClass("topic");
            }
            renderer.Render(block);
        }
        writer.Flush();

        return main.Append(writer.ToString()).ToString();
    }

    private static string RenderVersions(List<Release> releases) {
        var main = new StringBuilder()
            .Append("<h1>Версии</h1>\n")
            .Append("<p>Руководство на сайте описывает текущую разработку и может опережать последний релиз. ")
            .Append("Руководство выпущенной версии открывается из её строки, список изменений в ")
            .Append($"<a href=\"{Repository}/blob/master/docs/CHANGELOG.md\">CHANGELOG</a>.</p>\n")
            .Append("<table>\n<thead><tr><th>Версия</th><th>Дата</th><th>Релиз</th><th>Руководство</th></tr></thead>\n<tbody>\n");
        foreach (Release release in releases) {
            string date = DateOnly.ParseExact(release.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture)
                .ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
            main.Append($"<tr><td><b>{release.Version}</b></td><td>{date}</td>")
                .Append($"<td><a href=\"{Repository}/releases/tag/{release.Tag}\">скачать</a></td><td>");
            if (release.HasGuide) {
                main.Append($"<a href=\"{Repository}/blob/{release.Tag}/docs/GUIDE.md\">открыть</a>");
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
    private static IEnumerable<LinkInline> Links(Block block) {
        return block switch {
            LeafBlock { Inline: not null } leaf => leaf.Inline.Descendants<LinkInline>(),
            ContainerBlock container => container.Descendants<LinkInline>(),
            _ => Enumerable.Empty<LinkInline>(),
        };
    }

    /// <summary>
    /// A link as GUIDE.md has it -> as the site needs it: a GitHub anchor to
    /// the page and section it landed in, a picture to img/, any other file of
    /// the repository to GitHub. Anything that leads nowhere is an error.
    /// </summary>
    private string Rewrite(LinkInline link, GuidePage page, Guide guide) {
        string url = link.Url ?? "";
        string where = $"{GuideSource}:{link.Line + 1}";
        if (url.StartsWith('#')) {
            return Anchor(url[1..], page, guide, where);
        }
        if (_scheme.IsMatch(url)) {
            return url;
        }

        int hash = url.IndexOf('#', StringComparison.Ordinal);
        string fragment = hash < 0 ? "" : url[hash..];
        string full = Path.GetFullPath(Path.Combine(_root, "docs", Uri.UnescapeDataString(hash < 0 ? url : url[..hash])));
        string relative = Path.GetRelativePath(_root, full).Replace('\\', '/');
        if (relative.StartsWith("../", StringComparison.Ordinal) || Path.IsPathRooted(relative) || !Path.Exists(full)) {
            Errors.Add($"{where}: broken link {url}");

            return url;
        }
        if (relative == GuideSource) {
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
    /// page does not jump when it arrives. A screenshot is also a link to its
    /// own file: the column shows it shrunk, a click opens it full size - no
    /// script, Back returns. An icon of the interface (docs/icons, redrawn
    /// from the app's XAML) sits in the line of text and opens nothing.
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

            return "../../img/icons/" + Path.GetFileName(full);
        }
        if (Screenshot(full, relative, where) is not { } size) {
            return link.Url ?? "";
        }

        string url = "../../img/" + Path.GetFileName(full);
        attributes.AddProperty("width", size.Width.ToString(CultureInfo.InvariantCulture));
        attributes.AddProperty("height", size.Height.ToString(CultureInfo.InvariantCulture));
        attributes.AddProperty("loading", "lazy");
        if (link.Parent is not LinkInline) {
            var zoom = new LinkInline(url, "");
            zoom.GetAttributes().AddClass("zoom");
            link.ReplaceBy(zoom, copyChildren: false);
            zoom.AppendChild(link);
        }

        return url;
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

        string full = Path.GetFullPath(Path.Combine(_root, "docs", Uri.UnescapeDataString(src)));
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

        string url = "../../img/" + Path.GetFileName(full);
        int height = (int)Math.Round((double)width * size.Height / size.Width);
        string picture = $"<img src=\"{url}\" alt=\"{Escape(alt)}\" width=\"{width}\" height=\"{height}\" loading=\"lazy\">";

        // Floated, the picture is a box beside the text; otherwise it opens a
        // paragraph of its own, as a markdown picture does.
        return right
            ? $"<a class=\"zoom right\" href=\"{url}\">{picture}</a>"
            : $"<p><a class=\"zoom\" href=\"{url}\">{picture}</a></p>";
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
    /// page names (<c>guide: "slug"</c> or <c>"slug#section"</c>). A heading
    /// renamed in GUIDE.md renames the page, and F1 would land on a 404, so
    /// every such target has to be a built page, and its section an id on
    /// it - and there have to be some: a rename of the argument must not
    /// pass as "nothing to check".
    /// </summary>
    private void CheckSettingsPages() {
        string[] lines = Read(SettingsPagesSource).Split('\n');
        int found = 0;
        for (int i = 0; i < lines.Length; i++) {
            foreach (Match match in _guideArgument.Matches(lines[i])) {
                found++;
                string target = match.Groups[1].Value;
                int hash = target.IndexOf('#', StringComparison.Ordinal);
                string page = $"guide/{(hash < 0 ? target : target[..hash])}/index.html";
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
            Errors.Add($"{SettingsPagesSource}: no guide: \"...\" targets found - the settings pages' F1 is not checked");
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
}
