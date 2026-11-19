using System.Text.Json;
using Wander.Core.Logging;
using Wander.Core.Persistence;

namespace Wander.Platform.Windows.Persistence;

/// <summary>
/// <c>bookmark-titles.json</c> in the data folder: the bookmarks' own names
/// (<see cref="IBookmarkTitleStore"/>), path to name. Written whole,
/// write-then-rename, like <c>folders.json</c>; a yielding instance never
/// writes, a file that did not read is not written over as it is
/// (<see cref="JsonDataFile"/>).
/// </summary>
public sealed class JsonBookmarkTitleStore : IBookmarkTitleStore {
    private static readonly JsonSerializerOptions _options = new() { WriteIndented = true };

    private readonly JsonDataFile _file;
    private readonly InstanceLock _owner;


    public JsonBookmarkTitleStore(InstanceLock owner, ILogger log) {
        _file = new JsonDataFile(AppPaths.BookmarkTitlesFile, log, currentVersion: null);
        _owner = owner;
    }


    public bool IsReadOnly => _owner.IsYielding;


    public IReadOnlyDictionary<string, string> Load() {
        // One that did not read gives no names: the bookmarks read as their folders.
        return _file.Read<Dictionary<string, string>>(_options) ?? new Dictionary<string, string>();
    }

    public void Save(IReadOnlyDictionary<string, string> titles) {
        if (IsReadOnly) {
            return;
        }

        // Best effort, like folders.json.
        _file.Write(JsonSerializer.Serialize(titles, _options));
    }
}
