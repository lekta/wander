using Wander.Core.FileSystem;
using Wander.Core.Localization;
using Wander.Core.Logging;
using Wander.Core.Shell;

namespace Wander.Platform.Windows.Shell;

/// <summary>
/// Read-only shell-namespace enumeration. Three namespaces live behind it:
/// the Recycle Bin (<see cref="ShellPaths.RecycleBin"/>) -
/// <see cref="ShellRecycleBinFolder"/> - and archives browsed as folders -
/// <see cref="ShellArchiveFolder"/> - both through <c>IShellItem</c>; and
/// the computer's drives (<see cref="ShellPaths.Computer"/>) -
/// <see cref="ComputerDrives"/>.
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
    private readonly ComputerDrives _computer = new();


    public WindowsShellNamespace(ILogger logger) {
        _logger = logger;
        _archives = new ShellArchiveFolder(logger);
        _bin = new ShellRecycleBinFolder(logger);
    }


    public bool IsShellPath(string path) {
        return IsRecycleBin(path) || IsComputer(path) || ParseArchive(path) is not null;
    }

    /// <summary>
    /// The archive / inner-path split, and null for everything else - a
    /// real folder that happens to be called <c>backup.zip</c> included.
    /// That last check is what makes this safe to ask about any path: the
    /// split alone is pure string work, and only the disk can tell a
    /// container from a folder named like one.
    /// </summary>
    public ArchivePath? ParseArchive(string path) {
        var archive = _archives.Parse(path);

        return archive is not null && File.Exists(archive.Archive) ? archive : null;
    }

    public bool CanNavigate(string path) {
        if (IsRecycleBin(path) || IsComputer(path)) {
            return true;
        }

        return ParseArchive(path) is not null && _archives.CanNavigate(path);
    }

    public long? SizeOf(string path) {
        return ParseArchive(path) is { IsRoot: false } ? _archives.SizeOf(path) : null;
    }

    public byte[]? ReadEntry(string path) {
        return ParseArchive(path) is { IsRoot: false } ? _archives.ReadEntry(path) : null;
    }

    public Task CopyOut(
        IReadOnlyList<CopyOutItem> items, string targetFolder,
        IProgress<string>? progress, CancellationToken ct,
        IProgress<CopyOutWork>? work = null) {
        return Task.Run(() => _archives.CopyOut(items, targetFolder, progress, ct, work), ct);
    }

    public object? CreateDataObject(IReadOnlyList<string> paths, IReadOnlyList<string>? fileList = null) {
        return ShellDataObject.Create(paths, _logger, fileList);
    }

    /// <summary>
    /// The Recycle Bin's and the computer's own labels; null for an archive,
    /// whose path reads correctly as it stands and whose breadcrumbs are the
    /// ordinary ones.
    /// </summary>
    public string? GetDisplayName(string shellPath) {
        return IsRecycleBin(shellPath) ? Text.Get("SpecialFolderRecycleBin")
            : IsComputer(shellPath) ? Text.Get("ComputerHeader")
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
        if (ParseArchive(shellPath) is not null) {
            return _archives.Enumerate(shellPath, ct);
        }

        return Array.Empty<FileSystemEntry>();
    }


    private static bool IsRecycleBin(string path) {
        return string.Equals(path, ShellPaths.RecycleBin, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsComputer(string path) {
        return string.Equals(path, ShellPaths.Computer, StringComparison.OrdinalIgnoreCase);
    }
}
