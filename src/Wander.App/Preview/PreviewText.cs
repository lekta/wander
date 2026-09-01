using System.IO;
using System.Threading.Tasks;
using Markdig;
using Wander.App.Resources;
using Wander.App.Util;
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

    /// <summary>
    /// Books get a budget of their own: a novel is legitimately tens of
    /// megabytes once its illustrations are counted, and the 1 MB ceiling
    /// the text preview uses would refuse most of a shelf.
    /// </summary>
    public const long BookMaxFileSize = 64L * 1024 * 1024;


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
            await using var file = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
                bufferSize: 64 * 1024, useAsync: true);

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


    /// <summary>
    /// The line that closes a preview which does not reach the end of the
    /// file — either because the file is bigger than the read budget, or
    /// because it holds more characters than the pane will render.
    /// </summary>
    private static string ClippedNote(long size, string prefix = "") {
        return "\n\n" + prefix + string.Format(Strings.PreviewClipped, SizeFormatter.Format(size));
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
        blockquote p { text-indent: 0; }";


    /// <summary>
    /// The page the web view is handed. Everything rendered — Markdown and
    /// FB2 — goes through here, so both look like the same application.
    /// </summary>
    public static string WrapHtml(string body, string extraCss = "") {
        return $@"<!doctype html><html><head><meta charset='utf-8'><style>
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
            {extraCss}
        </style></head><body>{body}</body></html>";
    }
}
