using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Wander.Core.FileSystem;
using Wander.Core.Logging;

namespace Wander.Platform.Windows.FileSystem;

/// <summary>
/// IRecycleBin backed by Win32 <c>SHFileOperation</c> for Send and (TODO)
/// Shell32 namespace COM for Restore.
/// </summary>
public sealed class ShellRecycleBin : IRecycleBin {
    /// <summary>What a "something is in use" failure carries, so the caller can say so.</summary>
    private const int HResultSharingViolation = unchecked((int)0x80070020);

    /// <summary>How long a busy file or folder gets before the one retry.</summary>
    private const int BusyRetryDelayMs = 300;

    private readonly ILogger _logger;


    public ShellRecycleBin(ILogger logger) {
        _logger = logger;
    }


    // --- Send ----------------------------------------------------------

    public RecycleHandle Send(string path) {
        if (!File.Exists(path) && !Directory.Exists(path)) {
            throw new FileNotFoundException("Cannot recycle non-existent path", path);
        }

        // A file somebody holds without share-delete cannot be moved into
        // the bin, and SHFileOperation takes a second to say so (measured
        // 2026-09-17: 1075 ms). A handle asked for delete access answers the
        // same question in a couple of milliseconds. Often the hold is a
        // moment's - a thumbnail handler still reading - so once more after
        // a pause before giving up.
        if (IsFileBusy(path)) {
            _logger.Info($"Recycle: {path} is in use, waiting {BusyRetryDelayMs} ms");
            Thread.Sleep(BusyRetryDelayMs);
            if (IsFileBusy(path)) {
                throw InUse(path);
            }
        }

        DateTime when = DateTime.UtcNow;
        var op = RecycleOperation(path);
        int rc = SHFileOperation(ref op);
        // A folder with something open inside answers DE_INVALIDFILES - on a
        // path that does exist, so "invalid" is not the reason - and does so
        // at once; the file's own ERROR_SHARING_VIOLATION is the race with
        // the check above. Same pause, same one retry.
        if (IsBusy(rc)) {
            _logger.Info($"Recycle: something in {path} is in use, retrying");
            Thread.Sleep(BusyRetryDelayMs);
            op = RecycleOperation(path);
            rc = SHFileOperation(ref op);
        }
        if (IsBusy(rc)) {
            throw InUse(path);
        }
        if (rc != 0) {
            throw new IOException($"SHFileOperation FO_DELETE failed ({rc:X}) for {path}");
        }
        if (op.fAnyOperationsAborted) {
            throw new OperationCanceledException($"Recycle of '{path}' was aborted.");
        }

        _logger.Info($"Recycled: {path}");
        return new RecycleHandle(path, when);
    }


    private static bool IsBusy(int rc) {
        return rc is DE_INVALIDFILES or ERROR_SHARING_VIOLATION;
    }

