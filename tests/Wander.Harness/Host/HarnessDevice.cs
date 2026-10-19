using System.IO;
using Wander.Core.FileSystem;
using Wander.Core.Shell;

namespace Wander.Harness.Host;

/// <summary>
/// A phone for the scenarios (PLAN Y4): no device is ever plugged into the
/// machine a run is on, and a run must not read the one somebody did plug
/// in. Its storage is a folder of the sandbox; its paths are a real
/// device's (<c>device:Harness Phone\Internal storage\...</c>), so the
/// listing, the panels, the import, its pause and the menus all take the
/// same paths they take with a phone.
///
/// <para>
/// The scenario plugs it in, unplugs it, plugs it in locked, and pulls the
/// cable in the middle of a file: <see cref="BreakAfter"/> makes the next
/// read of that file stop after so many bytes with the code a pulled cable
/// gives (0x8007001F, stand Y4a), the device gone at the same moment -
/// <see cref="WentAway"/> lets the runner tell the window, as Windows would.
/// The folders a phone's owner has no business in are marked the way
/// <c>DeviceFolders</c> marks them (<see cref="DeviceClutter"/>).
/// </para>
/// </summary>
public sealed class HarnessDevice : IDeviceContent {
    public const string DeviceName = "Harness Phone";

    public const string StorageName = "Internal storage";

    /// <summary>The interface path its notices carry - shaped like a phone's, with the portable-device class at the end.</summary>
    public const string InterfacePath = @"\\?\HARNESS#VID_0000&PID_0000#HARNESS#{6ac27878-a6fa-4155-ba85-f98f491d4f33}";

    private const int GenFailure = unchecked((int)0x8007001F);      // ERROR_GEN_FAILURE
    private const int NotConnected = unchecked((int)0x8007048F);    // ERROR_DEVICE_NOT_CONNECTED

    private readonly Lock _lock = new();
    private readonly Dictionary<string, long> _breakAfter = new(StringComparer.OrdinalIgnoreCase);
    private string? _folder;
    private DeviceState _state = DeviceState.Gone;


    /// <summary>The device went away under a read - the runner tells the window, on its thread.</summary>
    public event Action? WentAway;


    public bool ThroughShell { get; set; }

    public static string RootPath => DevicePath.RootOf(DeviceName);

    public static string StoragePath => RootPath + "\\" + StorageName;


    /// <summary>Plugged in, its storage standing for <paramref name="folder"/>; locked - plugged in with no storage to show.</summary>
    public void Plug(string folder, bool locked) {
        lock (_lock) {
            _folder = folder;
            _state = locked ? DeviceState.Locked : DeviceState.Ready;
        }
    }

    public void Unplug() {
        lock (_lock) {
            _state = DeviceState.Gone;
        }
    }

    /// <summary>The next read of <paramref name="devicePath"/> stops after <paramref name="bytes"/>, and the device is gone.</summary>
    public void BreakAfter(string devicePath, long bytes) {
        lock (_lock) {
            _breakAfter[devicePath] = bytes;
        }
    }

    /// <summary>
    /// The folder on disk a place on the phone stands for; null for the
    /// device itself, a path on another device and anything else. Known
    /// whether or not it is plugged in - it is where the runner may write.
    /// </summary>
    public string? DiskPathOf(string path) {
        if (DevicePath.Parse(path) is not { IsRoot: false } parsed || !Same(parsed.Device, DeviceName)
            || !Same(parsed.Storage!, StorageName) || _folder is not { } folder) {
            return null;
        }

        return parsed.InStorage.Count == 0 ? folder : Path.Combine(new[] { folder }.Concat(parsed.InStorage).ToArray());
    }

    public IReadOnlyList<FileSystemEntry> Devices() {
        return State == DeviceState.Gone ? Array.Empty<FileSystemEntry>() : new[] { Folder(DeviceName, RootPath) };
    }

    public FileSystemEntry? GetEntry(string path) {
        if (DevicePath.Parse(path) is not { } parsed || !Same(parsed.Device, DeviceName) || State == DeviceState.Gone) {
            return null;
        }
        if (parsed.IsRoot) {
            return Folder(DeviceName, RootPath);
        }
        if (State != DeviceState.Ready || DiskPathOf(path) is not { } disk) {
            return null;
        }
        if (parsed.InStorage.Count == 0) {
            return Folder(StorageName, StoragePath);
        }

        return Directory.Exists(disk) || File.Exists(disk) ? Row(path, parsed.InStorage, disk, null) : null;
    }

