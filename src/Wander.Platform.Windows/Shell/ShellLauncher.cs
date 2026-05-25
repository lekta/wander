using System.Diagnostics;
using System.Runtime.InteropServices;
using Wander.Core.Shell;

namespace Wander.Platform.Windows.Shell;

public sealed class ShellLauncher : IShellLauncher {
    public void Open(string path) {
        var psi = new ProcessStartInfo {
            FileName = path,
            UseShellExecute = true,
        };
        Process.Start(psi);
    }

    public void ShowProperties(string path) {
        var info = new SHELLEXECUTEINFO {
            cbSize = Marshal.SizeOf<SHELLEXECUTEINFO>(),
            lpVerb = "properties",
            lpFile = path,
            nShow = SW_SHOW,
            fMask = SEE_MASK_INVOKEIDLIST,
        };
        ShellExecuteEx(ref info);
    }


    // --- Win32 -------------------------------------------------------

    private const uint SEE_MASK_INVOKEIDLIST = 0x0000000C;
    private const int SW_SHOW = 5;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct SHELLEXECUTEINFO {
        public int cbSize;
        public uint fMask;
        public IntPtr hwnd;
        [MarshalAs(UnmanagedType.LPTStr)] public string lpVerb;
        [MarshalAs(UnmanagedType.LPTStr)] public string lpFile;
        [MarshalAs(UnmanagedType.LPTStr)] public string? lpParameters;
        [MarshalAs(UnmanagedType.LPTStr)] public string? lpDirectory;
        public int nShow;
        public IntPtr hInstApp;
        public IntPtr lpIDList;
        [MarshalAs(UnmanagedType.LPTStr)] public string? lpClass;
        public IntPtr hkeyClass;
        public uint dwHotKey;
        public IntPtr hIcon;
        public IntPtr hProcess;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShellExecuteEx(ref SHELLEXECUTEINFO lpExecInfo);
}
