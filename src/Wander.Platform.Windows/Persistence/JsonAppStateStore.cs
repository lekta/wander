using System.Text.Json;
using System.Text.Json.Serialization;
using Wander.Core.Logging;
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

    private readonly JsonDataFile _file;
    private readonly InstanceLock _owner;


    /// <param name="owner">
    /// Who owns the file - see <see cref="InstanceLock"/>. Asked before
    /// every write, never before a read: a yielding instance still starts
    /// from the state the owner saved.
    /// </param>
    public JsonAppStateStore(InstanceLock owner, ILogger log) {
        Directory.CreateDirectory(AppPaths.DataRoot);
        _file = new JsonDataFile(AppPaths.StateFile, log, AppState.CurrentVersion);
        _owner = owner;
    }


    public bool IsReadOnly => _owner.IsYielding;

    public StoreReadOutcome LastLoad => _file.LastOutcome;


    /// <summary>
    /// The file, or the defaults when there is none to use. One that did not
    /// read is not written over as it is (<see cref="JsonDataFile"/>).
    /// </summary>
    public AppState Load() {
        return _file.Read<AppState>(_options) ?? new AppState();
    }

    /// <summary>
    /// Writes, unless this build has no business writing this file: it is
    /// yielding to the instance that owns it, or the file is of a newer
    /// shape than this build knows (<see cref="AppState.CurrentVersion"/>).
    /// Every caller does read-modify-write, so the record
    /// handed here carries the shape of the file it came from.
    /// </summary>
    public bool Save(AppState state) {
        if (IsReadOnly || state.Version > AppState.CurrentVersion) {
            return false;
        }

        // Write-then-rename so a crash mid-write can't leave a truncated
        // state.json — the old file stays intact until the new one is
        // fully on disk. A failure is a WARN, never a crash.
        return _file.Write(JsonSerializer.Serialize(state with { Version = AppState.CurrentVersion }, _options));
    }
}
