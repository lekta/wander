using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Wander.Core.Actions;

namespace Wander.Harness.Host;

/// <summary>
/// A desktop of its own for a run - a Windows desktop object, not the
/// screen. Parked off-screen and never shown activated, the window still
/// took the foreground from whoever was working: the first time the
/// keyboard moves inside it, WPF gives the window Win32 focus, and that
/// activates it (2026-09-29: a video left full screen when a scenario
/// opened its first name editor). The foreground is kept per desktop, so
/// a window on another one cannot take it - and cannot show up on the
/// screen either, whoever made it: WPF, the shell or the web view's own
/// process.
///
/// <para>
/// The run starts this executable again on that desktop with the same
/// arguments, console and standard handles, waits, and answers with its
/// exit code. A job ends the second process with the first: killed from
/// outside, the run would otherwise leave an application nobody can see.
/// </para>
/// </summary>
internal static class HarnessDesktop {
    /// <summary>
    /// Run the scenario in this process, on the desktop it was started on:
    /// what <see cref="Relaunch"/> passes on, and what a debugger session
    /// passes to keep the scenario in the process it is attached to.
    /// </summary>
    public const string InPlaceFlag = "in-place";

    private const string DesktopName = "WanderHarness";


    /// <summary>The exit code of the same command run on the desktop of its own.</summary>
    public static int Relaunch(string[] args) {
        string exe = Environment.ProcessPath
            ?? throw new InvalidOperationException("The harness cannot find its own executable to start again.");
        var commandLine = new StringBuilder(CommandLine.Quote(exe));
        foreach (string arg in args) {
            commandLine.Append(' ').Append(CommandLine.Quote(arg));
        }
        commandLine.Append(" --").Append(InPlaceFlag);

        IntPtr desktop = CreateDesktopW(DesktopName, IntPtr.Zero, IntPtr.Zero, 0, GENERIC_ALL, IntPtr.Zero);
        if (desktop == IntPtr.Zero) {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateDesktop failed");
        }

        IntPtr job = CreateKillOnCloseJob();
        try {
            var startup = new STARTUPINFO {
                cb = Marshal.SizeOf<STARTUPINFO>(),
                lpDesktop = @"WinSta0\" + DesktopName,
                dwFlags = STARTF_USESTDHANDLES,
                hStdInput = GetStdHandle(STD_INPUT_HANDLE),
                hStdOutput = GetStdHandle(STD_OUTPUT_HANDLE),
                hStdError = GetStdHandle(STD_ERROR_HANDLE),
            };
            // Suspended until it is in the job: nothing of it runs outside.
            if (!CreateProcessW(null, commandLine, IntPtr.Zero, IntPtr.Zero, true, CREATE_SUSPENDED,
                    IntPtr.Zero, null, ref startup, out var process)) {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateProcess failed");
            }

            try {
                // Best effort: without the job the run still works, and only
                // outlives this process when that one is killed.
                _ = AssignProcessToJobObject(job, process.hProcess);
                _ = ResumeThread(process.hThread);
                _ = WaitForSingleObject(process.hProcess, INFINITE);

                return GetExitCodeProcess(process.hProcess, out uint code) ? (int)code : 70;
            } finally {
                _ = CloseHandle(process.hThread);
                _ = CloseHandle(process.hProcess);
            }
        } finally {
            _ = CloseHandle(job);
            _ = CloseDesktop(desktop);
        }
    }


    private static IntPtr CreateKillOnCloseJob() {
        IntPtr job = CreateJobObjectW(IntPtr.Zero, null);
        var limits = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION {
            BasicLimitInformation = new JOBOBJECT_BASIC_LIMIT_INFORMATION { LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE },
        };
        _ = SetInformationJobObject(
            job, JobObjectExtendedLimitInformation, ref limits, Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>());

        return job;
    }


    // --- P/Invoke ------------------------------------------------------

    // Names as the Windows docs spell them, to the end of the file.
    // ReSharper disable InconsistentNaming

    private const uint GENERIC_ALL = 0x10000000;
    private const uint CREATE_SUSPENDED = 0x00000004;
    private const int STARTF_USESTDHANDLES = 0x00000100;
    private const int STD_INPUT_HANDLE = -10;
    private const int STD_OUTPUT_HANDLE = -11;
    private const int STD_ERROR_HANDLE = -12;
    private const uint INFINITE = 0xFFFFFFFF;
    private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x00002000;
    private const int JobObjectExtendedLimitInformation = 9;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct STARTUPINFO {
        public int cb;
        public string? lpReserved;
        public string? lpDesktop;
        public string? lpTitle;
        public int dwX;
        public int dwY;
        public int dwXSize;
        public int dwYSize;
        public int dwXCountChars;
        public int dwYCountChars;
        public int dwFillAttribute;
        public int dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION {
        public IntPtr hProcess;
        public IntPtr hThread;
        public int dwProcessId;
        public int dwThreadId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateDesktopW(
        string lpszDesktop, IntPtr lpszDevice, IntPtr pDevmode, int dwFlags, uint dwDesiredAccess, IntPtr lpsa);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseDesktop(IntPtr hDesktop);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateProcessW(
        string? lpApplicationName, StringBuilder lpCommandLine, IntPtr lpProcessAttributes, IntPtr lpThreadAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool bInheritHandles, uint dwCreationFlags, IntPtr lpEnvironment,
        string? lpCurrentDirectory, ref STARTUPINFO lpStartupInfo, out PROCESS_INFORMATION lpProcessInformation);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll")]
    private static extern uint ResumeThread(IntPtr hThread);

    [DllImport("kernel32.dll")]
    private static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetExitCodeProcess(IntPtr hProcess, out uint lpExitCode);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateJobObjectW(IntPtr lpJobAttributes, string? lpName);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(
        IntPtr hJob, int jobObjectInformationClass, ref JOBOBJECT_EXTENDED_LIMIT_INFORMATION lpJobObjectInformation,
        int cbJobObjectInformationLength);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);
}
