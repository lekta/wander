using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Wander.Core.FileSystem;

namespace Wander.Platform.Windows.FileSystem;

/// <summary>
/// A handle asked for delete access, sharing everything: it opens unless
/// somebody holds the file without share-delete - which is exactly what
/// keeps a delete, a move or a rename from going through. A couple of
/// milliseconds (measured 2026-09-17), and nothing is read or locked.
/// </summary>
public sealed class WindowsFileBusyProbe : IFileBusyProbe {
    public bool IsBusy(string path) {
        if (!File.Exists(path)) {
            return false;
        }

        using SafeFileHandle handle = CreateFileW(path, DELETE, FILE_SHARE_ALL, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
        if (!handle.IsInvalid) {
            return false;
        }

        return Marshal.GetLastWin32Error() == ERROR_SHARING_VIOLATION;
    }

    public string? FirstBusyIn(string folder, TimeSpan budget, CancellationToken ct) {
        if (!Directory.Exists(folder)) {
            return null;
        }

        var until = DateTime.UtcNow + budget;
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
        try {
            foreach (string file in Directory.EnumerateFiles(folder, "*", options)) {
                if (ct.IsCancellationRequested || DateTime.UtcNow > until) {
                    return null;
                }
                if (IsBusy(file)) {
                    return file;
                }
            }
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
            // A folder that went or closed its doors mid-walk names nobody.
        }

        return null;
    }


    // --- P/Invoke ------------------------------------------------------

    // Names as the Windows docs spell them, to the end of the file.
    // ReSharper disable InconsistentNaming

    private const int ERROR_SHARING_VIOLATION = 0x20;

    private const uint DELETE = 0x00010000;
    private const uint FILE_SHARE_ALL = 0x00000007;
    private const uint OPEN_EXISTING = 3;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(
        string lpFileName, uint dwDesiredAccess, uint dwShareMode, IntPtr lpSecurityAttributes,
        uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);
}
