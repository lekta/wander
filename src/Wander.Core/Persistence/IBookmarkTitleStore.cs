namespace Wander.Core.Persistence;

/// <summary>
/// Where the bookmarks' own names live (<c>bookmark-titles.json</c>,
/// <see cref="AppPaths.BookmarkTitlesFile"/>): a file of their own rather
/// than a block of <c>state.json</c>, which an older Wander writes whole and
/// would write without them (BACKLOG AD11). The list of bookmarks stays in
/// <c>state.json</c>; a name whose bookmark is gone is simply never asked for.
/// </summary>
public interface IBookmarkTitleStore {
    /// <summary>True while this instance must not write - the rule of <see cref="IAppStateStore.IsReadOnly"/>.</summary>
    bool IsReadOnly { get; }

    /// <summary>The names on disk, by path; empty when there is no file or it cannot be read.</summary>
    IReadOnlyDictionary<string, string> Load();

    /// <summary>Replaces the file with <paramref name="titles"/>. Best effort: a failure to persist never surfaces.</summary>
    void Save(IReadOnlyDictionary<string, string> titles);
}
