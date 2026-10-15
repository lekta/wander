using System.Runtime.InteropServices;
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
/// </summary>
public sealed class VolumeChangeHub : IVolumeChanges {
    private readonly ILogger _log;


    public VolumeChangeHub(ILogger log) {
        _log = log;
    }


    public event EventHandler<VolumeChange>? Changed;


    /// <summary>
    /// A window message: a volume's arrival or removal is raised, anything
    /// else is left alone. Never claims the message - other parts of the
    /// window may want it too.
    /// </summary>
    /// <returns>True when it was a volume change.</returns>
    public bool HandleMessage(int msg, nint wParam, nint lParam) {
        if (msg != WM_DEVICECHANGE || lParam == 0) {
            return false;
        }

        int kind = (int)wParam;
        if (kind != DBT_DEVICEARRIVAL && kind != DBT_DEVICEREMOVECOMPLETE) {
            return false;
        }

        var header = Marshal.PtrToStructure<DEV_BROADCAST_HDR>(lParam);
        if (header.dbch_devicetype != DBT_DEVTYP_VOLUME) {
            return false;
        }

        var volume = Marshal.PtrToStructure<DEV_BROADCAST_VOLUME>(lParam);
        bool arrived = kind == DBT_DEVICEARRIVAL;
        foreach (string root in RootsOf(volume.dbcv_unitmask)) {
            _log.Info($"Volume {(arrived ? "arrived" : "removed")}: {root}");
            Changed?.Invoke(this, new VolumeChange(root, arrived));
        }

        return true;
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


    // --- P/Invoke ------------------------------------------------------

    // Names as the Windows docs spell them, to the end of the file.
    // ReSharper disable InconsistentNaming

    private const int WM_DEVICECHANGE = 0x0219;
    private const int DBT_DEVICEARRIVAL = 0x8000;
    private const int DBT_DEVICEREMOVECOMPLETE = 0x8004;
    private const int DBT_DEVTYP_VOLUME = 0x00000002;

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
}
