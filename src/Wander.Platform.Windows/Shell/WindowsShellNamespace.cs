using System.Globalization;
using Wander.Core.FileSystem;
using Wander.Core.Logging;
using Wander.Core.Shell;

namespace Wander.Platform.Windows.Shell;

/// <summary>
/// Read-only shell-namespace enumeration. Currently knows only about the
/// Recycle Bin (<see cref="ShellPaths.RecycleBin"/>) — the only
/// namespace Wander surfaces today. Reuses the same <c>Shell.Application</c>
/// dynamic-COM dance that <c>ShellRecycleBin.Restore</c> uses; the
/// trade-off (locale-dependent column lookups, no per-process release
/// of the RCW) is identical and already documented there.
/// </summary>
public sealed class WindowsShellNamespace : IShellNamespace {
    // Shell special-folder constant CSIDL_BITBUCKET = 0xA. Same value that
    // Shell.NameSpace(int) takes for ssfBITBUCKET.
    private const int SsfBitBucket = 0xA;

    private readonly ILogger _logger;


    public WindowsShellNamespace(ILogger logger) {
        _logger = logger;
    }


    public bool IsShellPath(string path) {
        return IsRecycleBin(path);
    }

    public string? GetDisplayName(string shellPath) {
        return IsRecycleBin(shellPath) ? "Корзина" : null;
    }

    public IReadOnlyList<FileSystemEntry> Enumerate(string shellPath) {
        if (!IsRecycleBin(shellPath)) {
            return Array.Empty<FileSystemEntry>();
        }
        return EnumerateRecycleBin();
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
