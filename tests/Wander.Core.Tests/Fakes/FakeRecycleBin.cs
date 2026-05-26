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


    public RecycleHandle Send(string path) {
        CallLog.Add($"Recycle:{path}");
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
