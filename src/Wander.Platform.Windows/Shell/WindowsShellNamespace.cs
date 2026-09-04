using System.Globalization;
using Wander.Core.FileSystem;
using Wander.Core.Localization;
using Wander.Core.Logging;
using Wander.Core.Shell;

namespace Wander.Platform.Windows.Shell;

/// <summary>
/// Read-only shell-namespace enumeration. Two namespaces live behind it:
/// the Recycle Bin (<see cref="ShellPaths.RecycleBin"/>), which keeps the
/// <c>Shell.Application</c> dynamic-COM dance <c>ShellRecycleBin.Restore</c>
/// uses, and archives browsed as folders, which go through
/// <see cref="ShellArchiveFolder"/> and <c>IShellItem</c> instead - that
/// same <c>Shell.Application</c> reports 0 for the size and 1899 for the
/// date of every entry inside a <c>.7z</c>.
///
/// <para>
/// This class is the dispatcher and nothing else: which of the two a path
/// belongs to, and the fact that neither can be written to.
/// </para>
/// </summary>
public sealed class WindowsShellNamespace : IShellNamespace {
    // Shell special-folder constant CSIDL_BITBUCKET = 0xA. Same value that
    // Shell.NameSpace(int) takes for ssfBITBUCKET.
    private const int SsfBitBucket = 0xA;

    private readonly ILogger _logger;
    private readonly ShellArchiveFolder _archives;


