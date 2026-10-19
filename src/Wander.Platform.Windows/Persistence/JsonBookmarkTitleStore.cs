using System.Text.Json;
using Wander.Core.Persistence;

namespace Wander.Platform.Windows.Persistence;

/// <summary>
/// <c>bookmark-titles.json</c> in the data folder: the bookmarks' own names
/// (<see cref="IBookmarkTitleStore"/>), path to name. Written whole,
/// write-then-rename, like <c>folders.json</c>; a yielding instance never
/// writes.
/// </summary>
public sealed class JsonBookmarkTitleStore : IBookmarkTitleStore {
    private static readonly JsonSerializerOptions _options = new() { WriteIndented = true };

    private readonly string _filePath;
    private readonly InstanceLock _owner;


    public JsonBookmarkTitleStore(InstanceLock owner) {
        _filePath = AppPaths.BookmarkTitlesFile;
        _owner = owner;
    }


    public bool IsReadOnly => _owner.IsYielding;


    public IReadOnlyDictionary<string, string> Load() {
        try {
            return File.Exists(_filePath)
                && JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(_filePath), _options) is { } titles
                    ? titles
                    : new Dictionary<string, string>();
        } catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) {
            // A damaged file is an empty one: the bookmarks read as their folders.
            return new Dictionary<string, string>();
        }
    }

    public void Save(IReadOnlyDictionary<string, string> titles) {
        if (IsReadOnly) {
            return;
        }

        try {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            string tmpPath = _filePath + ".tmp";
            File.WriteAllText(tmpPath, JsonSerializer.Serialize(titles, _options));
            File.Move(tmpPath, _filePath, overwrite: true);
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
            // Best effort, like folders.json.
        }
    }
}
