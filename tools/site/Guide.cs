using System.Text;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Wander.Site;

/// <summary>
/// docs/GUIDE.md cut into pages. The structure of the file is the structure
/// of the site: <c>#</c> is the guide's title (its text is for GitHub and is
/// not published); a <c>##</c> without <c>###</c> is a page of its own; a
/// <c>##</c> with <c>###</c> is a group - a title in the navigation, no page,
/// no text of its own; <c>###</c> is a page; <c>####</c> are the page's
/// sections, listed at its top. Order is file order.
///
/// <para>
/// Links in the file are GitHub's (<c>#heading-anchor</c>, because that is
/// where the file is read too), so every heading's GitHub anchor is mapped
/// to the page and section it ended up in.
/// </para>
/// </summary>
internal sealed class Guide {
    private Guide() {
    }


    /// <summary>The pages in file order.</summary>
    public List<GuidePage> Pages { get; } = new();

    /// <summary>GitHub's anchor of every heading -> where it lives on the site.</summary>
    public Dictionary<string, LinkTarget> Anchors { get; } = new(StringComparer.Ordinal);


    public static Guide Parse(MarkdownDocument document, string source, List<string> errors) {
        var guide = new Guide();
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        string? titleAnchor = null;
        GuideGroup? group = null;
        GuidePage? page = null;
        // A ## is a page until a ### under it proves it a group.
        GuidePage? pending = null;
        string? pendingAnchor = null;

        foreach (Block block in document) {
            if (block is not HeadingBlock heading) {
                if (page is not null) {
                    page.Blocks.Add(block);
                } else if (titleAnchor is null) {
                    errors.Add($"{source}:{block.Line + 1}: text before the # title");
                }

                continue;
            }

            string title = PlainText(heading.Inline);
            string anchor = Slugs.GitHub(title, seen);
            string where = $"{source}:{heading.Line + 1}";
            if (heading.Level == 1) {
                if (titleAnchor is not null) {
                    errors.Add($"{where}: a second # title");
                }
                titleAnchor ??= anchor;

                continue;
            }
            if (titleAnchor is null) {
                errors.Add($"{where}: the guide starts with a # title");
                titleAnchor = "";
            }

            if (heading.Level == 2) {
                group = null;
                page = pending = new GuidePage(title, heading, null);
                pendingAnchor = anchor;
                guide.Pages.Add(page);
                guide.Anchors[anchor] = new LinkTarget(page, null);
            } else if (heading.Level == 3) {
                if (pending is not null) {
                    if (pending.Blocks.Count > 0) {
                        errors.Add($"{source}:{pending.Heading.Line + 1}: text under a group heading - a ## with ### inside has no page of its own");
                    }
                    guide.Pages.Remove(pending);
                    group = new GuideGroup(pending.Title);
                } else if (group is null) {
                    errors.Add($"{where}: ### outside a ## group");
                }

                page = new GuidePage(title, heading, group);
                guide.Pages.Add(page);
                guide.Anchors[anchor] = new LinkTarget(page, null);
                if (pending is not null) {
                    guide.Anchors[pendingAnchor!] = new LinkTarget(page, null);
                    pending = null;
                }
            } else if (page is null) {
                errors.Add($"{where}: a section outside a page");
            } else {
                AddSection(page, heading, title, where, errors);
                guide.Anchors[anchor] = new LinkTarget(page, page.Sections[^1].Anchor);
            }
        }

        if (guide.Pages.Count == 0) {
            errors.Add($"{source}: no pages");

            return guide;
        }
        if (!string.IsNullOrEmpty(titleAnchor)) {
            guide.Anchors[titleAnchor] = new LinkTarget(guide.Pages[0], null);
        }
        foreach (var duplicate in guide.Pages.GroupBy(p => p.Slug).Where(g => g.Count() > 1)) {
            string lines = string.Join(", ", duplicate.Select(p => p.Heading.Line + 1));
            errors.Add($"{source}:{lines}: pages share the slug '{duplicate.Key}'");
        }
        foreach (var empty in guide.Pages.Where(p => p.Slug.Length == 0)) {
            errors.Add($"{source}:{empty.Heading.Line + 1}: a page title with nothing to make a slug of");
        }

        return guide;
    }


    /// <summary>
    /// A section heading keeps its place in the text, gets an id, and moves up
    /// two levels: the page title is the h1, so #### is an h2 on the page.
    /// </summary>
    private static void AddSection(GuidePage page, HeadingBlock heading, string title, string where, List<string> errors) {
        string id = Slugs.Translit(title);
        if (id.Length == 0 || page.Sections.Any(s => s.Anchor == id)) {
            errors.Add($"{where}: section anchor '{id}' is empty or repeats on the page");
        }

        page.Sections.Add(new GuideSection(title, id, heading.Level));
        heading.GetAttributes().Id = id;
        heading.Level -= 2;
        page.Blocks.Add(heading);
    }

    private static string PlainText(ContainerInline? container) {
        var text = new StringBuilder();
        Append(container, text);

        return text.ToString().Trim();
    }

    private static void Append(ContainerInline? container, StringBuilder text) {
        if (container is null) {
            return;
        }

        foreach (Inline inline in container) {
            if (inline is LiteralInline literal) {
                text.Append(literal.Content.ToString());
            } else if (inline is CodeInline code) {
                text.Append(code.Content);
            } else if (inline is ContainerInline inner) {
                Append(inner, text);
            }
        }
    }
}


internal sealed class GuidePage {
    public GuidePage(string title, HeadingBlock heading, GuideGroup? group) {
        Title = title;
        Slug = Slugs.Translit(title);
        Heading = heading;
        Group = group;
    }


    public string Title { get; }

    /// <summary>The page's folder: <c>guide/&lt;slug&gt;/index.html</c>.</summary>
    public string Slug { get; }

    public HeadingBlock Heading { get; }

    public GuideGroup? Group { get; }

    /// <summary>What is under the title, section headings included.</summary>
    public List<Block> Blocks { get; } = new();

    public List<GuideSection> Sections { get; } = new();
}


internal sealed record GuideGroup(string Title);


internal sealed record GuideSection(string Title, string Anchor, int Level);


internal sealed record LinkTarget(GuidePage Page, string? Anchor);
