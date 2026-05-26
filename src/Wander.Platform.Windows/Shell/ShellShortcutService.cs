using System.Runtime.InteropServices;
using System.Text;
using Wander.Core.Shell;

namespace Wander.Platform.Windows.Shell;

/// <summary>
/// Creates and resolves Windows .lnk files via the IShellLinkW + IPersistFile
/// COM duo. This is the same mechanism Explorer uses when you Alt-drag a file.
/// </summary>
public sealed class ShellShortcutService : IShortcutService {
    public void Create(string targetPath, string shortcutPath) {
        var link = (IShellLinkW)new ShellLink();
        link.SetPath(targetPath);

        string? workDir = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrEmpty(workDir)) {
            link.SetWorkingDirectory(workDir);
        }

        var persist = (IPersistFile)link;
        persist.Save(shortcutPath, true);

        Marshal.ReleaseComObject(persist);
        Marshal.ReleaseComObject(link);
    }

    public string? Resolve(string shortcutPath) {
        if (string.IsNullOrEmpty(shortcutPath)
            || !File.Exists(shortcutPath)
            || !shortcutPath.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) {
            return null;
        }

        IShellLinkW? link = null;
        IPersistFile? persist = null;
        try {
            link = (IShellLinkW)new ShellLink();
            persist = (IPersistFile)link;
            persist.Load(shortcutPath, 0);

            var sb = new StringBuilder(260);
            link.GetPath(sb, sb.Capacity, IntPtr.Zero, SLGP_RAWPATH);
            string result = sb.ToString();
            return string.IsNullOrEmpty(result) ? null : result;
        } catch {
            return null;
        } finally {
            if (persist is not null) {
                Marshal.ReleaseComObject(persist);
            }
            if (link is not null) {
                Marshal.ReleaseComObject(link);
            }
        }
    }


    // --- COM definitions ----------------------------------------------

    private const int SLGP_RAWPATH = 0x4;

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink { }

    [ComImport]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW {
        void GetPath([MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cchMaxPath, IntPtr pfd, int fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cchMaxName);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cchMaxPath);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cchMaxPath);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotKey(out short pwHotkey);
        void SetHotKey(short wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cchIconPath, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, int dwReserved);
        void Resolve(IntPtr hwnd, int fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }

    [ComImport]
    [Guid("0000010b-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPersistFile {
        void GetClassID(out Guid pClassID);
        [PreserveSig] int IsDirty();
        void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, int dwMode);
        void Save([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, [MarshalAs(UnmanagedType.Bool)] bool fRemember);
        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
        void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string ppszFileName);
    }
}
