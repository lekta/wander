using Wander.Core.FileSystem;
using Wander.Core.Shell;

namespace Wander.Core.Tests.Fakes;

/// <summary>
/// A phone in memory: folders and files by <see cref="DevicePath"/>, and a
/// cable that can be pulled. <see cref="BreakAfter"/> makes a read of a file
/// stop after so many bytes the way a pulled cable stops it (0x8007001F)
/// and takes the device away; <see cref="BackAfter"/> brings it back after
/// so many looks at it - the import's poll, without any time passing.
/// </summary>
internal sealed class FakeDeviceContent : IDeviceContent {
    private const int GenFailure = unchecked((int)0x8007001F);

    private readonly Dictionary<string, (byte[] Bytes, DateTime Modified)> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _folders = new(StringComparer.OrdinalIgnoreCase);


    public bool ThroughShell { get; set; }

    /// <summary>How the device answers now.</summary>
    public DeviceState State { get; set; } = DeviceState.Ready;

    /// <summary>A read of the file stops after this many bytes, once, and the device goes.</summary>
    public Dictionary<string, int> BreakAfter { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>What reads of a file throw, one per read, before one goes through - the device staying there.</summary>
    public Dictionary<string, Queue<Exception>> ReadFailures { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>A device that went comes back once it has been looked at this many times; null - never.</summary>
    public int? BackAfter { get; set; }

    /// <summary>The state it comes back in - a phone may come back locked first.</summary>
    public DeviceState BackAs { get; set; } = DeviceState.Ready;

    /// <summary>Every file opened, in order.</summary>
    public List<string> Opened { get; } = new();

    public int StateLooks { get; private set; }


    public void AddFolder(string path) {
        _folders.Add(path);
    }

    public void AddFile(string path, string content, DateTime modifiedUtc) {
        _files[path] = (System.Text.Encoding.UTF8.GetBytes(content), modifiedUtc);
    }

    public void AddFile(string path, byte[] bytes, DateTime modifiedUtc) {
        _files[path] = (bytes, modifiedUtc);
    }

    public IReadOnlyList<FileSystemEntry> Devices() {
        return Array.Empty<FileSystemEntry>();
    }

    public FileSystemEntry? GetEntry(string path) {
        if (State != DeviceState.Ready) {
            return null;
        }
        if (_folders.Contains(path)) {
            return Folder(path);
        }

        return _files.TryGetValue(path, out var file) ? File(path, file.Bytes, file.Modified) : null;
    }

    public IReadOnlyList<FileSystemEntry> Enumerate(string path, CancellationToken ct = default) {
        if (State != DeviceState.Ready) {
            throw new IOException("The device is gone", GenFailure);
        }

        var rows = new List<FileSystemEntry>();
        rows.AddRange(_folders.Where(f => IsChild(f, path)).Select(Folder));
        rows.AddRange(_files.Where(f => IsChild(f.Key, path)).Select(f => File(f.Key, f.Value.Bytes, f.Value.Modified)));

        return rows;
    }

    public Stream OpenRead(string path) {
        Opened.Add(path);
        if (State != DeviceState.Ready) {
            throw new IOException("The device is gone", GenFailure);
        }
        if (ReadFailures.TryGetValue(path, out var failures) && failures.Count > 0) {
            throw failures.Dequeue();
        }
        if (!_files.TryGetValue(path, out var file)) {
            throw new FileNotFoundException("No such file on the device", path);
        }

        if (BreakAfter.Remove(path, out int after)) {
            return new BreakingStream(file.Bytes, after, () => State = DeviceState.Gone);
        }

        return new MemoryStream(file.Bytes, writable: false);
    }

    public byte[]? ReadThumbnail(string path) {
        return null;
    }

    public DeviceState StateOf(string path) {
        StateLooks++;
        if (State != DeviceState.Ready && BackAfter is { } left) {
            if (left <= 0) {
                State = BackAs;
                BackAs = DeviceState.Ready;
            } else {
                BackAfter = left - 1;
            }
        }

        return State;
    }


    private static bool IsChild(string path, string folder) {
        return string.Equals(Path.GetDirectoryName(path), folder, StringComparison.OrdinalIgnoreCase);
    }

    private static FileSystemEntry Folder(string path) {
        return new FileSystemEntry(Path.GetFileName(path), path, EntryKind.Directory, null, DateTime.MinValue, false, false, false, false);
    }

    private static FileSystemEntry File(string path, byte[] bytes, DateTime modified) {
        return new FileSystemEntry(Path.GetFileName(path), path, EntryKind.File, bytes.Length, modified, false, false, false, false);
    }


    /// <summary>Gives so many bytes, then breaks the way a pulled cable breaks a read - and the device is gone.</summary>
    private sealed class BreakingStream : MemoryStream {
        private readonly int _after;
        private readonly Action _gone;

        public BreakingStream(byte[] bytes, int after, Action gone) : base(bytes, writable: false) {
            _after = after;
            _gone = gone;
        }

        public override int Read(byte[] buffer, int offset, int count) {
            if (Position >= _after) {
                _gone();

                throw new IOException("A device attached to the system is not functioning.", GenFailure);
            }

            return base.Read(buffer, offset, (int)Math.Min(count, _after - Position));
        }
    }
}
