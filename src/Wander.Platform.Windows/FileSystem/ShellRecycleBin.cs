using System.Runtime.InteropServices;
using Wander.Core.FileSystem;
using Wander.Core.Logging;

namespace Wander.Platform.Windows.FileSystem;

/// <summary>
/// IRecycleBin backed by Win32 <c>SHFileOperation</c> for Send and (TODO)
/// Shell32 namespace COM for Restore.
/// </summary>
public sealed class ShellRecycleBin : IRecycleBin {
    private readonly ILogger _logger;


    public ShellRecycleBin(ILogger logger) {
        _logger = logger;
    }


    // --- Send ----------------------------------------------------------

    public RecycleHandle Send(string path) {
        if (!File.Exists(path) && !Directory.Exists(path)) {
            throw new FileNotFoundException("Cannot recycle non-existent path", path);
        }

        // pFrom is a double-null-terminated list of paths; for a single item
        // it's still "X:\foo\bar\0\0".
        var op = new SHFILEOPSTRUCT {
            wFunc = FO_DELETE,
            pFrom = path + '\0' + '\0',
            fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_NOERRORUI | FOF_SILENT | FOF_WANTNUKEWARNING,
        };
        DateTime when = DateTime.UtcNow;
        int rc = SHFileOperation(ref op);
        if (rc != 0) {
            throw new IOException($"SHFileOperation FO_DELETE failed ({rc:X}) for {path}");
        }
        if (op.fAnyOperationsAborted) {
            throw new OperationCanceledException($"Recycle of '{path}' was aborted.");
        }

        _logger.Info($"Recycled: {path}");
        return new RecycleHandle(path, when);
    }


    // --- Restore -------------------------------------------------------

    public void Restore(RecycleHandle handle) {
        // TODO: implement via Shell32 COM.
        //
        // Sketch of what goes here (see chat notes for full nuance list):
        //
        //   dynamic shell = Activator.CreateInstance(
        //       Type.GetTypeFromProgID("Shell.Application")
        //       ?? throw new InvalidOperationException("Shell.Application unavailable"));
        //   const int ssfBITBUCKET = 0xa;
        //   dynamic bin = shell.NameSpace(ssfBITBUCKET);
        //
        //   dynamic? best = null;
        //   DateTime bestDelta = TimeSpan.MaxValue;
        //   foreach (dynamic item in bin.Items()) {
        //       // Column 1 on Win10/11 is "Original Location". The full
        //       // original path is Path.Combine(<col 1>, item.Name).
        //       string origDir = bin.GetDetailsOf(item, 1);
        //       string fullOrig = Path.Combine(origDir, (string)item.Name);
        //       if (!string.Equals(fullOrig, handle.OriginalPath, StringComparison.OrdinalIgnoreCase)) {
        //           continue;
        //       }
        //       // Column 2 = "Date deleted". Parse with CurrentCulture; pick
        //       // the item whose timestamp is closest to (and >= rounded-down)
        //       // handle.DeletedAtUtc.ToLocalTime().
        //       ...
        //   }
        //
        //   if (best is null) throw new IOException("Item not found in recycle bin.");
        //
        //   // The verb name is locale-dependent ("Restore" / "Восстановить"
        //   // / "Wiederherstellen" / ...). Two workarounds:
        //   //   1. Enumerate item.Verbs() and pick the one whose Name (with
        //   //      "&" stripped) is at the well-known index — fragile.
        //   //   2. Use IFileOperation::CopyItem to handle.OriginalPath and
        //   //      then IFileOperation::DeleteItem on the bin item — robust,
        //   //      but requires real COM interop (not dynamic-friendly).
        //   //
        //   // We'll go with (2) when we wire this up.
        //
        //   best.InvokeVerb("Restore");
        //
        // Until then: log loudly and surface a clear error so the user knows
        // delete-undo just isn't on yet.

        _logger.Warn($"Recycle restore not yet implemented for {handle.OriginalPath}");
        throw new NotImplementedException(
            "Restoring from the Windows recycle bin via Shell32 isn't wired up yet. " +
            "Open the recycle bin in Explorer and restore from there for now.");
    }


    // --- P/Invoke ------------------------------------------------------

    private const uint FO_DELETE = 0x0003;

    private const ushort FOF_SILENT = 0x0004;
    private const ushort FOF_NOCONFIRMATION = 0x0010;
    private const ushort FOF_ALLOWUNDO = 0x0040;
    private const ushort FOF_NOERRORUI = 0x0400;
    /// <summary>Suppress the "this is too big for the recycle bin, delete permanently?" prompt by failing fast instead.</summary>
    private const ushort FOF_WANTNUKEWARNING = 0x4000;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode, Pack = 1)]
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
}
