namespace Wander.Core.Search;

/// <summary>
/// Pulls the searchable text out of one file format.
///
/// <para>
/// The abstraction exists because "search inside files" is the same
/// question for a <c>.cs</c>, a <c>.docx</c> and a <c>.doc</c>, and only
/// the answer differs: one is bytes to decode, one is a zip of XML, one is
/// a COM filter the operating system ships. The layers differ too — the
/// first two live in Core, the third can only live in
/// <c>Wander.Platform.Windows</c> — and this interface is the seam that
/// lets them.
/// </para>
///
/// <para>
/// This is also the abstraction the preview pane's <c>.doc</c> support
/// (PLAN B5) was waiting on: whatever can be searched can be shown.
/// </para>
/// </summary>
public interface IContentExtractor {
    /// <summary>
    /// True when the extraction is dear enough to be worth remembering.
    /// Decoding a source file is a memcpy and a scan; unzipping a document
    /// or crossing a COM boundary is tens of milliseconds, and those are
    /// the ones <see cref="ExtractedTextCache"/> keeps.
    /// </summary>
    bool IsExpensive { get; }

    /// <summary>
    /// Whether this extractor is willing to try <paramref name="path"/>.
    /// Decided from the name alone — the point of asking is to avoid
    /// opening the file.
    /// </summary>
    bool CanExtract(string path);

    /// <summary>
    /// The file's text, or null when it turned out not to be readable
    /// after all (a <c>.asset</c> that is binary, a damaged zip, a format
    /// the registered filter refused). Implementations swallow their own
    /// I/O and parse failures and answer null: one unreadable file must not
    /// end a search over ten thousand of them.
    /// </summary>
    string? Extract(string path, CancellationToken token);
}
