using System.ComponentModel;
using System.Diagnostics;
using System.IO;
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
        InvokeVerb("properties", path);
    }

    public void OpenWith(string path) {
        // "openas" is the shell's own picker, dialog and "always use this
        // app" checkbox included — reimplementing it would be strictly worse.
        InvokeVerb("openas", path);
    }

    public void RevealInExplorer(string path) {
        // /select needs the quotes and tolerates a trailing separator badly,
        // hence the trim.
        string target = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        Process.Start(new ProcessStartInfo {
            FileName = "explorer.exe",
            Arguments = $"/select,\"{target}\"",
            UseShellExecute = true,
        });
    }

    public void OpenTerminal(string folderPath) {
        // Windows Terminal when it's there, PowerShell when it isn't. wt.exe
        // is an app-execution alias, so failure surfaces as Win32Exception
        // rather than as a missing-file check we could do up front.
        try {
            Process.Start(new ProcessStartInfo {
                FileName = "wt.exe",
                WorkingDirectory = folderPath,
                UseShellExecute = true,
            });
        } catch (Win32Exception) {
            Process.Start(new ProcessStartInfo {
                FileName = "powershell.exe",
                WorkingDirectory = folderPath,
                UseShellExecute = true,
            });
        }
    }


    private static void InvokeVerb(string verb, string path) {
        var info = new SHELLEXECUTEINFO {
            cbSize = Marshal.SizeOf<SHELLEXECUTEINFO>(),
            lpVerb = verb,
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
