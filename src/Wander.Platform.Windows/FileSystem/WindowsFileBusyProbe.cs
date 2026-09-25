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
