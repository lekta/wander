using System.Text.Json;
using Wander.Core.Persistence;

namespace Wander.Platform.Windows.Persistence;

public sealed class JsonAppStateStore : IAppStateStore {
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
            return JsonSerializer.Deserialize<AppState>(json) ?? new AppState();
        } catch {
            return new AppState();
        }
    }

    public void Save(AppState state) {
        try {
            string json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_filePath, json);
        } catch {
            // best-effort: failure to persist must not crash the app
        }
    }
}
