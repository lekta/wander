using Wander.Core.FileSystem;

namespace Wander.Core.Search;

/// <summary>Why a file is in the result list, alongside the file itself.</summary>
/// <param name="Entry">The row to show. Carries the full path, so results from different folders can share one list.</param>
/// <param name="Snippet">
/// The line the match sits on, trimmed and with runs of whitespace
/// collapsed — or null when the file matched by name only. A file that
/// matched on both is reported with the snippet: the name is already
/// visible in the row, the line inside the file is not.
/// </param>
/// <param name="Line">1-based line number of <paramref name="Snippet"/>, or 0 when there is none.</param>
public sealed record SearchHit(FileSystemEntry Entry, string? Snippet, int Line);


/// <summary>How far along a running search is, for the status bar.</summary>
/// <param name="FilesScanned">Files looked at so far, matched or not.</param>
/// <param name="Found">Hits reported so far.</param>
/// <param name="CurrentFolder">Folder being walked right now, for the "searching in…" line.</param>
public readonly record struct SearchProgress(int FilesScanned, int Found, string CurrentFolder);


/// <summary>How a search ended.</summary>
/// <param name="FilesScanned">Total files looked at.</param>
/// <param name="Found">Total hits reported.</param>
/// <param name="Truncated">True when <see cref="SearchRequest.MaxResults"/> cut the pass short.</param>
/// <param name="UnreadableFiles">
/// Files whose content could not be read at all — no extractor claimed
/// them, or the one that did failed. Reported rather than swallowed: "no
/// matches" and "nothing could be opened" are different answers, and a
/// folder of PDFs on a machine with no PDF filter installed gives the
/// second one.
/// </param>
public readonly record struct SearchOutcome(int FilesScanned, int Found, bool Truncated, int UnreadableFiles);
