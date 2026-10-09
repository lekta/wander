using System.Text.Json;
using Wander.Core.Persistence;
using Wander.Core.Preview;

namespace Wander.Platform.Windows.Persistence;

/// <summary>
/// <c>model-views.json</c> in the data folder: the views of
/// <see cref="IModelViews"/>. Read on first use, held in memory - tiles
/// ask from pool threads - and written whole after each change, on the
/// pool, write-then-rename; a yielding instance never writes. The oldest
/// views go past <see cref="MaxViews"/>.
/// </summary>
public sealed class JsonModelViewStore : IModelViews {
    private const int MaxViews = 4000;

    private static readonly JsonSerializerOptions _options = new() { WriteIndented = false };

    private readonly string _filePath;
    private readonly InstanceLock _owner;
    private readonly Lock _lock = new();
    private readonly Lock _writeLock = new();

    // Path (lower case) -> view and when it was set, for dropping the oldest.
    private Dictionary<string, Entry>? _views;
    private long _clock;

    // The newest view on disk: a write queued behind a later one is dropped.
    private long _written;


    public JsonModelViewStore(InstanceLock owner) {
        _filePath = AppPaths.ModelViewsFile;
        _owner = owner;
    }


    public ModelView? Get(string path) {
        lock (_lock) {
            return Views().TryGetValue(Key(path), out var entry) ? new ModelView(entry.Spin, entry.Tilt) : null;
        }
    }

    public void Set(string path, ModelView view) {
        Entry[] snapshot;
        lock (_lock) {
            var views = Views();
            views[Key(path)] = new Entry(view.Spin, view.Tilt, ++_clock);
            if (views.Count > MaxViews) {
                foreach (var old in views.OrderBy(v => v.Value.Stamp).Take(views.Count - MaxViews).Select(v => v.Key).ToList()) {
                    views.Remove(old);
                }
            }
            snapshot = views.Select(v => v.Value with { Path = v.Key }).ToArray();
        }
        if (_owner.IsYielding) {
            return;
        }

        _ = Task.Run(() => Save(snapshot));
    }


    private Dictionary<string, Entry> Views() {
        if (_views is not null) {
            return _views;
        }

        _views = new Dictionary<string, Entry>(StringComparer.Ordinal);
        try {
            if (File.Exists(_filePath) && JsonSerializer.Deserialize<Entry[]>(File.ReadAllText(_filePath), _options) is { } entries) {
                foreach (var entry in entries.Where(e => !string.IsNullOrEmpty(e.Path))) {
                    _views[entry.Path!] = entry;
                    _clock = Math.Max(_clock, entry.Stamp);
                }
            }
        } catch {
            // A damaged file is an empty one: views are a convenience.
        }

        return _views;
    }

    private void Save(Entry[] entries) {
        lock (_writeLock) {
            long newest = entries.Length == 0 ? 0 : entries.Max(e => e.Stamp);
            if (newest <= _written) {
                return;
            }

            try {
                Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
                string tmpPath = _filePath + ".tmp";
                File.WriteAllText(tmpPath, JsonSerializer.Serialize(entries, _options));
                File.Move(tmpPath, _filePath, overwrite: true);
                _written = newest;
            } catch {
                // Best effort, like folders.json.
            }
        }
    }

    private static string Key(string path) {
        return path.ToLowerInvariant();
    }


    private sealed record Entry(double Spin, double Tilt, long Stamp) {
        public string? Path { get; init; }
    }
}