    public IReadOnlyList<FileSystemEntry> Enumerate(string path, CancellationToken ct = default) {
        var parsed = DevicePath.Parse(path) ?? throw new DirectoryNotFoundException(path);
        if (!Same(parsed.Device, DeviceName) || State == DeviceState.Gone) {
            throw new IOException($"The device is not connected: {parsed.Device}", NotConnected);
        }
        if (parsed.IsRoot) {
            return State == DeviceState.Locked ? Array.Empty<FileSystemEntry>() : new[] { Folder(StorageName, StoragePath) };
        }
        if (State != DeviceState.Ready || DiskPathOf(path) is not { } disk || !Directory.Exists(disk)) {
            throw new DirectoryNotFoundException($"Not on the device: {path}");
        }

        var rows = new List<FileSystemEntry>();
        foreach (string child in Directory.EnumerateFileSystemEntries(disk)) {
            ct.ThrowIfCancellationRequested();
            string name = Path.GetFileName(child);
            var segments = parsed.InStorage.Append(name).ToArray();
            rows.Add(Row(Path.Combine(path, name), segments, child, parsed.InStorage.Count == 0 ? EmptyAtRoot(child, name) : null));
        }

        return rows;
    }

    public Stream OpenRead(string path) {
        if (State != DeviceState.Ready) {
            throw new IOException("The device is not connected", NotConnected);
        }
        if (DiskPathOf(path) is not { } disk || !File.Exists(disk)) {
            throw new FileNotFoundException("Not on the device", path);
        }

        long? after;
        lock (_lock) {
            after = _breakAfter.Remove(path, out long bytes) ? bytes : null;
        }
        var stream = new FileStream(disk, FileMode.Open, FileAccess.Read, FileShare.Read);

        return after is { } limit ? new BreakingStream(stream, limit, OnBroke) : stream;
    }

    public byte[]? ReadThumbnail(string path) {
        // Most phones keep one; this one does not, so the thumbnail is the
        // picture read whole - the other half of DeviceThumbnail.
        return null;
    }

    public DeviceState StateOf(string path) {
        return DevicePath.Parse(path) is { } parsed && Same(parsed.Device, DeviceName) ? State : DeviceState.Gone;
    }


    private DeviceState State {
        get {
            lock (_lock) {
                return _state;
            }
        }
    }

    private void OnBroke() {
        Unplug();
        WentAway?.Invoke();
    }

    /// <summary>A standard folder at the root holds nothing but dot-names - asked only of those, as DeviceFolders asks.</summary>
    private static bool? EmptyAtRoot(string disk, string name) {
        if (!Directory.Exists(disk) || !DeviceClutter.IsEmptyStandard(name)) {
            return null;
        }

        return DeviceClutter.LooksEmpty(Directory.EnumerateFileSystemEntries(disk).Select(Path.GetFileName)!);
    }

    private static FileSystemEntry Row(string path, IReadOnlyList<string> segments, string disk, bool? empty) {
        bool folder = Directory.Exists(disk);
        bool clutter = DeviceClutter.IsClutter(segments, folder ? empty : null);
        var info = folder ? (FileSystemInfo)new DirectoryInfo(disk) : new FileInfo(disk);

        return new FileSystemEntry(
            Name: Path.GetFileName(disk),
            FullPath: path,
            Kind: folder ? EntryKind.Directory : EntryKind.File,
            Size: folder ? null : ((FileInfo)info).Length,
            ModifiedUtc: info.LastWriteTimeUtc,
            IsHidden: clutter,
            IsReadOnly: false,
            IsSystem: clutter,
            LinksToDirectory: false);
    }

    private static FileSystemEntry Folder(string name, string path) {
        return new FileSystemEntry(name, path, EntryKind.Directory, null, DateTime.MinValue, false, false, false, false);
    }

    private static bool Same(string a, string b) {
        return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }


    /// <summary>Gives so many bytes of the file, then breaks the way a pulled cable breaks a read.</summary>
    private sealed class BreakingStream : Stream {
        private readonly FileStream _inner;
        private readonly long _after;
        private readonly Action _broke;


        public BreakingStream(FileStream inner, long after, Action broke) {
            _inner = inner;
            _after = after;
            _broke = broke;
        }


        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => _inner.Length;

        public override long Position {
            get => _inner.Position;
            set => throw new NotSupportedException();
        }


        public override int Read(byte[] buffer, int offset, int count) {
            if (_inner.Position >= _after) {
                _broke();

                throw new IOException("A device attached to the system is not functioning.", GenFailure);
            }

            return _inner.Read(buffer, offset, (int)Math.Min(count, _after - _inner.Position));
        }

        public override void Flush() {
        }

        public override long Seek(long offset, SeekOrigin origin) {
            throw new NotSupportedException();
        }

        public override void SetLength(long value) {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count) {
            throw new NotSupportedException();
        }


        protected override void Dispose(bool disposing) {
            if (disposing) {
                _inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
