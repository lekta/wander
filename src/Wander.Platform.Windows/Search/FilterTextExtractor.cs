using System.Runtime.InteropServices;
using System.Text;
using Wander.Core.Logging;
using Wander.Core.Search;

namespace Wander.Platform.Windows.Search;

/// <summary>
/// Text out of the formats Windows itself knows how to read: <c>.doc</c>,
/// <c>.rtf</c>, <c>.htm</c> out of the box, and whatever else the machine
/// has a filter installed for — <c>.pdf</c> where a PDF reader is
/// installed, the Office formats where Office is.
///
/// <para>
/// This is the answer to "what do we parse <c>.doc</c> with" (PLAN B5)
/// that costs nothing: the binary Word format has a piece table, fast
/// saves and eight-bit compressed runs, and a hand-written reader for it
/// is a long tail of wrong answers. Windows ships a correct one in
/// <c>OffFilt.dll</c> and has since Windows 7.
/// </para>
///
/// <para>
/// The honest limit is that the set of formats is the user's machine, not
/// ours. A PDF on a machine with no PDF filter is reported unreadable
/// rather than silently treated as containing nothing — see
/// <see cref="SearchOutcome.UnreadableFiles"/>.
/// </para>
/// </summary>
public sealed class FilterTextExtractor : IContentExtractor {
    /// <summary>
    /// Characters pulled from one document before we stop asking. The same
    /// reasoning as <c>ZipDocumentExtractor</c>: past a few million, the
    /// file is generated and the answer will not change.
    /// </summary>
    private const int MaxChars = 8 * 1024 * 1024;

    /// <summary>
    /// Buffer handed to <c>GetText</c>. One char short of this is asked for
    /// each time — the filter writes a terminator the count does not
    /// include, and a buffer sized exactly is how this call corrupts the
    /// heap.
    /// </summary>
    private const int BufferChars = 8192;

    /// <summary>
    /// The formats handed to a filter — a named list rather than "whatever
    /// the registry has".
    ///
    /// <para>
    /// The registry answers for far more than this, plain text included,
    /// and taking it up on that would be a step backwards twice over: a COM
    /// round trip per source file instead of a read, and the shipped text
    /// filter decodes bytes as the system codepage where
    /// <see cref="Wander.Core.Preview.EncodingProbe"/> works out what the
    /// file is actually in — a Windows-1251 note would come back as
    /// mojibake. So this list is only what nothing else in Wander can read:
    /// container formats and compressed ones.
    /// </para>
    ///
    /// <para>
    /// <c>.htm</c> is deliberately absent even though Windows filters it
    /// well. In a file manager, HTML is as often something to grep as
    /// something to read, and stripping it to prose would lose every
    /// attribute and tag the user might be looking for.
    /// </para>
    /// </summary>
    private static readonly HashSet<string> _formats = new(StringComparer.OrdinalIgnoreCase) {
        ".doc", ".dot", ".rtf", ".pdf", ".chm", ".msg",
        ".wpd", ".xps", ".oxps", ".one", ".pub", ".vsd", ".vsdx",
        ".mht", ".mhtml",
    };


    private readonly ILogger _log;

    /// <summary>Extensions already found to have no filter registered on this machine.</summary>
    private readonly HashSet<string> _withoutFilter = new(StringComparer.OrdinalIgnoreCase);


    public FilterTextExtractor(ILogger? log = null) {
        _log = log ?? NullLogger.Instance;
    }


    /// <summary>Expensive: a COM object per file, and a document parse behind it.</summary>
    public bool IsExpensive => true;


    /// <summary>
    /// Claims the format whether or not this machine has a filter for it.
    /// Saying no once the filter turns out to be missing would be cheaper,
    /// but it would also mean the caller stops counting those files as
    /// unreadable after the first one — and "none of your PDFs could be
    /// opened" is exactly the answer that must not go quiet.
    /// </summary>
    public bool CanExtract(string path) {
        return _formats.Contains(Path.GetExtension(path));
    }


