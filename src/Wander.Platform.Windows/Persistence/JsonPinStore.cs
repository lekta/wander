using System.Text.Json;
using Wander.Core.Logging;
using Wander.Core.Persistence;
using Wander.Core.Pins;

namespace Wander.Platform.Windows.Persistence;

/// <summary>
/// <c>pins.json</c> in the data folder: the pins behind
/// <see cref="IPinStore"/>. The same shape of caution as
/// <see cref="JsonFolderSettingsStore"/> - write-then-rename, a yielding
/// instance never writes, a file of a newer shape is read but not
/// overwritten, a file that did not read is not written over as it is
/// (<see cref="JsonDataFile"/>) - and its own <see cref="CurrentVersion"/>.
/// </summary>
public sealed class JsonPinStore : IPinStore {
    /// <summary>The shape of the file; goes up when an older build would misread or drop what a newer one wrote.</summary>
    public const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions _options = new() {
        WriteIndented = true,
    };

    private readonly JsonDataFile _file;
    private readonly InstanceLock _owner;


    public JsonPinStore(InstanceLock owner, ILogger log) {
        Directory.CreateDirectory(AppPaths.DataRoot);
        _file = new JsonDataFile(AppPaths.PinsFile, log, CurrentVersion);
        _owner = owner;
    }


    public bool IsReadOnly => _owner.IsYielding;


    public IReadOnlyList<PinFolder> Load() {
        return _file.Read<PinsFile>(_options)?.Folders ?? Array.Empty<PinFolder>();
    }

    public void Save(IReadOnlyList<PinFolder> folders) {
        if (IsReadOnly) {
            return;
        }

        // Best effort, like state.json: a pin that did not persist is a
        // WARN, not a crash.
        _file.Write(JsonSerializer.Serialize(new PinsFile(CurrentVersion, folders), _options));
    }


    private sealed record PinsFile(int Version, IReadOnlyList<PinFolder>? Folders);
}
