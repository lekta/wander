using System.Runtime.InteropServices;
using Wander.Core.FileSystem;
using Wander.Core.Logging;
using static Wander.Platform.Windows.Shell.ShellItemInterop;
using ComTypes = System.Runtime.InteropServices.ComTypes;

namespace Wander.Platform.Windows.Shell;

/// <summary>
/// A device's content read the way Explorer reads it, through the shell:
/// the fallback "Отладка" switches on for a device Windows Portable
/// Devices does not get on with (decision of 2026-10-19). Slower - a cold
/// listing of a phone's camera folder takes the shell minutes (stand Y4a) -
/// and with no thumbnails of the device's own, but the same rows.
///
/// <para>
/// A place opens by the device's parsing name and the display names under
/// it; an item's own parsing name (its PUID) does not parse back (stand
/// Y4a). So every item read is kept by its path, and a file found again
/// is found from its folder - re-listing a big folder for each file would
/// take as long as the listing itself. The items are made on the calling
/// thread (a pool thread, MTA): the shell's folder for a device marshals
/// them as it needs.
/// </para>
/// </summary>
internal sealed class ShellDeviceReader {
    private readonly ILogger _log;
    private readonly Lock _lock = new();
    private readonly Dictionary<string, IShellItem> _items = new(StringComparer.OrdinalIgnoreCase);


    public ShellDeviceReader(ILogger log) {
        _log = log;
    }


    /// <summary>The storages of a device, by the names the shell shows.</summary>
    public IReadOnlyList<WpdObject> Storages(DeviceFolders.Device device) {
        var root = ItemFor(device.ShellPath);

        return Children(DevicePath.RootOf(device.Name), root, CancellationToken.None);
    }

    /// <summary>What a folder of a device holds; ids are the rows' paths.</summary>
    public IReadOnlyList<WpdObject> List(DeviceFolders.Device device, DevicePath folder, CancellationToken ct) {
        return Children(Join(folder), Find(device, folder, ct), ct);
    }

    /// <summary>A file's bytes, through the shell's stream.</summary>
    public Stream Open(DeviceFolders.Device device, DevicePath file, long length) {
        var item = Find(device, file, CancellationToken.None);
        var bhid = BHID_Stream;
        var iid = IID_IStream;
        int hr = item.BindToHandler(IntPtr.Zero, ref bhid, ref iid, out object raw);
        if (hr < 0 || raw is not ComTypes.IStream stream) {
            throw new COMException($"The device gave no stream for {Join(file)}", hr < 0 ? hr : unchecked((int)0x80004002));
        }

        return new ComReadStream(stream, length);
    }

    /// <summary>Everything read is let go of: a device came or went, and its items with it.</summary>
    public void Forget() {
        List<IShellItem> items;
        lock (_lock) {
            items = _items.Values.ToList();
            _items.Clear();
        }
        foreach (var item in items) {
            WpdInterop.Release(item);
        }
    }


    /// <summary>The item at a place, from what was read, listing the folders on the way that were not.</summary>
    private IShellItem Find(DeviceFolders.Device device, DevicePath path, CancellationToken ct) {
        string key = Join(path);
        lock (_lock) {
            if (_items.TryGetValue(key, out var known)) {
                return known;
            }
        }

        if (path.Parts.Count <= 1) {
            // The device and its storages open by name.
            var item = ItemFor(path.IsRoot ? device.ShellPath : device.ShellPath + "\\" + path.Storage);
            lock (_lock) {
                _items[key] = item;
            }

            return item;
        }

        var parent = new DevicePath(path.Device, path.Parts.Take(path.Parts.Count - 1).ToArray());
        Children(Join(parent), Find(device, parent, ct), ct);
        lock (_lock) {
            return _items.TryGetValue(key, out var found)
                ? found
                : throw new FileNotFoundException($"Not on the device: {key}", key);
        }
    }

    private List<WpdObject> Children(string folderPath, IShellItem folder, CancellationToken ct) {
        var rows = new List<WpdObject>();
        var bhid = BHID_EnumItems;
        var iid = IID_IEnumShellItems;
        int hr = folder.BindToHandler(IntPtr.Zero, ref bhid, ref iid, out object raw);
        if (hr < 0 || raw is not IEnumShellItems items) {
            throw new COMException($"The device's folder did not list: {folderPath}", hr);
        }

        try {
            var batch = new IShellItem[1];
            while (items.Next(1, batch, out uint fetched) == 0 && fetched == 1) {
                ct.ThrowIfCancellationRequested();
                var item = batch[0];
                batch[0] = null!;
                var row = Row(folderPath, item);
                lock (_lock) {
                    if (_items.Remove(row.Id, out var old)) {
                        WpdInterop.Release(old);
                    }
                    _items[row.Id] = item;
                }
                rows.Add(row);
            }
        } finally {
            WpdInterop.Release(items);
        }

        return rows;
    }

    private static WpdObject Row(string folderPath, IShellItem item) {
        bool folder = item.GetAttributes(SFGAO_FOLDER | SFGAO_STREAM, out uint attributes) >= 0
            && (attributes & SFGAO_FOLDER) != 0 && (attributes & SFGAO_STREAM) == 0;
        string name = "";
        long? size = null;
        var modified = DateTime.MinValue;
        if (item is IShellItem2 two) {
            var fileName = PKEY_FileName;
            if (two.GetString(ref fileName, out IntPtr buffer) >= 0 && buffer != IntPtr.Zero) {
                try {
                    name = Marshal.PtrToStringUni(buffer) ?? "";
                } finally {
                    Marshal.FreeCoTaskMem(buffer);
                }
            }

            var sizeKey = PKEY_Size;
            if (!folder && two.GetUInt64(ref sizeKey, out ulong bytes) >= 0) {
                size = (long)bytes;
            }

            var modifiedKey = PKEY_DateModified;
            if (two.GetFileTime(ref modifiedKey, out FILETIME time) >= 0 && time.Ticks > 0) {
                modified = DateTime.FromFileTimeUtc(time.Ticks);
            }
        }
        if (name.Length == 0) {
            name = Display(item);
        }

        name = DevicePath.SafeName(name);

        return new WpdObject(Path.Combine(folderPath, name), name, folder, size, modified);
    }

    private IShellItem ItemFor(string parsingName) {
        var iid = IID_IShellItem;
        int hr = SHCreateItemFromParsingName(parsingName, IntPtr.Zero, ref iid, out object raw);
        if (hr < 0 || raw is not IShellItem item) {
            _log.Info($"Device (shell): {parsingName} did not open (0x{hr:X8})");

            throw new IOException($"The device's place did not open (0x{hr:X8})", hr);
        }

        return item;
    }

    private static string Display(IShellItem item) {
        if (item.GetDisplayName(SIGDN_NORMALDISPLAY, out IntPtr buffer) < 0 || buffer == IntPtr.Zero) {
            return "";
        }

        try {
            return Marshal.PtrToStringUni(buffer) ?? "";
        } finally {
            Marshal.FreeCoTaskMem(buffer);
        }
    }

    private static string Join(DevicePath path) {
        return Path.Combine(new[] { DevicePath.RootOf(path.Device) }.Concat(path.Parts).ToArray());
    }
}
