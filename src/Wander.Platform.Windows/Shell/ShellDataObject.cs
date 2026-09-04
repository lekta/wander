using System.Runtime.InteropServices;
using Wander.Core.Logging;
using static Wander.Platform.Windows.Shell.ShellItemInterop;

namespace Wander.Platform.Windows.Shell;

/// <summary>
/// The shell's own data object for a selection of paths - what Explorer
/// hands over when the same items are copied to the clipboard or dragged
/// somewhere. Built the way Explorer builds it: an item array over the
/// paths, then <c>BHID_DataObject</c>.
///
/// <para>
/// It exists for the paths a <c>CF_HDROP</c> cannot carry. An entry inside
/// an archive has no file another program could open, and a file list
/// naming it makes the receiver report a file that is not there. What the
/// shell puts in the object instead is item ids (<c>CFSTR_SHELLIDLIST</c>)
/// and, for a zip, a file-group descriptor - the receiver asks the shell
/// for the bytes and the shell unpacks them.
/// </para>
///
/// <para>
/// Ordinary paths work through it too, and give the receiver everything
/// Explorer would have offered rather than the bare file list WPF builds.
/// </para>
/// </summary>
internal static class ShellDataObject {
    /// <summary>
    /// The object, or null when the shell would not build one. Every
    /// failure here means a path it does not recognise or a file that has
    /// gone, and the caller has an ordinary file list to fall back on.
    /// </summary>
    public static object? Create(IReadOnlyList<string> paths, ILogger log) {
        if (paths.Count == 0) {
            return null;
        }

        // Absolute id lists rather than the items themselves: the array
        // function that takes items is not exported by name (see
        // ShellItemInterop), and the id list is what the object carries
        // anyway.
        var pidls = new List<IntPtr>(paths.Count);
        try {
            foreach (string path in paths) {
                if (CreateIdList(path) is not { } pidl) {
                    log.Warn($"Data object: the shell does not know {path}");

                    return null;
                }
                pidls.Add(pidl);
            }

            int hr = SHCreateShellItemArrayFromIDLists((uint)pidls.Count, pidls.ToArray(), out IShellItemArray array);
            if (hr < 0 || array is null) {
                log.Warn($"Data object: no item array for {paths.Count} paths (hr=0x{hr:X8})");

                return null;
            }

            try {
                var bhid = BHID_DataObject;
                var iid = IID_IDataObject;
                hr = array.BindToHandler(IntPtr.Zero, ref bhid, ref iid, out object data);
                if (hr < 0) {
                    log.Warn($"Data object: BHID_DataObject refused (hr=0x{hr:X8})");

                    return null;
                }

                return data;
            } finally {
                Release(array);
            }
        } catch (COMException ex) {
            // A handler that throws instead of failing: the caller has the
            // ordinary file list to fall back on, and Ctrl+C must not crash.
            log.Warn($"Data object: {ex.Message}");

            return null;
        } finally {
            foreach (IntPtr pidl in pidls) {
                Marshal.FreeCoTaskMem(pidl);
            }
        }
    }


    /// <summary>The absolute id list of a path, or null when the shell cannot parse it.</summary>
    private static IntPtr? CreateIdList(string path) {
        var iid = IID_IShellItem;
        int hr = SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out object raw);
        if (hr < 0 || raw is not IShellItem item) {
            return null;
        }

        try {
            return SHGetIDListFromObject(item, out IntPtr pidl) >= 0 ? pidl : null;
        } finally {
            Release(item);
        }
    }

    private static void Release(object? comObject) {
        if (comObject is not null && Marshal.IsComObject(comObject)) {
            Marshal.ReleaseComObject(comObject);
        }
    }
}