    public WindowsShellNamespace(ILogger logger) {
        _logger = logger;
        _archives = new ShellArchiveFolder(logger);
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
        IProgress<string>? progress, CancellationToken ct) {
        return Task.Run(() => _archives.CopyOut(items, targetFolder, progress, ct), ct);
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

    public IReadOnlyList<FileSystemEntry> Enumerate(string shellPath) {
        if (IsRecycleBin(shellPath)) {
            return EnumerateRecycleBin();
        }
        if (ParseArchive(shellPath) is not null) {
            return _archives.Enumerate(shellPath);
        }

        return Array.Empty<FileSystemEntry>();
    }


    private static bool IsRecycleBin(string path) {
        return string.Equals(path, ShellPaths.RecycleBin, StringComparison.OrdinalIgnoreCase);
    }


    private IReadOnlyList<FileSystemEntry> EnumerateRecycleBin() {
        Type? shellType = Type.GetTypeFromProgID("Shell.Application");
        if (shellType is null) {
            _logger.Warn("Shell.Application COM type not available — recycle bin will appear empty.");
            return Array.Empty<FileSystemEntry>();
        }

        dynamic? shell = Activator.CreateInstance(shellType);
        if (shell is null) {
            _logger.Warn("Failed to instantiate Shell.Application — recycle bin will appear empty.");
            return Array.Empty<FileSystemEntry>();
        }

        dynamic? bin = shell.NameSpace(SsfBitBucket);
        if (bin is null) {
            _logger.Warn("Could not open recycle bin namespace.");
            return Array.Empty<FileSystemEntry>();
        }

        dynamic items;
        try {
            items = bin.Items();
        } catch (Exception ex) {
            _logger.Error("Recycle bin: bin.Items() failed.", ex);
            return Array.Empty<FileSystemEntry>();
        }

        var result = new List<FileSystemEntry>();
        int count;
        try {
            count = (int)items.Count;
        } catch {
            return Array.Empty<FileSystemEntry>();
        }

        for (int i = 0; i < count; i++) {
            FileSystemEntry? entry;
            try {
                dynamic item = items.Item(i);
                entry = BuildEntry(bin, item);
            } catch (Exception ex) {
                _logger.Warn($"Recycle bin: skipped item {i} ({ex.Message})");
                continue;
            }
            if (entry is not null) {
                result.Add(entry);
            }
        }

        // Recycle bin's natural ordering is "newest deletion first" — this
        // matches Explorer and is what the user expects when looking for
        // "the file I just deleted by mistake". We do the sort here rather
        // than per-view because Wander doesn't yet have per-folder sort
        // overrides; once those exist this can move into the VM.
        result.Sort((a, b) => b.ModifiedUtc.CompareTo(a.ModifiedUtc));

        return result;
    }


    /// <summary>
    /// Map one <c>FolderItem</c> from the recycle-bin namespace to a
    /// <see cref="FileSystemEntry"/>. The entry's <c>FullPath</c> is the
    /// real on-disk path inside <c>C:\$Recycle.Bin\…</c> — enough for the
    /// system icon provider to find an icon, and enough for the shell
    /// launcher to open the item. Folder vs file is decided by
    /// <c>item.IsFolder</c> so summary footers display the right glyph;
    /// children of folders are not enumerated — Wander does not recurse
    /// into recycled folders in this iteration.
    /// </summary>
    private static FileSystemEntry? BuildEntry(dynamic bin, dynamic item) {
        string name = (string)(item.Name ?? "");
        if (string.IsNullOrEmpty(name)) {
            return null;
        }

        string fullPath = SafeStr(() => (string?)item.Path);
        if (string.IsNullOrEmpty(fullPath)) {
            return null;
        }

        bool isFolder = SafeBool(() => (bool)item.IsFolder);
        long? size = SafeNullableLong(() => (long)item.Size);

        // GetDetailsOf column 2 = "Date deleted" (locale-bound; see
        // ShellRecycleBin.TryParseShellDate). Falls back to ModifyDate
        // when the deleted-date column is missing or unparseable.
        DateTime modifiedUtc;
        string deletedRaw = SafeStr(() => (string?)bin.GetDetailsOf(item, 2));
        if (TryParseShellDate(deletedRaw, out DateTime deletedLocal)) {
            modifiedUtc = deletedLocal.ToUniversalTime();
        } else {
            modifiedUtc = SafeDate(() => (DateTime)item.ModifyDate);
        }

        // GetDetailsOf column 1 = "Original location" (the directory the file
        // lived in before recycling). Same locale caveat as column 2 — the
        // *value* is a real path, locale only affects the column heading.
        string originalDir = SafeStr(() => (string?)bin.GetDetailsOf(item, 1));
        string? originalLocation = string.IsNullOrEmpty(originalDir) ? null : originalDir;

        return new FileSystemEntry(
            Name: name,
            FullPath: fullPath,
            Kind: isFolder ? EntryKind.Directory : EntryKind.File,
            Size: size,
            ModifiedUtc: modifiedUtc,
            IsHidden: false,
            IsReadOnly: false,
            IsSystem: false,
            // Recycled .lnk-to-folder shouldn't masquerade as a folder in the
            // bin — Wander doesn't navigate into bin entries either way, so
            // this stays false.
            LinksToDirectory: false,
            OriginalLocation: originalLocation);
    }


    // --- Defensive accessors -------------------------------------------
    // Shell COM throws COMException for missing optional properties on
    // some item types — wrap each lookup so a single bad item doesn't
    // wipe out the whole enumeration.

    private static string SafeStr(Func<string?> f) {
        try { return f() ?? ""; } catch { return ""; }
    }

    private static bool SafeBool(Func<bool> f) {
        try { return f(); } catch { return false; }
    }

    private static long? SafeNullableLong(Func<long> f) {
        try { return f(); } catch { return null; }
    }

    private static DateTime SafeDate(Func<DateTime> f) {
        try { return f(); } catch { return DateTime.MinValue; }
    }

    /// <summary>
    /// Same parser as <c>ShellRecycleBin.TryParseShellDate</c> — strips
    /// LRT/RTL marks and tries current + invariant cultures. Kept as a
    /// private copy here rather than pulling the shared parser into a
    /// public helper, since both call sites are tiny and the duplication
    /// is one method.
    /// </summary>
    private static bool TryParseShellDate(string s, out DateTime result) {
        result = default;
        if (string.IsNullOrWhiteSpace(s)) {
            return false;
        }
        var cleaned = new string(s.Where(c => c >= 0x20 && c != 0x200E && c != 0x200F && c != 0x202A && c != 0x202C).ToArray());
        return DateTime.TryParse(cleaned, CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out result)
            || DateTime.TryParse(cleaned, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out result);
    }
}
