using System.Text.Json;
using Wander.Core.Persistence;
using Wander.Core.Pins;

namespace Wander.Platform.Windows.Persistence;

/// <summary>
/// <c>pins.json</c> in the data folder: the pins behind
/// <see cref="IPinStore"/>. The same shape of caution as
/// <see cref="JsonFolderSettingsStore"/> - write-then-rename, a yielding
/// instance never writes, a file of a newer shape is read but not
/// overwritten, a broken file reads as no pins - and its own
/// <see cref="CurrentVersion"/>.
/// </summary>
public sealed class JsonPinStore : IPinStore {
    /// <summary>The shape of the file; goes up when an older build would misread or drop what a newer one wrote.</summary>
    public const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions _options = new() {
        WriteIndented = true,
    };

    private readonly string _filePath;
    private readonly InstanceLock _owner;

    // The version of the file as loaded; a newer one than this build knows
    // is left alone on Save.
    private int _fileVersion;


    public JsonPinStore(InstanceLock owner) {
        Directory.CreateDirectory(AppPaths.DataRoot);
        _filePath = AppPaths.PinsFile;
        _owner = owner;
    }


    public bool IsReadOnly => _owner.IsYielding;


    public IReadOnlyList<PinFolder> Load() {
        if (!File.Exists(_filePath)) {
            return Array.Empty<PinFolder>();
        }

        try {
            string json = File.ReadAllText(_filePath);
            var file = JsonSerializer.Deserialize<PinsFile>(json, _options);
            if (file is null) {
                return Array.Empty<PinFolder>();
            }
            _fileVersion = file.Version;

            return file.Folders ?? Array.Empty<PinFolder>();
        } catch {
            return Array.Empty<PinFolder>();
        }
    }

    public void Save(IReadOnlyList<PinFolder> folders) {
        if (IsReadOnly || _fileVersion > CurrentVersion) {
            return;
        }

        try {
            string json = JsonSerializer.Serialize(new PinsFile(CurrentVersion, folders), _options);
            string tmpPath = _filePath + ".tmp";
            File.WriteAllText(tmpPath, json);
            File.Move(tmpPath, _filePath, overwrite: true);
            _fileVersion = CurrentVersion;
        } catch {
            // Best effort, like state.json: a pin that did not persist is
            // not worth a crash.
        }
    }


    private sealed record PinsFile(int Version, IReadOnlyList<PinFolder>? Folders);
}
