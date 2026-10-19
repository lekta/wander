using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Wander.Core.FileSystem;
using Wander.Core.Logging;

namespace Wander.Platform.Windows.FileSystem;

/// <summary>
/// <see cref="IVolumeChanges"/> from <c>WM_DEVICECHANGE</c>: the system
/// broadcasts a volume's arrival and removal to every top-level window, and
/// the window's hook hands the message here (<see cref="HandleMessage"/>).
/// Platform has no WPF; the app installs the hook. One event per drive
/// letter in the message's mask - a card reader with two slots can say two
/// at once.
///
/// <para>
/// The broadcast is not all (2026-10-19): a flash drive's removal never
/// reached the window, and an eject from the tray fails "in use" on the
/// handle of the open folder's watcher. So the volume the open folder is on
/// is also watched the way Windows asks programs to (<see cref="Guard"/>):
/// a handle of ours on its root, registered for the drive's own notices.
/// Asked to let the volume go, the hub closes that handle and raises
/// <see cref="Releasing"/> - the watcher closes its own; kept by somebody
/// else, both open again (<see cref="ReleaseRefused"/>); gone, the removal
/// is raised whether the broadcast comes or not - and once when both do.
/// </para>
/// </summary>
public sealed class VolumeChangeHub : IVolumeChanges {
    /// <summary>A removal of the same volume said again within this - the broadcast after the drive's own notice - is the same removal.</summary>
    private const long RepeatMs = 10_000;

    private readonly ILogger _log;
    private readonly Lock _lock = new();
    private readonly Dictionary<string, long> _removedAt = new(StringComparer.OrdinalIgnoreCase);

    private nint _window;
    private Guarded? _guard;
    // What the last Guard() asked for: the folder opened before the window
    // was there is guarded once it is (Attach).
    private string? _folder;

    // Which Guard() call is the current one: the handle is opened on the
    // pool, and an older call must not put back a volume the newer let go.
    private int _generation;


    public VolumeChangeHub(ILogger log) {
        _log = log;
    }


    public event EventHandler<VolumeChange>? Changed;

    public event EventHandler<string>? Releasing;

    /// <summary>The volume Windows asked for was kept - by another program, or by what Wander could not let go of in time: what let go of it on <see cref="Releasing"/> may hold it again.</summary>
    public event EventHandler<string>? ReleaseRefused;


    /// <summary>The window the drive's notices go to - the one whose hook calls <see cref="HandleMessage"/>. Nothing is guarded before.</summary>
    public void Attach(nint window) {
        _window = window;
        Guard(_folder);
    }

    /// <summary>
    /// The volume <paramref name="folder"/> is on is the one guarded from now
    /// on, the one before is let go; none for null, a share or a shell path.
    /// Never blocks the caller: opening the root of a sleeping drive takes
    /// seconds, and the watcher calls this on every step of navigation.
    /// </summary>
    public void Guard(string? folder) {
        _folder = folder;
        int gen = Interlocked.Increment(ref _generation);
        Task.Run(() => Retarget(gen, folder));
    }

    /// <summary>
    /// A window message: a volume's arrival or removal is raised, a notice
    /// about the guarded volume acted on, anything else left alone. Never
    /// claims the message - other parts of the window may want it too, and
    /// the answer to Windows' question is "yes" either way.
    /// </summary>
    /// <returns>True when it was about a volume.</returns>
    public bool HandleMessage(int msg, nint wParam, nint lParam) {
        if (msg != WM_DEVICECHANGE || lParam == 0) {
            return false;
        }

        var header = Marshal.PtrToStructure<DEV_BROADCAST_HDR>(lParam);

        return header.dbch_devicetype switch {
            DBT_DEVTYP_VOLUME => OnVolume((int)wParam, lParam),
            DBT_DEVTYP_HANDLE => OnHandle((int)wParam, lParam),
            _ => false,
        };
    }


