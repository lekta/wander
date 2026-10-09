using System.IO;
using Markdig;
using Wander.App.Resources;
using Wander.App.Util;
using Wander.Core.FileSystem;
using Wander.Core.Preview;

namespace Wander.App.Preview;

/// <summary>
/// What the pane reads off disk, and how much of it. One file is text,
/// code, Markdown or a book depending on which loader asked, but the
/// reading is the same question every time: which encoding, how many bytes
/// are worth spending, and what to say when the file goes on past there.
/// </summary>
/// <param name="Text">What was decoded — never the whole file past the budget.</param>
/// <param name="Clipped">Whether the file goes on past what was read.</param>
/// <param name="Size">The file's real size, for the note at the bottom.</param>
internal readonly record struct PreviewTextFile(string Text, bool Clipped, long Size);


internal static class PreviewText {
    private const long MaxFileSize = 1_048_576;     // 1 MB
    private const int MaxChars = 200_000;

    /// <summary>A table page past this is built with fewer rows: the web view takes 2 MB as a string.</summary>
    private const int MaxSheetPageBytes = 1_900_000;

    /// <summary>
    /// The grid of <see cref="SheetPage"/>: the page itself does not
    /// scroll, each sheet does under the tabs, so the column letters and
    /// row numbers can stick to its edges.
    /// </summary>
    private const string SheetCss = @"
        :root { color-scheme: light dark; --paper: #FFFFFF; --ink: #222222; --chrome: #F3F3F3; --line: #E0E0E0; --dim: #7A7A7A; }
        @media (prefers-color-scheme: dark) {
            :root { --paper: #1E1E1E; --ink: #E0E0E0; --chrome: #2B2B2B; --line: #3A3A3A; --dim: #9A9A9A; }
        }
        html, body { margin: 0; height: 100%; overflow: hidden; background: var(--paper); color: var(--ink); }
        body { font: 12px 'Segoe UI', sans-serif; }
        input.tab { display: none; }
        .tabs { position: absolute; top: 0; left: 0; right: 0; height: 26px; display: flex; overflow-x: auto; overflow-y: hidden;
                white-space: nowrap; background: var(--chrome); border-bottom: 1px solid var(--line); }
        .tabs label { padding: 4px 12px; cursor: pointer; color: var(--dim); border-right: 1px solid var(--line); }
        .sheet { display: none; position: absolute; top: 27px; bottom: 0; left: 0; right: 0; overflow: auto; }
        .single .sheet { top: 0; }
        table { border-collapse: separate; border-spacing: 0; }
        th, td { border-right: 1px solid var(--line); border-bottom: 1px solid var(--line); padding: 2px 6px;
                 white-space: nowrap; max-width: 360px; overflow: hidden; text-overflow: ellipsis; vertical-align: top; }
        th { background: var(--chrome); color: var(--dim); font-weight: normal; }
        thead th { position: sticky; top: 0; z-index: 1; text-align: center; min-width: 24px; }
        tbody th { position: sticky; left: 0; text-align: right; }
        thead th.corner { left: 0; z-index: 2; }
        td.n { text-align: right; }
        .note { margin: 0; padding: 6px 8px; color: var(--dim); }
        ";

    /// <summary>
    /// Books get a budget of their own: a novel is legitimately tens of
    /// megabytes once its illustrations are counted, and the 1 MB ceiling
    /// the text preview uses would refuse most of a shelf.
    /// </summary>
    public const long BookMaxFileSize = 64L * 1024 * 1024;

    /// <summary>
    /// Book-specific rules on top of the shared ones: a cover that sits at
    /// a plate's size rather than filling the pane, and the indented,
    /// centred shapes FB2 uses for verse and epigraphs.
    /// </summary>
    public const string BookCss = @"
        .fb2-head { text-align: center; margin-bottom: 1.5em; }
        .fb2-cover { max-width: 220px; max-height: 320px; box-shadow: 0 1px 6px rgba(0,0,0,.35); margin-bottom: 10px; }
        .fb2-head h1 { font-size: 18px; margin: 0.2em 0; }
        .fb2-author { color: #555; margin: 0.2em 0 0; }
        .fb2-annotation { text-align: left; font-size: 12px; color: #444; border-top: 1px solid #DDD; margin-top: 12px; padding-top: 8px; }
        .fb2-title { font-size: 15px; font-weight: 600; margin: 1.2em 0 0.5em; }
        .fb2-title p { margin: 0; }
        .fb2-empty { height: 0.8em; }
        .fb2-poem { margin: 1em 2em; font-style: italic; }
        .fb2-stanza { margin-bottom: 0.8em; }
        .fb2-text-author { text-align: right; color: #555; font-style: italic; }
        .fb2-image { display: block; margin: 1em auto; max-width: 100%; }
        .fb2-cut { color: #A05000; border-top: 1px solid #DDD; padding-top: 8px; }
        p { text-indent: 1.2em; margin: 0.2em 0; text-align: justify; }
        blockquote p { text-indent: 0; }
        @media (prefers-color-scheme: dark) {
            .fb2-author, .fb2-text-author { color: #BDBDBD; }
            .fb2-annotation { color: #BDBDBD; border-top-color: #3A3A3A; }
            .fb2-cut { color: #E8A33D; border-top-color: #3A3A3A; }
        }";


    /// <summary>
    /// Markdig speaks plain CommonMark unless told otherwise, and CommonMark
    /// has no tables — a <c>| … | … |</c> block came out as one run-on
    /// paragraph of pipes and dashes. Which is most of what a README's
    /// tables are for.
    ///
    /// <para>
    /// Listed one by one rather than through <c>UseAdvancedExtensions()</c>:
    /// that bundle also turns YouTube links into iframes and reads
    /// <c>{#id .class}</c> out of the text as markup, neither of which a
    /// preview pane wants — least of all one that blocks the network and
    /// would show the iframe as an empty box.
    /// </para>
    /// </summary>
    private static readonly MarkdownPipeline _markdownPipeline =
        new MarkdownPipelineBuilder()
            .UsePipeTables()
            .UseGridTables()
            .UseEmphasisExtras()      // ~~strikethrough~~, ++inserted++
            .UseTaskLists()           // - [x] done
            .UseAutoLinks()           // bare https://… as a link
            .UseFootnotes()
            .Build();


    /// <summary>
    /// Reads a text file, working out its encoding rather than assuming
    /// UTF-8. Assuming turns every byte of a codepaged file into
    /// <c>U+FFFD</c>, and a folder of old notes reads as a wall of black
    /// diamonds — see <see cref="EncodingProbe"/>.
    ///
    /// <para>
    /// A file past <see cref="MaxFileSize"/> is read up to that budget and
    /// reported as clipped, rather than refused. Refusing was the old
    /// behaviour and it was the wrong answer to the question the pane
    /// exists to answer: a two-megabyte log is still a log, and its first
    /// megabyte says what it is. <c>Clipped</c> is what the caller turns
    /// into the note at the bottom, so the reader is never left thinking
    /// they have seen the end of the file.
    /// </para>
    /// </summary>
    public static async Task<PreviewTextFile?> ReadAsync(string path, CancellationToken ct) {
        try {
            await using var file = SharedRead.Open(path, bufferSize: 64 * 1024, FileOptions.Asynchronous);

            long size = file.Length;
            int budget = (int)Math.Min(size, MaxFileSize);
            byte[] bytes = new byte[budget];
            await file.ReadExactlyAsync(bytes, ct);

            string text = EncodingProbe.Decode(bytes);
            bool clipped = size > budget;
            if (clipped) {
                // The cut lands wherever the budget ran out, which for
                // anything but ASCII is regularly the middle of a
                // character. The decoder turns that stump into U+FFFD;
                // dropping the tail is nicer than ending the preview on a
                // black diamond that is an artefact of where we stopped
                // reading, not of the file.
                text = text.TrimEnd('�');
            }

            return new PreviewTextFile(text, clipped, size);
        } catch (OperationCanceledException) {
            throw;
        } catch {
            return null;
        }
    }


    /// <summary>
    /// The text a plain or highlighted preview shows: cut to what the pane
    /// will render, and closed with the note when anything was left out.
    /// <paramref name="notePrefix"/> makes that note a comment where the
    /// view expects code.
    /// </summary>
    public static string Clip(PreviewTextFile file, string notePrefix = "") {
        string text = file.Text;
        bool clipped = file.Clipped;
        if (text.Length > MaxChars) {
            text = text.Substring(0, MaxChars);
            clipped = true;
        }

        return clipped ? text + ClippedNote(file.Size, notePrefix) : text;
    }


    /// <summary>How many characters of the file's text <see cref="Clip"/> keeps, before its note.</summary>
    public static int ShownChars(PreviewTextFile file) {
        return Math.Min(file.Text.Length, MaxChars);
    }

    /// <summary>Whether <see cref="Clip"/> leaves anything out - the file goes past what was read, or past what the pane renders.</summary>
    public static bool GoesOn(PreviewTextFile file) {
        return file.Clipped || file.Text.Length > MaxChars;
    }


    /// <summary>
    /// Markdown rendered to HTML. The note about a clipped file has to be
    /// Markdown too — a rule and an emphasised line, which is what "the
    /// file goes on past here" looks like in a rendered document.
    /// </summary>
    public static string MarkdownToHtml(PreviewTextFile file) {
        string md = file.Clipped
            ? file.Text + "\n\n---\n\n*" + string.Format(Strings.PreviewClipped, SizeFormatter.Format(file.Size)) + "*"
            : file.Text;

        return Markdown.ToHtml(md, _markdownPipeline);
    }


    /// <summary>
    /// The page the web view is handed. Everything rendered — Markdown and
    /// FB2 — goes through here, so both look like the same application.
    /// Both schemes are in the stylesheet: the pane tells the browser which
    /// one the window is in (<c>PreferredColorScheme</c>), and the dark one
    /// takes the dark theme's surface and text.
    /// </summary>
    public static string WrapHtml(string body, string extraCss = "") {
        return $@"<!doctype html><html><head><meta charset='utf-8'><style>
            :root {{ color-scheme: light dark; }}
            body {{ font-family: 'Segoe UI', sans-serif; font-size: 13px; padding: 10px; color: #222; }}
            pre, code {{ font-family: Consolas, monospace; background: #f4f4f4; padding: 2px 4px; border-radius: 3px; }}
            pre {{ padding: 8px; overflow-x: auto; }}
            h1, h2, h3 {{ margin: 0.6em 0 0.3em; }}
            blockquote {{ border-left: 3px solid #ccc; margin: 0; padding-left: 10px; color: #555; }}
            /* display:block so a table wider than the pane scrolls inside
               itself instead of pushing the whole page sideways. */
            table {{ border-collapse: collapse; display: block; overflow-x: auto; max-width: 100%; }}
            th, td {{ border: 1px solid #ccc; padding: 4px 8px; text-align: left; }}
            th {{ background: #F0F0F0; }}
            img {{ max-width: 100%; }}
            ul.contains-task-list {{ list-style: none; padding-left: 1.2em; }}
            @media (prefers-color-scheme: dark) {{
                html, body {{ background: #1E1E1E; color: #E0E0E0; }}
                pre, code {{ background: #2B2B2B; }}
                blockquote {{ border-left-color: #555; color: #BDBDBD; }}
                th, td {{ border-color: #474747; }}
                th {{ background: #2B2B2B; }}
                a {{ color: #60B0F0; }}
            }}
            {extraCss}
        </style></head><body>{body}</body></html>";
    }


    /// <summary>
    /// The page an SVG is drawn on: the picture as an image, fitted
    /// to the pane either way - a vector has no size worth keeping - on the
    /// surround's colour, as the pane draws a photograph. As an image it runs
    /// no script and fetches nothing, whatever the file carries.
    /// </summary>
    public static string SvgPage(byte[] svg, string background) {
        return "<!doctype html><html><head><meta charset='utf-8'><style>" +
            $"html, body {{ margin: 0; height: 100%; background: {background}; }}" +
            "img { display: block; width: 100%; height: 100%; object-fit: contain; box-sizing: border-box; padding: 4px; }" +
            "</style></head><body><img src='data:image/svg+xml;base64," + Convert.ToBase64String(svg) + "'></body></html>";
    }


    /// <summary>
    /// The page a table is shown on: a sheet at a time under tabs of their
    /// names, columns lettered and rows numbered as in a spreadsheet, both
    /// kept in view while the grid scrolls. Tabs are radio buttons and
    /// CSS - the page needs no script. A long cell is cut to a line, the
    /// whole of it in its tooltip; numbers stand to the right.
    ///
    /// <para>
    /// The web view takes a page of at most 2 MB as a string: past
    /// <see cref="MaxSheetPageBytes"/> the page is built again with half
    /// the rows, and the note under each sheet says how many it shows.
    /// </para>
    /// </summary>
    public static string SheetPage(IReadOnlyList<Sheet> sheets) {
        int rows = int.MaxValue;
        while (true) {
            string page = BuildSheetPage(sheets, rows);
            if (rows <= 1 || System.Text.Encoding.UTF8.GetByteCount(page) <= MaxSheetPageBytes) {
                return page;
            }

            rows = Math.Min(rows, sheets.Max(s => s.Rows.Count)) / 2;
        }
    }


    private static string BuildSheetPage(IReadOnlyList<Sheet> sheets, int rowLimit) {
        var html = new System.Text.StringBuilder();
        html.Append("<!doctype html><html><head><meta charset='utf-8'><style>").Append(SheetCss);
        for (int i = 0; i < sheets.Count; i++) {
            html.Append($"#s{i}:checked ~ #t{i} {{ display: block; }} ");
            html.Append($"#s{i}:checked ~ .tabs label[for=s{i}] {{ background: var(--paper); color: var(--ink); font-weight: 600; }} ");
        }
        html.Append("</style></head><body").Append(sheets.Count == 1 ? " class='single'>" : ">");

        for (int i = 0; i < sheets.Count; i++) {
            html.Append($"<input type='radio' class='tab' name='sheet' id='s{i}'").Append(i == 0 ? " checked>" : ">");
        }
        if (sheets.Count > 1) {
            html.Append("<div class='tabs'>");
            for (int i = 0; i < sheets.Count; i++) {
                html.Append($"<label for='s{i}'>{Html(sheets[i].Name)}</label>");
            }
            html.Append("</div>");
        }

        for (int i = 0; i < sheets.Count; i++) {
            var sheet = sheets[i];
            int shown = Math.Min(sheet.Rows.Count, rowLimit);
            int columns = sheet.Columns;
            html.Append($"<div class='sheet' id='t{i}'>");
            if (columns == 0) {
                html.Append($"<p class='note'>{Html(Strings.PreviewTableEmpty)}</p></div>");

                continue;
            }

            html.Append("<table><thead><tr><th class='corner'></th>");
            for (int c = 0; c < columns; c++) {
                html.Append("<th>").Append(ColumnName(c)).Append("</th>");
            }
            html.Append("</tr></thead><tbody>");
            for (int r = 0; r < shown; r++) {
                var row = sheet.Rows[r];
                html.Append("<tr><th>").Append(r + 1).Append("</th>");
                for (int c = 0; c < columns; c++) {
                    string cell = c < row.Length ? row[c] : "";
                    if (cell.Length == 0) {
                        html.Append("<td></td>");
                    } else {
                        string text = Html(cell);
                        html.Append(LooksNumeric(cell) ? "<td class='n'" : "<td");
                        html.Append(cell.Length > 40 || cell.Contains('\n') ? $" title='{text}'>" : ">");
                        html.Append(text).Append("</td>");
                    }
                }
                html.Append("</tr>");
            }
            html.Append("</tbody></table>");

            if (shown < sheet.Rows.Count || sheet.Clipped) {
                string note = sheet.TotalRows is { } total
                    ? string.Format(Strings.PreviewTableRowsOf, shown, total)
                    : string.Format(Strings.PreviewTableRowsMore, shown);
                html.Append($"<p class='note'>{Html(note)}</p>");
            }
            html.Append("</div>");
        }

        return html.Append("</body></html>").ToString();
    }

    /// <summary>"A", "Z", "AA" - a spreadsheet's name for a zero-based column.</summary>
    private static string ColumnName(int column) {
        string name = "";
        for (int n = column + 1; n > 0; n = (n - 1) / 26) {
            name = (char)('A' + (n - 1) % 26) + name;
        }

        return name;
    }

    /// <summary>A number as a spreadsheet shows one - digits with group spaces, a decimal mark, a sign, a percent.</summary>
    private static bool LooksNumeric(string text) {
        return text.Length < 40 && System.Text.RegularExpressions.Regex.IsMatch(text, @"^[-+−]?[\d   ]*\d([.,]\d+)?([eE][-+]?\d+)?%?$");
    }

    private static string Html(string text) {
        return System.Net.WebUtility.HtmlEncode(text);
    }


    /// <summary>
    /// The line that closes a preview which does not reach the end of the
    /// file — either because the file is bigger than the read budget, or
    /// because it holds more characters than the pane will render.
    /// </summary>
    private static string ClippedNote(long size, string prefix = "") {
        return "\n\n" + prefix + string.Format(Strings.PreviewClipped, SizeFormatter.Format(size));
    }
}
