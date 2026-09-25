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
/// </summary>
internal sealed class SiteBuilder {
    public const string Repository = "https://github.com/lekta/wander";

    private const string GuideSource = "docs/GUIDE.md";
    private const string LandingSource = "docs/site/index.html";
    private const string TemplateSource = "tools/site/page.html";

    private static readonly Regex _scheme = new("^[a-z][a-z0-9+.-]*:", RegexOptions.IgnoreCase);
    private static readonly Regex _placeholder = new(@"\{\{([a-z]+)\}\}");
    private static readonly Regex _link = new(@"\s(?:href|src)=""([^""]*)""");
    private static readonly Regex _id = new(@"\sid=""([^""]*)""");
    private static readonly UTF8Encoding _utf8 = new(encoderShouldEmitUTF8Identifier: false);

    private readonly string _root;
    private readonly MarkdownPipeline _pipeline = new MarkdownPipelineBuilder().UsePipeTables().Build();

    /// <summary>Site path (relative, forward slashes) -> the page.</summary>
    private readonly Dictionary<string, string> _pages = new(StringComparer.Ordinal);

    /// <summary>Site path -> the file copied there.</summary>
    private readonly Dictionary<string, string> _files = new(StringComparer.Ordinal);

    private Release? _latest;


    public SiteBuilder(string root) {
        _root = root;
    }


    public List<string> Errors { get; } = new();


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
        var guidePages = _pages.Where(p => p.Key.StartsWith("guide/", StringComparison.Ordinal)).ToList();
        var largest = guidePages.MaxBy(p => _utf8.GetByteCount(p.Value));
        long pictures = _files.Values.Sum(file => new FileInfo(file).Length);

        yield return $"{guidePages.Count} guide pages, largest {Kb(_utf8.GetByteCount(largest.Value))} ({largest.Key})";
        yield return $"landing {Kb(_utf8.GetByteCount(_pages["index.html"]))}, pictures {Kb(pictures)}; download {_latest!.Tag}";
    }


    private string RenderPage(Guide guide, int index) {
        GuidePage page = guide.Pages[index];
        foreach (Block block in page.Blocks) {
            foreach (LinkInline link in Links(block)) {
                link.Url = Rewrite(link, page, guide);
            }
        }

        // Above the title: the group on the left, back / next on the right.
        var main = new StringBuilder("<div class=\"head\">");
        if (page.Group is not null) {
            main.Append("<p class=\"group\">").Append(Escape(page.Group.Title)).Append("</p>");
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
    /// the page to guide/. A group folds (details, no script); only the group
    /// of the open page starts unfolded, since nothing carries what the reader
    /// folded over to the next page.
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
                if (group is not null) {
                    bool open = ReferenceEquals(current?.Group, group);
                    nav.Append(open ? "<li><details open><summary>" : "<li><details><summary>")
                        .Append(Escape(group.Title))
                        .Append("</summary><ul>\n");
                }
            }
            nav.Append($"<li><a href=\"{prefix}{page.Slug}/index.html\"");
            if (page == current) {
                nav.Append(" aria-current=\"page\"");
            }
            nav.Append('>').Append(Escape(page.Title)).Append("</a></li>\n");
        }
        if (group is not null) {
            nav.Append("</ul></details></li>\n");
        }

        return nav.Append("</ul>").ToString();
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

    /// <summary>A picture keeps its file name under img/ and gets its size, so the page does not jump when it arrives.</summary>
    private string Picture(LinkInline link, string full, string relative, string where) {
        var size = File.Exists(full) ? Webp.Size(full) : null;
        if (size is null || !relative.StartsWith("docs/screenshots/", StringComparison.Ordinal)) {
            Errors.Add($"{where}: pictures are docs/screenshots/*.webp, not {relative}");

            return link.Url ?? "";
        }

        var attributes = link.GetAttributes();
        attributes.AddProperty("width", size.Value.Width.ToString(CultureInfo.InvariantCulture));
        attributes.AddProperty("height", size.Value.Height.ToString(CultureInfo.InvariantCulture));
        attributes.AddProperty("loading", "lazy");

        return "../../img/" + Path.GetFileName(full);
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