    /// <summary>
    /// A file some handle keeps from being deleted or renamed. False for a
    /// folder, and for any other failure to open: SHFileOperation reports
    /// those in its own words.
    /// </summary>
    private static bool IsFileBusy(string path) {
        if (!File.Exists(path)) {
            return false;
        }

        using SafeFileHandle handle = CreateFileW(path, DELETE, FILE_SHARE_ALL, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
        if (!handle.IsInvalid) {
            return false;
        }

        return Marshal.GetLastWin32Error() == ERROR_SHARING_VIOLATION;
    }

    /// <summary>The failure the caller can recognise and name the holder of.</summary>
    private static IOException InUse(string path) {
        return new IOException($"'{path}' or something inside it is in use", HResultSharingViolation);
    }

    private static SHFILEOPSTRUCT RecycleOperation(string path) {
        // pFrom is a double-null-terminated list of paths; for a single item
        // it's still "X:\foo\bar\0\0".
        return new SHFILEOPSTRUCT {
            wFunc = FO_DELETE,
            pFrom = path + '\0' + '\0',
            fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_NOERRORUI | FOF_SILENT | FOF_WANTNUKEWARNING,
        };
    }


    // --- Restore -------------------------------------------------------

    public void Restore(RecycleHandle handle) {
        // Implementation note: we go through Shell.Application's FolderItemVerb
        // ("Restore" / "Восстановить" depending on locale) rather than
        // IFileOperation::CopyItem because it's a lot less COM boilerplate
        // and matches what Explorer does internally. Trade-off: the verb
        // name is locale-bound — currently en-US and ru-RU are known to
        // work; other locales will throw and need their localized verb name
        // added to the lookup. See TECHDEBT.
        //
        // Target-occupied case (user recreated the file between Delete and
        // Undo) is intentionally not handled here — Shell will silently
        // append "(1)" and we accept that for now.

        Type? shellType = Type.GetTypeFromProgID("Shell.Application");
        if (shellType is null) {
            throw new InvalidOperationException("Shell.Application COM type is not available.");
        }
        dynamic? shell = Activator.CreateInstance(shellType);
        if (shell is null) {
            throw new InvalidOperationException("Could not create Shell.Application instance.");
        }

        const int ssfBITBUCKET = 0xa;
        dynamic? bin = shell.NameSpace(ssfBITBUCKET);
        if (bin is null) {
            throw new InvalidOperationException("Could not open the recycle bin namespace.");
        }

        // --- Find the matching bin item ---
        // Match by original full path (column 1 = original location dir,
        // item.Name = original file name). When multiple matches exist
        // (same path deleted several times), pick the one whose delete
        // timestamp is closest to our handle's.

        dynamic? best = null;
        TimeSpan bestDelta = TimeSpan.MaxValue;

        dynamic items = bin.Items();
        int count = items.Count;
        for (int i = 0; i < count; i++) {
            dynamic item = items.Item(i);
            string origDir = (string)(bin.GetDetailsOf(item, 1) ?? "");
            string itemName = (string)(item.Name ?? "");
            string fullOrig = Path.Combine(origDir, itemName);

            if (!string.Equals(fullOrig, handle.OriginalPath, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            string deletedStr = (string)(bin.GetDetailsOf(item, 2) ?? "");
            if (TryParseShellDate(deletedStr, out DateTime deletedLocal)) {
                TimeSpan delta = (deletedLocal.ToUniversalTime() - handle.DeletedAtUtc).Duration();
                if (delta < bestDelta) {
                    best = item;
                    bestDelta = delta;
                }
            } else if (best is null) {
                // Fallback: keep the first path-match if no date parses.
                best = item;
            }
        }

        if (best is null) {
            throw new IOException($"Item not found in recycle bin: {handle.OriginalPath}");
        }

        // --- Find and invoke the Restore verb ---
        // FolderItemVerb.Name has a '&' accelerator marker we strip before
        // comparing. The name is localized by Windows.

        dynamic verbs = best.Verbs();
        int verbCount = verbs.Count;
        dynamic? restoreVerb = null;
        for (int i = 0; i < verbCount; i++) {
            dynamic verb = verbs.Item(i);
            string name = ((string)(verb.Name ?? "")).Replace("&", "");
            if (IsRestoreVerb(name)) {
                restoreVerb = verb;
                break;
            }
        }

        if (restoreVerb is null) {
            throw new IOException(
                $"Restore verb not found on recycle-bin item for {handle.OriginalPath}. " +
                $"Likely an unsupported Windows locale — add the localized verb name to ShellRecycleBin.IsRestoreVerb.");
        }

        restoreVerb.DoIt();
        _logger.Info($"Restored from recycle: {handle.OriginalPath}");
    }


    /// <summary>
    /// Known localized forms of the recycle-bin "Restore" verb. Extend as
    /// new locales come up (see TECHDEBT).
    /// </summary>
    private static bool IsRestoreVerb(string name) {
        return name.Equals("Restore", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Восстановить", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Shell.GetDetailsOf for the "Date deleted" column returns a localized
    /// string with embedded LRT/RTL marks (U+200E etc) around digits — those
    /// break DateTime.Parse, so we strip them first and try both current
    /// and invariant cultures.
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


    // --- P/Invoke ------------------------------------------------------

    private const uint FO_DELETE = 0x0003;

    /// <summary>SHFileOperation's own "the path was invalid" - see <see cref="Send"/>.</summary>
    private const int DE_INVALIDFILES = 0x7C;
    private const int ERROR_SHARING_VIOLATION = 0x20;

    private const uint DELETE = 0x00010000;
    private const uint FILE_SHARE_ALL = 0x00000007;
    private const uint OPEN_EXISTING = 3;

    private const ushort FOF_SILENT = 0x0004;
    private const ushort FOF_NOCONFIRMATION = 0x0010;
    private const ushort FOF_ALLOWUNDO = 0x0040;
    private const ushort FOF_NOERRORUI = 0x0400;
    /// <summary>Suppress the "this is too big for the recycle bin, delete permanently?" prompt by failing fast instead.</summary>
    private const ushort FOF_WANTNUKEWARNING = 0x4000;

    // Note: no Pack override — SHFILEOPSTRUCT needs natural alignment so that
    // pFrom/pTo (pointers) sit on 8-byte boundaries on x64. Pack=1 misaligns
    // them and SHFileOperation crashes with 0xC0000005.
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCT {
        public IntPtr hwnd;
        public uint wFunc;
        public string pFrom;
        public string? pTo;
        public ushort fFlags;
        [MarshalAs(UnmanagedType.Bool)]
        public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        public string? lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperation(ref SHFILEOPSTRUCT lpFileOp);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(
        string lpFileName, uint dwDesiredAccess, uint dwShareMode, IntPtr lpSecurityAttributes,
        uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);
}
