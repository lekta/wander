using System.IO;
using Wander.Core;
using Wander.Core.FileSystem;
using Wander.Core.Shell;

namespace Wander.Harness.Host;

/// <summary>
/// The machine's own shell namespace with the phone of the scenarios in
/// place of real devices: the bin, archives and the drives are the shell's
/// as ever; a place on a device is <see cref="HarnessDevice"/>'s, and the
/// computer's listing shows that phone after the drives - never one that is
/// really plugged in, which a run has no business reading.
/// </summary>
public sealed class HarnessShellNamespace : IShellNamespace {
    private readonly IShellNamespace _inner;
    private readonly HarnessDevice _device;


    public HarnessShellNamespace(IShellNamespace inner, HarnessDevice device) {
        _inner = inner;
        _device = device;
    }


    public bool IsShellPath(string path) {
        return DevicePath.Is(path) || _inner.IsShellPath(path);
    }

    public IReadOnlyList<FileSystemEntry> Enumerate(
        string shellPath, CancellationToken ct = default, IProgress<IReadOnlyList<FileSystemEntry>>? portions = null) {
        if (DevicePath.Is(shellPath)) {
            return _device.Enumerate(shellPath, ct);
        }
        if (string.Equals(shellPath, ShellPaths.Computer, StringComparison.OrdinalIgnoreCase)) {
            return _inner.Enumerate(shellPath, ct, portions).Where(e => !DevicePath.Is(e.FullPath)).Concat(_device.Devices()).ToList();
        }

        return _inner.Enumerate(shellPath, ct, portions);
    }

    public string? GetDisplayName(string shellPath) {
        return DevicePath.Parse(shellPath) is { } device
            ? device.IsRoot ? device.Device : null
            : _inner.GetDisplayName(shellPath);
    }

    public ArchivePath? ParseArchive(string path) {
        return _inner.ParseArchive(path);
    }

    public bool CanNavigate(string path) {
        return DevicePath.Is(path) ? _device.GetEntry(path) is { Kind: EntryKind.Directory } : _inner.CanNavigate(path);
    }

    public long? SizeOf(string path) {
        return DevicePath.Is(path) ? _device.GetEntry(path)?.Size : _inner.SizeOf(path);
    }

    public byte[]? ReadEntry(string path) {
        return DevicePath.Is(path) ? null : _inner.ReadEntry(path);
    }

    public Task CopyOut(
        IReadOnlyList<CopyOutItem> items, string targetFolder, IProgress<string>? progress, CancellationToken ct,
        IProgress<CopyOutWork>? work = null) {
        if (items.Count == 0 || !DevicePath.Is(items[0].Path)) {
            return _inner.CopyOut(items, targetFolder, progress, ct, work);
        }

        // The scratch copy behind the preview and "Open", as the real branch makes it.
        return Task.Run(() => {
            var fs = ServiceLocator.Get<IFileSystem>();
            foreach (var item in items) {
                var entry = _device.GetEntry(item.Path) ?? throw new FileNotFoundException("Not on the device", item.Path);
                using var stream = _device.OpenRead(item.Path);
                fs.WriteFromStream(stream, entry.Size ?? stream.Length, Path.Combine(targetFolder, item.NewName ?? entry.Name),
                    entry.ModifiedUtc, null, ct);
                progress?.Report(item.Path);
            }
        }, ct);
    }

    public object? CreateDataObject(IReadOnlyList<string> paths, IReadOnlyList<string>? fileList = null) {
        // No shell object for a phone the shell has never heard of: Wander's
        // own paste goes by the paths.
        return paths.Any(DevicePath.Is) ? null : _inner.CreateDataObject(paths, fileList);
    }
}
