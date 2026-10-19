using Wander.Core;
using Wander.Core.FileSystem;
using Wander.Core.Localization;
using Wander.Core.Logging;
using Wander.Core.Shell;

namespace Wander.Platform.Windows.Shell;

/// <summary>
/// Read-only shell-namespace enumeration. Four namespaces live behind it:
/// the Recycle Bin (<see cref="ShellPaths.RecycleBin"/>) -
/// <see cref="ShellRecycleBinFolder"/> - and archives browsed as folders -
/// <see cref="ShellArchiveFolder"/> - both through <c>IShellItem</c>; the
/// computer's drives and devices (<see cref="ShellPaths.Computer"/>) -
/// <see cref="ComputerDrives"/>; and phones and cameras
/// (<see cref="DevicePath"/>) - <see cref="DeviceFolders"/>.
///
/// <para>
/// This class is the dispatcher and nothing else: which of them a path
/// belongs to, and the fact that none can be written to.
/// </para>
/// </summary>
public sealed class WindowsShellNamespace : IShellNamespace {
    private readonly ILogger _logger;
    private readonly ShellArchiveFolder _archives;
    private readonly ShellRecycleBinFolder _bin;
    private readonly ComputerDrives _computer;
    private readonly DeviceFolders? _devices;


    /// <summary>Without devices: the harness's own, for archives and the bin.</summary>
    public WindowsShellNamespace(ILogger logger) : this(logger, null) {
    }

    /// <param name="devices">Phones and cameras; null - the computer lists its drives only.</param>
    internal WindowsShellNamespace(ILogger logger, DeviceFolders? devices) {
        _logger = logger;
        _archives = new ShellArchiveFolder(logger);
        _bin = new ShellRecycleBinFolder(logger);
        _devices = devices;
        _computer = new ComputerDrives(devices);
    }


    public bool IsShellPath(string path) {
        return IsRecycleBin(path) || IsComputer(path) || DevicePath.Is(path) || ParseArchive(path) is not null;
    }

    /// <summary>
    /// The archive / inner-path split, and null for everything else - a
    /// real folder that happens to be called <c>backup.zip</c> included.
    /// That last check is what makes this safe to ask about any path: the
    /// split alone is pure string work, and only the disk can tell a
    /// container from a folder named like one.
    /// </summary>
    public ArchivePath? ParseArchive(string path) {
        if (DevicePath.Is(path)) {
            return null;
        }

        var archive = _archives.Parse(path);

        return archive is not null && File.Exists(archive.Archive) ? archive : null;
    }

    public bool CanNavigate(string path) {
        if (IsRecycleBin(path) || IsComputer(path)) {
            return true;
        }
        if (DevicePath.Is(path)) {
            return _devices?.GetEntry(path) is { Kind: EntryKind.Directory };
        }

        return ParseArchive(path) is not null && _archives.CanNavigate(path);
    }

    public long? SizeOf(string path) {
        if (DevicePath.Is(path)) {
            return _devices?.GetEntry(path) is { Kind: EntryKind.File } entry ? entry.Size : null;
        }

        return ParseArchive(path) is { IsRoot: false } ? _archives.SizeOf(path) : null;
    }

    public byte[]? ReadEntry(string path) {
        return ParseArchive(path) is { IsRoot: false } ? _archives.ReadEntry(path) : null;
    }

    public Task CopyOut(
        IReadOnlyList<CopyOutItem> items, string targetFolder,
        IProgress<string>? progress, CancellationToken ct,
        IProgress<CopyOutWork>? work = null) {
        if (items.Count > 0 && DevicePath.Is(items[0].Path)) {
            return Task.Run(() => CopyOffDevice(items, targetFolder, progress, ct), ct);
        }

        return Task.Run(() => _archives.CopyOut(items, targetFolder, progress, ct, work), ct);
    }

    public object? CreateDataObject(IReadOnlyList<string> paths, IReadOnlyList<string>? fileList = null) {
        if (paths.Count > 0 && DevicePath.Is(paths[0])) {
            // The shell's own names for them: whoever takes the drop - Explorer,
            // a photo editor - asks the shell for the bytes.
            var shellPaths = paths.Select(p => _devices?.ShellPathOf(p)).ToList();

            return shellPaths.Any(p => p is null) ? null : ShellDataObject.Create(shellPaths!, _logger);
        }

        return ShellDataObject.Create(paths, _logger, fileList);
    }

    /// <summary>
    /// The Recycle Bin's, the computer's and a device's own labels; null for
    /// an archive, whose path reads correctly as it stands and whose
    /// breadcrumbs are the ordinary ones - and for a place on a device, which
    /// reads by its folders' names.
    /// </summary>
    public string? GetDisplayName(string shellPath) {
        return IsRecycleBin(shellPath) ? Text.Get("SpecialFolderRecycleBin")
            : IsComputer(shellPath) ? Text.Get("ComputerHeader")
            : DevicePath.Parse(shellPath) is { IsRoot: true } device ? device.Device
            : null;
    }

    public IReadOnlyList<FileSystemEntry> Enumerate(
        string shellPath, CancellationToken ct = default, IProgress<IReadOnlyList<FileSystemEntry>>? portions = null) {
        if (IsRecycleBin(shellPath)) {
            return _bin.Enumerate(ct, portions);
        }
        if (IsComputer(shellPath)) {
            return _computer.Enumerate(ct);
        }
        if (DevicePath.Is(shellPath)) {
            return _devices?.Enumerate(shellPath, ct) ?? Array.Empty<FileSystemEntry>();
        }
        if (ParseArchive(shellPath) is not null) {
            return _archives.Enumerate(shellPath, ct);
        }

        return Array.Empty<FileSystemEntry>();
    }


    /// <summary>
    /// Copies off a device for the preview and "Open" - a scratch copy, as an
    /// archive entry's is (<see cref="TempExtraction"/>): no conflicts asked,
    /// the target was made for it. A folder comes with what is in it.
    /// </summary>
    private void CopyOffDevice(IReadOnlyList<CopyOutItem> items, string targetFolder, IProgress<string>? progress, CancellationToken ct) {
        if (_devices is null) {
            throw new IOException("No devices here");
        }

        var fs = ServiceLocator.Get<IFileSystem>();
        foreach (var item in items) {
            ct.ThrowIfCancellationRequested();
            var entry = _devices.GetEntry(item.Path) ?? throw new FileNotFoundException("Not on the device", item.Path);
            Copy(fs, entry, Path.Combine(targetFolder, item.NewName ?? entry.Name), ct);
            progress?.Report(item.Path);
        }
    }

    private void Copy(IFileSystem fs, FileSystemEntry entry, string destination, CancellationToken ct) {
        ct.ThrowIfCancellationRequested();
        if (entry.Kind == EntryKind.Directory) {
            fs.CreateDirectory(destination);
            foreach (var child in _devices!.Enumerate(entry.FullPath, ct)) {
                Copy(fs, child, Path.Combine(destination, child.Name), ct);
            }

            return;
        }

        using var stream = _devices!.OpenRead(entry.FullPath);
        fs.WriteFromStream(stream, entry.Size ?? stream.Length, destination,
            entry.ModifiedUtc == DateTime.MinValue ? null : entry.ModifiedUtc, null, ct);
    }

    private static bool IsRecycleBin(string path) {
        return string.Equals(path, ShellPaths.RecycleBin, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsComputer(string path) {
        return string.Equals(path, ShellPaths.Computer, StringComparison.OrdinalIgnoreCase);
    }
}