    /// <summary>The drive roots in a unit mask: bit 0 is A:, bit 1 is B:, and so on.</summary>
    internal static IReadOnlyList<string> RootsOf(uint mask) {
        var roots = new List<string>();
        for (int bit = 0; bit < 26; bit++) {
            if ((mask & (1u << bit)) != 0) {
                roots.Add($"{(char)('A' + bit)}:\\");
            }
        }

        return roots;
    }


    private bool OnVolume(int kind, nint lParam) {
        if (kind != DBT_DEVICEARRIVAL && kind != DBT_DEVICEREMOVECOMPLETE) {
            return false;
        }

        var volume = Marshal.PtrToStructure<DEV_BROADCAST_VOLUME>(lParam);
        foreach (string root in RootsOf(volume.dbcv_unitmask)) {
            if (kind == DBT_DEVICEARRIVAL) {
                lock (_lock) {
                    _removedAt.Remove(root);
                }
                _log.Info($"Volume arrived: {root}");
                Changed?.Invoke(this, new VolumeChange(root, true));
            } else {
                Removed(root, "broadcast");
            }
        }

        return true;
    }

    /// <summary>
    /// The guarded volume's own notice, as "Processing a Request to Remove a
    /// Device" spells it out: asked - the handle is closed before the answer;
    /// refused - opened again under a new registration; gone - let go of.
    /// </summary>
    private bool OnHandle(int kind, nint lParam) {
        var notice = Marshal.PtrToStructure<DEV_BROADCAST_HANDLE>(lParam);
        string root;
        lock (_lock) {
            if (_guard is not { } guard || guard.Notify != notice.dbch_hdevnotify) {
                return false;
            }

            root = guard.Root;
            switch (kind) {
                case DBT_DEVICEQUERYREMOVE:
                    guard.Handle.Dispose();
                    break;

                case DBT_DEVICEQUERYREMOVEFAILED:
                    Drop();
                    // The volume is there and was just opened: no sleeping
                    // drive to wait for on this thread.
                    _guard = Open(root);
                    break;

                case DBT_DEVICEREMOVEPENDING:
                case DBT_DEVICEREMOVECOMPLETE:
                    Drop();
                    break;

                default:
                    return false;
            }
        }

        switch (kind) {
            case DBT_DEVICEQUERYREMOVE:
                _log.Info($"Volume removal asked: {root}");
                Releasing?.Invoke(this, root);
                break;

            case DBT_DEVICEQUERYREMOVEFAILED:
                _log.Info($"Volume removal refused: {root}");
                ReleaseRefused?.Invoke(this, root);
                break;

            default:
                Removed(root, "notice");
                break;
        }

        return true;
    }

    /// <summary>A volume went: raised once, however many of the two ways say so.</summary>
    private void Removed(string root, string how) {
        lock (_lock) {
            long now = Environment.TickCount64;
            if (_removedAt.TryGetValue(root, out long at) && now - at < RepeatMs) {
                return;
            }
            _removedAt[root] = now;
            if (_guard is { } guard && string.Equals(guard.Root, root, StringComparison.OrdinalIgnoreCase)) {
                Drop();
            }
        }

        _log.Info($"Volume removed: {root} ({how})");
        Changed?.Invoke(this, new VolumeChange(root, false));
    }


    // --- The guarded volume -----------------------------------------------

    private void Retarget(int gen, string? folder) {
        string? root = RootToGuard(folder);
        lock (_lock) {
            if (gen != Volatile.Read(ref _generation)
                || string.Equals(_guard?.Root, root, StringComparison.OrdinalIgnoreCase)) {
                return;
            }

            Drop();
            if (root is not null) {
                _guard = Open(root);
            }
        }
    }

    /// <summary>
    /// The root of a drive that Windows asks about before taking it - a
    /// flash drive, a USB disk (Windows calls it fixed); null for anything
    /// else. A disc is not asked about: it is ejected as a medium.
    /// </summary>
    private static string? RootToGuard(string? folder) {
        if (string.IsNullOrEmpty(folder) || folder.StartsWith(@"\\", StringComparison.Ordinal)) {
            return null;
        }

        try {
            string? root = Path.GetPathRoot(folder);
            if (string.IsNullOrEmpty(root) || root.Length != 3) {
                return null;
            }

            return new DriveInfo(root).DriveType is DriveType.Removable or DriveType.Fixed ? root : null;
        } catch (Exception ex) when (ex is ArgumentException or IOException) {
            return null;
        }
    }

