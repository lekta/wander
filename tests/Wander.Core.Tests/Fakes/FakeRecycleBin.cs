using Wander.Core.FileSystem;

namespace Wander.Core.Tests.Fakes;

/// <summary>
/// In-memory recycle bin for tests: Send removes the entry from the fake fs
/// and records it; Restore puts it back. CallLog mirrors FakeFileSystem.
/// </summary>
internal sealed class FakeRecycleBin : IRecycleBin {
    private readonly FakeFileSystem _fs;
    private readonly Dictionary<RecycleHandle, (bool IsFile, byte[]? Bytes)> _bin = new();


    public FakeRecycleBin(FakeFileSystem fs) {
        _fs = fs;
    }


    public List<string> CallLog { get; } = new();

    /// <summary>How many more sends of a path fail "in use" before it goes; <see cref="int.MaxValue"/> never goes.</summary>
    public Dictionary<string, int> InUseFor { get; } = new(StringComparer.OrdinalIgnoreCase);


    public RecycleHandle Send(string path) {
        CallLog.Add($"Recycle:{path}");
        if (InUseFor.TryGetValue(path, out int left) && left > 0) {
            if (left != int.MaxValue) {
                InUseFor[path] = left - 1;
            }

            throw FileInUse.Error(path);
        }
        var handle = new RecycleHandle(path, DateTime.UtcNow);
        if (_fs.Files.TryGetValue(path, out var bytes)) {
            _bin[handle] = (true, bytes);
            _fs.Files.Remove(path);
        } else if (_fs.Directories.Remove(path)) {
            _bin[handle] = (false, null);
        } else {
            throw new FileNotFoundException("Cannot recycle missing path", path);
        }
        return handle;
    }

    public void Restore(RecycleHandle handle) {
        CallLog.Add($"Restore:{handle.OriginalPath}");
        if (!_bin.TryGetValue(handle, out var entry)) {
            throw new IOException($"Handle not found in fake bin: {handle.OriginalPath}");
        }
        if (entry.IsFile) {
            _fs.Files[handle.OriginalPath] = entry.Bytes!;
        } else {
            _fs.Directories.Add(handle.OriginalPath);
        }
        _bin.Remove(handle);
    }
}
