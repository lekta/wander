using Wander.Core.FileSystem;

namespace Wander.Core.Search;

/// <summary>
/// One search, as a value. Passed whole into the background pass so a
/// setting changed mid-flight cannot alter a search that is already
/// running — the same rule the folder listing follows with
/// <see cref="EntryVisibility"/> and <see cref="SortOptions"/>.
///
/// <para>
/// Two criteria, not one, and they are combined with <em>and</em>. That is
/// the whole point of splitting them: "every <c>.cs</c> that mentions
/// <c>ExtractedTextCache</c>" is the question people actually have, and a
/// single field could only ever answer "name or contents", which returns
/// every picture in the folder whose name happens to contain the letter
/// the user was looking for inside documents.
/// </para>
/// </summary>
/// <param name="Name">
/// Mask on the file name. Empty lets everything through — a search for
/// text alone is a legitimate search.
/// </param>
/// <param name="Text">
/// Text to look for inside files. Empty means names only, which is also
/// what makes this the cheap half: contents mean opening every candidate.
/// </param>
/// <param name="Root">Folder the search starts from.</param>
/// <param name="Scope">How far to reach.</param>
/// <param name="SearchBinaries">
/// Also scan files that are not text, byte for byte — see
/// <see cref="BinaryTextSearch"/>. Only meaningful together with
/// <paramref name="Text"/>.
/// </param>
/// <param name="Visibility">Which entries may appear, same rules as the folder listing.</param>
public sealed record SearchRequest(
    NameFilter Name,
    string Text,
    string Root,
    SearchScope Scope,
    bool SearchBinaries,
    EntryVisibility Visibility) {
    /// <summary>True when files have to be opened to answer this.</summary>
    public bool HasText => Text.Length > 0;

    /// <summary>
    /// True when this asks for nothing at all. Such a request is not run:
    /// "show me every file on the disk" is not a search, it is a mistake
    /// with a five-thousand-row result.
    /// </summary>
    public bool IsEmpty => Name.IsEmpty && Text.Length == 0;

    /// <summary>
    /// Files larger than this are not read for content. Not a parser limit:
    /// a floor under how long one row of a folder full of database dumps
    /// may hold up the pass. Names of oversized files are still matched,
    /// unless the search also asks for text — then they cannot qualify.
    /// </summary>
    public long MaxFileSize { get; init; } = 32L * 1024 * 1024;

    /// <summary>
    /// Where the result list stops growing. Past a few thousand rows the
    /// answer is "narrow the query", and collecting a hundred thousand
    /// entries to say so costs both memory and the user's patience.
    /// </summary>
    public int MaxResults { get; init; } = 5000;

    /// <summary>
    /// How deep <see cref="SearchScope.Subfolders"/> walks. Deep enough for
    /// any real tree, shallow enough that a directory junction pointing at
    /// its own ancestor terminates even if the visited-set misses it.
    /// </summary>
    public int MaxDepth { get; init; } = 64;
}
