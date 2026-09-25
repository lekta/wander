using System.Runtime.InteropServices;
using Wander.Core.Diagnostics;

namespace Wander.Platform.Windows.Diagnostics;

/// <summary>
/// Uses the Windows Restart Manager API (rstrtmgr.dll) to ask "who has this file
/// open?". This is the same mechanism MSIs use to figure out which apps to ask
/// to close during installs. RmRegisterResources takes file paths only, so a
/// folder is asked about through the files inside it - the first
/// <see cref="MaxFolderFiles"/> of them: a folder that will not go to the bin
/// is usually a folder with one of its files open.
/// </summary>
public sealed class RestartManagerLockInspector : IFileLockInspector {
    private const int MaxFolderFiles = 500;


    public IReadOnlyList<FileLockInfo> WhoIsLocking(string filePath) {
        string[] resources = FilesOf(filePath);
        if (resources.Length == 0) {
            return Array.Empty<FileLockInfo>();
        }

        string sessionKey = Guid.NewGuid().ToString();
        int hr = RmStartSession(out uint handle, 0, sessionKey);
        if (hr != 0) {
            return Array.Empty<FileLockInfo>();
        }

        try {
            hr = RmRegisterResources(handle, (uint)resources.Length, resources, 0, null, 0, null);
            if (hr != 0) {
                return Array.Empty<FileLockInfo>();
            }

            uint pnProcInfo = 0;
            uint lpdwRebootReasons = 0;

            hr = RmGetList(handle, out uint pnProcInfoNeeded, ref pnProcInfo, null, ref lpdwRebootReasons);
            if (hr != ERROR_MORE_DATA || pnProcInfoNeeded == 0) {
                return Array.Empty<FileLockInfo>();
            }

            var processInfo = new RM_PROCESS_INFO[pnProcInfoNeeded];
            pnProcInfo = pnProcInfoNeeded;
            hr = RmGetList(handle, out pnProcInfoNeeded, ref pnProcInfo, processInfo, ref lpdwRebootReasons);
            if (hr != 0) {
                return Array.Empty<FileLockInfo>();
            }

            var result = new List<FileLockInfo>((int)pnProcInfo);
            for (int i = 0; i < pnProcInfo; i++) {
                string name = processInfo[i].strAppName;
                if (string.IsNullOrEmpty(name)) {
                    name = "(unknown)";
                }
                result.Add(new FileLockInfo(processInfo[i].Process.dwProcessId, name));
            }
            return result;
        } catch {
            return Array.Empty<FileLockInfo>();
        } finally {
            _ = RmEndSession(handle);
        }
    }


    private static string[] FilesOf(string path) {
        if (string.IsNullOrEmpty(path)) {
            return Array.Empty<string>();
        }
        if (File.Exists(path)) {
            return new[] { path };
        }
        if (!Directory.Exists(path)) {
            return Array.Empty<string>();
        }

        try {
            var options = new EnumerationOptions {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.ReparsePoint,
            };

            return Directory.EnumerateFiles(path, "*", options).Take(MaxFolderFiles).ToArray();
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
            return Array.Empty<string>();
        }
    }


    // --- Restart Manager P/Invoke -------------------------------------

    // Names as the Windows docs spell them, to the end of the file.
    // ReSharper disable InconsistentNaming

    private const int ERROR_MORE_DATA = 234;
    private const int CCH_RM_MAX_APP_NAME = 255;
    private const int CCH_RM_MAX_SVC_NAME = 63;

    [StructLayout(LayoutKind.Sequential)]
    private struct RM_UNIQUE_PROCESS {
        public int dwProcessId;
        public System.Runtime.InteropServices.ComTypes.FILETIME ProcessStartTime;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct RM_PROCESS_INFO {
        public RM_UNIQUE_PROCESS Process;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CCH_RM_MAX_APP_NAME + 1)]
        public string strAppName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CCH_RM_MAX_SVC_NAME + 1)]
        public string strServiceShortName;
        public int ApplicationType;
        public uint AppStatus;
        public uint TSSessionId;
        [MarshalAs(UnmanagedType.Bool)] public bool bRestartable;
    }

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmStartSession(out uint pSessionHandle, int dwSessionFlags, string strSessionKey);

    [DllImport("rstrtmgr.dll")]
    private static extern int RmEndSession(uint pSessionHandle);

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmRegisterResources(
        uint pSessionHandle,
        uint nFiles,
        string[] rgsFilenames,
        uint nApplications,
        [In] RM_UNIQUE_PROCESS[]? rgApplications,
        uint nServices,
        string[]? rgsServiceNames);

    [DllImport("rstrtmgr.dll")]
    private static extern int RmGetList(
        uint dwSessionHandle,
        out uint pnProcInfoNeeded,
        ref uint pnProcInfo,
        [In, Out] RM_PROCESS_INFO[]? rgAffectedApps,
        ref uint lpdwRebootReasons);
}
