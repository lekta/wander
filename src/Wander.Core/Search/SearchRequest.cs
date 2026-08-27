using Wander.Core.FileSystem;

namespace Wander.Core.Search;

/// <summary>
/// One search, as a value. Passed whole into the background pass so a
/// setting changed mid-flight cannot alter a search that is already
/// running — the same rule the folder listing follows with
/// <see cref="EntryVisibility"/> and <see cref="SortOptions"/>.
/// </summary>
/// <param name="Query">What the user typed. Never empty — an empty query is not a search.</param>
/// <param name="Root">Folder the search starts from. Ignored when <paramref name="Scope"/> is <see cref="SearchScope.Computer"/>.</param>
/// <param name="Scope">How far to reach.</param>
/// <param name="SearchContents">
/// Also look inside files, not just at their names. Off by default because
/// it is the expensive half: names come free with the directory listing,
/// contents mean opening every candidate file.
/// </param>
/// <param name="Visibility">Which entries may appear, same rules as the folder listing.</param>
public sealed record SearchRequest(
    string Query,
    string Root,
    SearchScope Scope,
    bool SearchContents,
    EntryVisibility Visibility) {
    /// <summary>
    /// Files larger than this are not read for content. Not a parser limit:
    /// a floor under how long one row of a folder full of database dumps
    /// may hold up the pass. Names of oversized files are still matched.
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
