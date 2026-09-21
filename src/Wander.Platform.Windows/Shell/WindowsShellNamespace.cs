using Wander.Core.FileSystem;
using Wander.Core.Localization;
using Wander.Core.Logging;
using Wander.Core.Shell;

namespace Wander.Platform.Windows.Shell;

/// <summary>
/// Read-only shell-namespace enumeration. Two namespaces live behind it:
/// the Recycle Bin (<see cref="ShellPaths.RecycleBin"/>) -
/// <see cref="ShellRecycleBinFolder"/> - and archives browsed as folders -
/// <see cref="ShellArchiveFolder"/>. Both go through <c>IShellItem</c>.
///
/// <para>
/// This class is the dispatcher and nothing else: which of the two a path
/// belongs to, and the fact that neither can be written to.
/// </para>
/// </summary>
public sealed class WindowsShellNamespace : IShellNamespace {
    private readonly ILogger _logger;
    private readonly ShellArchiveFolder _archives;
    private readonly ShellRecycleBinFolder _bin;


    public WindowsShellNamespace(ILogger logger) {
        _logger = logger;
        _archives = new ShellArchiveFolder(logger);
        _bin = new ShellRecycleBinFolder(logger);
    }


    public bool IsShellPath(string path) {
        return IsRecycleBin(path) || ParseArchive(path) is not null;
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
        if (IsRecycleBin(path)) {
            return true;
        }

        return ParseArchive(path) is not null && _archives.CanNavigate(path);
    }

    public Task CopyOut(
        IReadOnlyList<CopyOutItem> items, string targetFolder,
        IProgress<string>? progress, CancellationToken ct,
        IProgress<CopyOutWork>? work = null) {
        return Task.Run(() => _archives.CopyOut(items, targetFolder, progress, ct, work), ct);
    }

    public object? CreateDataObject(IReadOnlyList<string> paths) {
        return ShellDataObject.Create(paths, _logger);
    }

    /// <summary>
    /// The Recycle Bin's own label; null for an archive, whose path reads
    /// correctly as it stands and whose breadcrumbs are the ordinary ones.
    /// </summary>
    public string? GetDisplayName(string shellPath) {
        return IsRecycleBin(shellPath) ? Text.Get("SpecialFolderRecycleBin") : null;
    }

    public IReadOnlyList<FileSystemEntry> Enumerate(string shellPath, CancellationToken ct = default) {
        if (IsRecycleBin(shellPath)) {
            return _bin.Enumerate(ct);
        }
        if (ParseArchive(shellPath) is not null) {
            return _archives.Enumerate(shellPath, ct);
        }

        return Array.Empty<FileSystemEntry>();
    }


    private static bool IsRecycleBin(string path) {
        return string.Equals(path, ShellPaths.RecycleBin, StringComparison.OrdinalIgnoreCase);
    }
}
