using System.Text.Json;
using System.Text.Json.Serialization;
using Wander.Core.Persistence;

namespace Wander.Platform.Windows.Persistence;

public sealed class JsonAppStateStore : IAppStateStore {
    private static readonly JsonSerializerOptions _options = new() {
        WriteIndented = true,
        // Write NavigationSource (and any other enum) as its name string,
        // not as a numeric index. Makes state.json hand-readable and
        // resilient to enum-value reordering in the source.
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _filePath;
    private readonly InstanceLock _owner;


    /// <param name="owner">
    /// Who owns the file - see <see cref="InstanceLock"/>. Asked before
    /// every write, never before a read: a yielding instance still starts
    /// from the state the owner saved.
    /// </param>
    public JsonAppStateStore(InstanceLock owner) {
        Directory.CreateDirectory(AppPaths.DataRoot);
        _filePath = AppPaths.StateFile;
        _owner = owner;
    }


    public bool IsReadOnly => _owner.IsYielding;


    public AppState Load() {
        if (!File.Exists(_filePath)) {
            return new AppState();
        }

        try {
            string json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<AppState>(json, _options) ?? new AppState();
        } catch {
            return new AppState();
        }
    }

    /// <summary>
    /// Writes, unless this build has no business writing this file: it is
    /// yielding to the instance that owns it, or the file is of a newer
    /// shape than this build knows (<see cref="AppState.CurrentVersion"/>,
    /// PLAN AD11). Every caller does read-modify-write, so the record
    /// handed here carries the shape of the file it came from.
    /// </summary>
    public void Save(AppState state) {
        if (IsReadOnly || state.Version > AppState.CurrentVersion) {
            return;
        }

        try {
            // Write-then-rename so a crash mid-write can't leave a truncated
            // state.json — the old file stays intact until the new one is
            // fully on disk.
            string json = JsonSerializer.Serialize(state with { Version = AppState.CurrentVersion }, _options);
            string tmpPath = _filePath + ".tmp";
            File.WriteAllText(tmpPath, json);
            File.Move(tmpPath, _filePath, overwrite: true);
        } catch {
            // best-effort: failure to persist must not crash the app
        }
    }
}