    /// <summary>Caller holds the lock. Null when the root will not open - a card reader with no card - or the window is not there yet.</summary>
    private Guarded? Open(string root) {
        if (_window == 0) {
            return null;
        }

        var handle = CreateFileW(root, FILE_LIST_DIRECTORY, FILE_SHARE_ALL, 0, OPEN_EXISTING, FILE_FLAG_BACKUP_SEMANTICS, 0);
        if (handle.IsInvalid) {
            return null;
        }

        var filter = new DEV_BROADCAST_HANDLE {
            dbch_size = Marshal.SizeOf<DEV_BROADCAST_HANDLE>(),
            dbch_devicetype = DBT_DEVTYP_HANDLE,
            dbch_handle = handle.DangerousGetHandle(),
        };
        nint notify = RegisterDeviceNotificationW(_window, ref filter, DEVICE_NOTIFY_WINDOW_HANDLE);
        if (notify == 0) {
            _log.Warn($"Volume {root}: removal notices not registered (error {Marshal.GetLastWin32Error()})");
            handle.Dispose();

            return null;
        }

        _log.Info($"Volume guarded: {root}");

        return new Guarded(root, handle, notify);
    }

    /// <summary>Caller holds the lock.</summary>
    private void Drop() {
        if (_guard is not { } guard) {
            return;
        }

        _guard = null;
        UnregisterDeviceNotification(guard.Notify);
        guard.Handle.Dispose();
    }


    /// <summary>The guarded volume: its root, the handle the notices are registered on, the registration.</summary>
    private sealed record Guarded(string Root, SafeFileHandle Handle, nint Notify);


    // --- P/Invoke ------------------------------------------------------

    // Names as the Windows docs spell them, to the end of the file.
    // ReSharper disable InconsistentNaming

    private const int WM_DEVICECHANGE = 0x0219;
    private const int DBT_DEVICEARRIVAL = 0x8000;
    private const int DBT_DEVICEQUERYREMOVE = 0x8001;
    private const int DBT_DEVICEQUERYREMOVEFAILED = 0x8002;
    private const int DBT_DEVICEREMOVEPENDING = 0x8003;
    private const int DBT_DEVICEREMOVECOMPLETE = 0x8004;
    private const int DBT_DEVTYP_VOLUME = 0x00000002;
    private const int DBT_DEVTYP_HANDLE = 0x00000006;
    private const int DEVICE_NOTIFY_WINDOW_HANDLE = 0x00000000;

    private const uint FILE_LIST_DIRECTORY = 0x00000001;
    private const uint FILE_SHARE_ALL = 0x00000007;
    private const uint OPEN_EXISTING = 3;
    private const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;

    [StructLayout(LayoutKind.Sequential)]
    private struct DEV_BROADCAST_HDR {
        public int dbch_size;
        public int dbch_devicetype;
        public int dbch_reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DEV_BROADCAST_VOLUME {
        public int dbcv_size;
        public int dbcv_devicetype;
        public int dbcv_reserved;
        public uint dbcv_unitmask;
        public ushort dbcv_flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DEV_BROADCAST_HANDLE {
        public int dbch_size;
        public int dbch_devicetype;
        public int dbch_reserved;
        public nint dbch_handle;
        public nint dbch_hdevnotify;
        public Guid dbch_eventguid;
        public int dbch_nameoffset;
        public byte dbch_data;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(
        string lpFileName, uint dwDesiredAccess, uint dwShareMode, nint lpSecurityAttributes,
        uint dwCreationDisposition, uint dwFlagsAndAttributes, nint hTemplateFile);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint RegisterDeviceNotificationW(nint hRecipient, ref DEV_BROADCAST_HANDLE notificationFilter, int flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterDeviceNotification(nint handle);
}
