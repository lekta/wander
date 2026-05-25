using System.Runtime.InteropServices;
using Wander.Core.Diagnostics;

namespace Wander.Platform.Windows.Diagnostics;

/// <summary>
/// Uses the Windows Restart Manager API (rstrtmgr.dll) to ask "who has this file
/// open?". This is the same mechanism MSIs use to figure out which apps to ask
/// to close during installs. Works for files only — for folders we return empty
/// because RmRegisterResources expects individual file paths.
/// </summary>
public sealed class RestartManagerLockInspector : IFileLockInspector {
    public IReadOnlyList<FileLockInfo> WhoIsLocking(string filePath) {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) {
            return Array.Empty<FileLockInfo>();
        }

        string sessionKey = Guid.NewGuid().ToString();
        int hr = RmStartSession(out uint handle, 0, sessionKey);
        if (hr != 0) {
            return Array.Empty<FileLockInfo>();
        }

        try {
            string[] resources = { filePath };
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
            RmEndSession(handle);
        }
    }


    // --- Restart Manager P/Invoke -------------------------------------

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
