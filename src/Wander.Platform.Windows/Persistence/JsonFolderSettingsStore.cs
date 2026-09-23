using System.Text.Json;
using System.Text.Json.Serialization;
using Wander.Core.Folders;
using Wander.Core.Persistence;

namespace Wander.Platform.Windows.Persistence;

/// <summary>
/// <c>folders.json</c> in the data folder: the per-folder records behind
/// <see cref="IFolderSettingsStore"/>. The same shape of caution as
/// <see cref="JsonAppStateStore"/> - write-then-rename, a yielding instance
/// never writes, a file of a newer shape is read but not overwritten - and
/// its own <see cref="CurrentVersion"/>, because this file changes on its
/// own schedule.
/// </summary>
public sealed class JsonFolderSettingsStore : IFolderSettingsStore {
    /// <summary>The shape of the file; goes up when an older build would misread or drop what a newer one wrote.</summary>
    public const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions _options = new() {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _filePath;
    private readonly InstanceLock _owner;

    // The version of the file as loaded; a newer one than this build knows
    // is left alone on Save.
    private int _fileVersion;


    public JsonFolderSettingsStore(InstanceLock owner) {
        Directory.CreateDirectory(AppPaths.DataRoot);
        _filePath = AppPaths.FoldersFile;
        _owner = owner;
    }


    public bool IsReadOnly => _owner.IsYielding;


    public IReadOnlyList<FolderRecord> Load() {
        if (!File.Exists(_filePath)) {
            return Array.Empty<FolderRecord>();
        }

        try {
            string json = File.ReadAllText(_filePath);
            var file = JsonSerializer.Deserialize<FolderSettingsFile>(json, _options);
            if (file is null) {
                return Array.Empty<FolderRecord>();
            }
            _fileVersion = file.Version;

            return file.Folders ?? Array.Empty<FolderRecord>();
        } catch {
            return Array.Empty<FolderRecord>();
        }
    }

    public void Save(IReadOnlyList<FolderRecord> records) {
        if (IsReadOnly || _fileVersion > CurrentVersion) {
            return;
        }

        try {
            string json = JsonSerializer.Serialize(new FolderSettingsFile(CurrentVersion, records), _options);
            string tmpPath = _filePath + ".tmp";
            File.WriteAllText(tmpPath, json);
            File.Move(tmpPath, _filePath, overwrite: true);
            _fileVersion = CurrentVersion;
        } catch {
            // Best effort, like state.json: a pin that did not persist is
            // not worth a crash.
        }
    }


    private sealed record FolderSettingsFile(int Version, IReadOnlyList<FolderRecord>? Folders);
}