    public string? Extract(string path, CancellationToken token) {
        // A folder of PDFs on a machine without a PDF filter would
        // otherwise pay a failing COM lookup per file; one failure is
        // enough to know about all of them.
        lock (_withoutFilter) {
            if (_withoutFilter.Contains(Path.GetExtension(path))) {
                return null;
            }
        }

        IFilter? filter = null;
        int hr;
        try {
            hr = NativeFilter.LoadIFilter(path, IntPtr.Zero, out filter);
        } catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) {
            // No query.dll: not a Windows we can filter on. Stop asking.
            _log.Warn($"IFilter unavailable: {ex.Message}");
            RememberNoFilter(Path.GetExtension(path));

            return null;
        }

        if (hr != NativeFilter.SOk || filter is null) {
            RememberNoFilter(Path.GetExtension(path));

            return null;
        }

        try {
            return ReadAll(filter, token);
        } catch (COMException ex) {
            _log.Warn($"IFilter failed on {path}: 0x{ex.HResult:X8}");

            return null;
        } finally {
            Marshal.ReleaseComObject(filter);
        }
    }


    /// <summary>
    /// Walks the filter's chunks and concatenates the textual ones. Value
    /// chunks — the document's properties, author and title — are skipped:
    /// they are metadata about the file, and a search for a word inside
    /// documents that hits on the name of whoever wrote them is a search
    /// nobody asked for.
    /// </summary>
    private static string? ReadAll(IFilter filter, CancellationToken token) {
        uint outFlags = 0;
        // ApplyIndexAttributes is not here for the attributes — the value
        // chunks it produces are skipped below. It is here because without
        // it the shipped Office and RTF filters return no chunks at all
        // once any canonicalisation flag is set: measured on this Windows,
        // flags 1|2 and 1|2|8 both yield an empty document, while 0, 16 and
        // 1|2|8|16 all yield its text. Canonicalisation is worth keeping —
        // it is what turns a Word paragraph into a line — so the flag that
        // makes the filters answer comes with it.
        int hr = filter.Init(
            FilterInit.CanonParagraphs | FilterInit.HardLineBreaks | FilterInit.CanonSpaces
                | FilterInit.ApplyIndexAttributes,
            0,
            IntPtr.Zero,
            ref outFlags);
        if (hr != NativeFilter.SOk) {
            return null;
        }

        var text = new StringBuilder();
        var buffer = new StringBuilder(BufferChars);

        while (text.Length < MaxChars) {
            token.ThrowIfCancellationRequested();

            hr = filter.GetChunk(out var chunk);
            if (hr == NativeFilter.FilterEEndOfChunks) {
                break;
            }
            if (hr != NativeFilter.SOk) {
                // Anything else mid-document is a chunk we cannot use;
                // whatever came before it is still a usable answer.
                break;
            }
            if (chunk.Flags != ChunkState.Text) {
                continue;
            }

            AppendChunk(filter, buffer, text, token);
        }

        return text.Length > 0 ? text.ToString() : null;
    }


    private static void AppendChunk(IFilter filter, StringBuilder buffer, StringBuilder text, CancellationToken token) {
        while (text.Length < MaxChars) {
            token.ThrowIfCancellationRequested();

            uint chars = BufferChars - 1;
            buffer.Length = 0;
            int hr = filter.GetText(ref chars, buffer);

            if (hr is NativeFilter.FilterENoMoreText or NativeFilter.FilterENoText) {
                return;
            }
            if (hr is not (NativeFilter.SOk or NativeFilter.FilterSLastText)) {
                return;
            }

            // The filter reports how much it wrote; the StringBuilder does
            // not know, so the count is the one to trust — but never past
            // what is actually there.
            text.Append(buffer.ToString(0, (int)Math.Min(chars, (uint)buffer.Length)));
            text.Append(' ');

            if (hr == NativeFilter.FilterSLastText) {
                return;
            }
        }
    }


    private void RememberNoFilter(string extension) {
        if (extension.Length == 0) {
            return;
        }

        lock (_withoutFilter) {
            _withoutFilter.Add(extension);
        }
    }
}
