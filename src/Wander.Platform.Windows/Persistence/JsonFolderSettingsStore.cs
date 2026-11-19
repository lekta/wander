using System.Text.Json;
using System.Text.Json.Serialization;
using Wander.Core.Folders;
using Wander.Core.Logging;
using Wander.Core.Persistence;

namespace Wander.Platform.Windows.Persistence;

/// <summary>
/// <c>folders.json</c> in the data folder: the per-folder records behind
/// <see cref="IFolderSettingsStore"/>. The same shape of caution as
/// <see cref="JsonAppStateStore"/> - write-then-rename, a yielding instance
/// never writes, a file of a newer shape is read but not overwritten, a
/// file that did not read is not written over as it is
/// (<see cref="JsonDataFile"/>) - and its own <see cref="CurrentVersion"/>,
/// because this file changes on its own schedule.
/// </summary>
public sealed class JsonFolderSettingsStore : IFolderSettingsStore {
    /// <summary>The shape of the file; goes up when an older build would misread or drop what a newer one wrote.</summary>
    public const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions _options = new() {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly JsonDataFile _file;
    private readonly InstanceLock _owner;


    public JsonFolderSettingsStore(InstanceLock owner, ILogger log) {
        Directory.CreateDirectory(AppPaths.DataRoot);
        _file = new JsonDataFile(AppPaths.FoldersFile, log, CurrentVersion);
        _owner = owner;
    }


    public bool IsReadOnly => _owner.IsYielding;


    public IReadOnlyList<FolderRecord> Load() {
        return _file.Read<FolderSettingsFile>(_options)?.Folders ?? Array.Empty<FolderRecord>();
    }

    public void Save(IReadOnlyList<FolderRecord> records) {
        if (IsReadOnly) {
            return;
        }

        // Best effort, like state.json: a record that did not persist is a
        // WARN, not a crash.
        _file.Write(JsonSerializer.Serialize(new FolderSettingsFile(CurrentVersion, records), _options));
    }


    private sealed record FolderSettingsFile(int Version, IReadOnlyList<FolderRecord>? Folders);
}
