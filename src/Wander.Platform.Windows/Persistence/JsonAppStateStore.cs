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


    public JsonAppStateStore() {
        string folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Wander");
        Directory.CreateDirectory(folder);
        _filePath = Path.Combine(folder, "state.json");
    }


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

    public void Save(AppState state) {
        try {
            // Write-then-rename so a crash mid-write can't leave a truncated
            // state.json — the old file stays intact until the new one is
            // fully on disk.
            string json = JsonSerializer.Serialize(state, _options);
            string tmpPath = _filePath + ".tmp";
            File.WriteAllText(tmpPath, json);
            File.Move(tmpPath, _filePath, overwrite: true);
        } catch {
            // best-effort: failure to persist must not crash the app
        }
    }
}
