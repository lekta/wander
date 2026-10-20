using System.Runtime.InteropServices;
using Wander.Core.FileSystem;
using Wander.Core.Logging;
using Wander.Core.Shell;
using static Wander.Platform.Windows.Shell.ShellItemInterop;

namespace Wander.Platform.Windows.Shell;

/// <summary>
/// Phones and cameras as folders (PLAN Y4b): the namespace's fourth branch,
/// read only. Which devices are plugged in is the shell's word - the items
/// of "This PC" behind a portable-device interface, with Explorer's names;
/// what is on them is read through Windows Portable Devices directly
/// (decision of 2026-10-19): a cold listing of a phone's camera folder
/// takes the shell half a minute to three minutes and WPD one to ten
/// seconds (stand Y4a). <see cref="ThroughShell"/> switches the content to
/// the shell, for a device WPD does not get on with.
///
/// <para>
/// A device's object ids hold for one opening of it, so the opened device
/// and every id read from it live together in a session, by the device's
/// name; a notice that a device came or went (<see cref="IVolumeChanges"/>)
/// closes the sessions, and the next look opens a fresh one and finds the
/// objects again by their names - which is what a copy that waited for its
/// phone does (PLAN Y4d).
/// </para>
///
/// <para>
/// A storage's folders the owner has no business in (<see cref="DeviceClutter"/>)
/// come out hidden and system, as protected Windows files do: the listing
/// hides them, "Защищённые системные файлы" shows them.
/// </para>
/// </summary>
internal sealed class DeviceFolders : IDeviceContent {
    /// <summary>"This PC".</summary>
    internal const string ThisPc = "::{20D04FE0-3AEA-1069-A2D8-08002B30309D}";

    /// <summary>ERROR_DEVICE_NOT_CONNECTED as an HRESULT: what a look at a device that is not there throws.</summary>
    private const int NotConnected = unchecked((int)0x8007048F);

    private readonly ILogger _log;
    private readonly Lock _lock = new();
    private readonly Dictionary<string, Session> _sessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly ShellDeviceReader _shell;

    /// <summary>The devices last read; null when a notice said they changed.</summary>
    private IReadOnlyList<Device>? _devices;

    private string _lastLogged = "";


    public DeviceFolders(ILogger log, IVolumeChanges? changes) {
        _log = log;
        _shell = new ShellDeviceReader(log);
        if (changes is not null) {
            changes.Changed += OnChanged;
        }
    }


    public bool ThroughShell { get; set; }


    public IReadOnlyList<FileSystemEntry> Devices() {
        // A listing asked for is read anew: the shell learns of a device a
        // moment after its notice, and a list read in that moment would
        // stay without it.
        lock (_lock) {
            _devices = null;
        }

        var rows = new List<FileSystemEntry>();
        foreach (var device in ReadDevices()) {
            VolumeInfo? volume = null;
            try {
                var session = SessionFor(device);
                session.Storages = null;
                volume = VolumeOf(device, StoragesOf(session));
            } catch (Exception ex) when (ex is COMException or IOException or UnauthorizedAccessException) {
                // A device that will not open now is still in the list;
                // what it holds is asked when it is opened.
            }

            rows.Add(Folder(device.Name, DevicePath.RootOf(device.Name), volume));
        }

        return rows;
    }

    public FileSystemEntry? GetEntry(string path) {
        if (DevicePath.Parse(path) is not { } parsed) {
            return null;
        }

        try {
            return Resolve(parsed, CancellationToken.None).Entry;
        } catch (Exception ex) when (ex is COMException or IOException or UnauthorizedAccessException) {
            return null;
        }
    }

    public IReadOnlyList<FileSystemEntry> Enumerate(string path, CancellationToken ct = default) {
        var parsed = DevicePath.Parse(path) ?? throw new DirectoryNotFoundException(path);
        var session = SessionFor(parsed);
        if (parsed.IsRoot) {
            // Read again, not taken from the session: a phone unlocked
            // since shows its storage now.
            session.Storages = null;

            return StoragesOf(session).Select(s => s.Entry).ToList();
        }

        var folder = Resolve(parsed, ct);

        return List(session, parsed, folder, ct);
    }

    public Stream OpenRead(string path) {
        var parsed = DevicePath.Parse(path) ?? throw new FileNotFoundException("Not on a device", path);
        for (int look = 1; ; look++) {
            var session = SessionFor(parsed);
            var node = Resolve(parsed, CancellationToken.None);
            if (node.Entry.Kind != EntryKind.File) {
                throw new FileNotFoundException("Not a file", path);
            }

            try {
                return ThroughShell
                    ? _shell.Open(session.Device, parsed, node.Entry.Size ?? 0)
                    : WpdOf(session).OpenContent(node.ObjectId, node.Entry.Size ?? 0);
            } catch (COMException ex) when (look == 1) {
                // An id from before the phone re-read its storage: the names
                // are asked again, once.
                _log.Info($"Device: {path} did not open ({ex.Message}) - looking it up again");
                Forget(session);
            }
        }
    }

    public byte[]? ReadThumbnail(string path) {
        if (ThroughShell || DevicePath.Parse(path) is not { IsRoot: false } parsed) {
            return null;
        }

        var session = SessionFor(parsed);
        var node = Resolve(parsed, CancellationToken.None);
        try {
            return node.Entry.Kind == EntryKind.File ? WpdOf(session).Thumbnail(node.ObjectId) : null;
        } catch (COMException ex) {
            throw new IOException($"The device gave no thumbnail now (0x{ex.HResult:X8})", ex);
        }
    }

    public DeviceState StateOf(string path) {
        if (DevicePath.Parse(path) is not { } parsed) {
            return DeviceState.Gone;
        }

        lock (_lock) {
            _devices = null;
        }
        if (ReadDevices().FirstOrDefault(d => Same(d.Name, parsed.Device)) is null) {
            return DeviceState.Gone;
        }

        for (int look = 1; look <= 2; look++) {
            try {
                var session = SessionFor(parsed);
                session.Storages = null;

                return StoragesOf(session).Count == 0 ? DeviceState.Locked : DeviceState.Ready;
            } catch (Exception ex) when (ex is COMException or IOException) {
                // A session from before the device went away: a fresh one.
                Session? stale;
                lock (_lock) {
                    _sessions.TryGetValue(parsed.Device, out stale);
                }
                if (stale is not null) {
                    Forget(stale);
                }
            }
        }

        return DeviceState.Gone;
    }

    /// <summary>
    /// The shell's own name for a device's place - what its icon and its
    /// data object are asked by: the device's parsing name and the names of
    /// the storage and the folders under it (stand Y4a: a PUID path does not
    /// parse back). Null when the device is not plugged in.
    /// </summary>
    public string? ShellPathOf(string path) {
        if (DevicePath.Parse(path) is not { } parsed
            || ReadDevices().FirstOrDefault(d => Same(d.Name, parsed.Device)) is not { } device) {
            return null;
        }

        return parsed.IsRoot ? device.ShellPath : device.ShellPath + "\\" + string.Join("\\", parsed.Parts);
    }

    // --- Devices and sessions -------------------------------------------

    /// <summary>The devices plugged in: read from the shell when the last list is gone stale, else the last list.</summary>
    private IReadOnlyList<Device> ReadDevices() {
        lock (_lock) {
            if (_devices is { } known) {
                return known;
            }
        }

        IReadOnlyList<Device> read;
        try {
            read = OwnApartment.Run("Wander device list", ListDevices);
        } catch (Exception ex) when (ex is COMException or InvalidCastException) {
            // "This PC" would not list: no devices this time, the drives
            // beside them are listed as ever.
            _log.Warn($"Devices: \"This PC\" not listed ({ex.Message})");
            read = Array.Empty<Device>();
        }
        lock (_lock) {
            _devices = read;
            string names = string.Join(", ", read.Select(d => d.Name));
            if (names != _lastLogged) {
                _lastLogged = names;
                // A phone may be called by its owner's name: masked like one.
                string said = string.Join(", ", read.Select(d => Log.Path(d.Name)));
                _log.Info($"Devices: {(said.Length > 0 ? said : "none")}");
            }
        }

        return read;
    }

    /// <summary>
    /// The items of "This PC" behind a portable-device interface - phones,
    /// cameras, players - with the names Explorer gives them; a second
    /// device of the same name gets "(2)".
    /// </summary>
    private List<Device> ListDevices() {
        var found = new List<(string Name, string Parsing)>();
        var iid = IID_IShellItem;
        if (SHCreateItemFromParsingName(ThisPc, IntPtr.Zero, ref iid, out object raw) < 0 || raw is not IShellItem computer) {
            return new List<Device>();
        }

        IEnumShellItems? items = null;
        try {
            var bhid = BHID_EnumItems;
            var iidEnum = IID_IEnumShellItems;
            if (computer.BindToHandler(IntPtr.Zero, ref bhid, ref iidEnum, out object rawItems) < 0
                || rawItems is not IEnumShellItems enumerator) {
                return new List<Device>();
            }

            items = enumerator;
            string wpd = WpdInterop.GUID_DEVINTERFACE_WPD.ToString("B");
            var batch = new IShellItem[1];
            while (items.Next(1, batch, out uint fetched) == 0 && fetched == 1) {
                var item = batch[0];
                batch[0] = null!;
                try {
                    string parsing = NameOf(item, SIGDN_DESKTOPABSOLUTEPARSING);
                    if (parsing.Contains(wpd, StringComparison.OrdinalIgnoreCase)) {
                        found.Add((DevicePath.SafeName(NameOf(item, SIGDN_NORMALDISPLAY)), parsing));
                    }
                } finally {
                    ShellItemInterop.Release(item);
                }
            }
        } finally {
            ShellItemInterop.Release(items);
            ShellItemInterop.Release(computer);
        }

        var devices = new List<Device>(found.Count);
        foreach (var group in found.OrderBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(f => f.Parsing, StringComparer.OrdinalIgnoreCase)
                     .GroupBy(f => f.Name, StringComparer.OrdinalIgnoreCase)) {
            int n = 0;
            foreach (var (name, parsing) in group) {
                n++;
                string pnpId = parsing.StartsWith(ThisPc + "\\", StringComparison.OrdinalIgnoreCase) ? parsing[(ThisPc.Length + 1)..] : parsing;
                devices.Add(new Device(n == 1 ? name : $"{name} ({n})", pnpId, parsing));
            }
        }

        return devices;
    }

    /// <summary>The session of a device by its name - opened anew when the device came back under another interface (another USB port).</summary>
    private Session SessionFor(DevicePath path) {
        var device = ReadDevices().FirstOrDefault(d => Same(d.Name, path.Device));
        if (device is null) {
            // Plugged in a moment ago, before any notice: the shell once more.
            lock (_lock) {
                _devices = null;
            }
            device = ReadDevices().FirstOrDefault(d => Same(d.Name, path.Device))
                ?? throw new IOException($"The device is not connected: {path.Device}", NotConnected);
        }

        return SessionFor(device);
    }

    private Session SessionFor(Device device) {
        Session? stale;
        Session session;
        lock (_lock) {
            if (_sessions.TryGetValue(device.Name, out var known) && Same(known.Device.PnpId, device.PnpId)) {
                return known;
            }

            stale = known;
            session = new Session(device);
            _sessions[device.Name] = session;
        }
        stale?.Dispose();

        return session;
    }

    /// <summary>The device opened through WPD, on first use.</summary>
    private static WpdDevice WpdOf(Session session) {
        lock (session) {
            return session.Wpd ??= WpdDevice.Open(session.Device.PnpId);
        }
    }

    private void OnChanged(object? sender, VolumeChange change) {
        if (!change.IsDevice) {
            return;
        }

        var gone = new List<Session>();
        lock (_lock) {
            _devices = null;
            foreach (var session in _sessions.Values) {
                if (Same(session.Device.PnpId, change.Root)) {
                    gone.Add(session);
                }
            }
            foreach (var session in gone) {
                _sessions.Remove(session.Device.Name);
            }
        }

        // Off the window's thread: closing a device a copy is still
        // reading from waits on that read.
        Task.Run(() => {
            foreach (var session in gone) {
                session.Dispose();
            }
            _shell.Forget();
        });
    }

    /// <summary>A session's ids are no good any more: closed, and the next look opens a new one.</summary>
    private void Forget(Session session) {
        lock (_lock) {
            if (_sessions.TryGetValue(session.Device.Name, out var current) && ReferenceEquals(current, session)) {
                _sessions.Remove(session.Device.Name);
            }
        }
        session.Dispose();
        _shell.Forget();
    }


    // --- What is on a device --------------------------------------------

    /// <summary>The node <paramref name="path"/> stands for, from what was read, listing the folders on the way that were not.</summary>
    private Node Resolve(DevicePath path, CancellationToken ct) {
        var session = SessionFor(path);
        string root = DevicePath.RootOf(path.Device);
        if (path.IsRoot) {
            return new Node(WpdInterop.DeviceObjectId, Folder(path.Device, root, null));
        }

        var storage = StoragesOf(session).FirstOrDefault(s => Same(s.Entry.Name, path.Storage!))
            ?? throw new DirectoryNotFoundException($"No such storage on {path.Device}: {path.Storage}");
        var node = storage;
        string current = storage.Entry.FullPath;
        var walked = new List<string>();
        foreach (string name in path.InStorage) {
            string next = Path.Combine(current, name);
            Node? found;
            lock (_lock) {
                session.Nodes.TryGetValue(next, out found);
            }
            if (found is null) {
                List(session, new DevicePath(path.Device, new[] { path.Storage! }.Concat(walked).ToArray()), node, ct);
                lock (_lock) {
                    session.Nodes.TryGetValue(next, out found);
                }
            }

            node = found ?? throw new FileNotFoundException($"Not on the device: {next}", next);
            current = next;
            walked.Add(name);
        }

        return node;
    }

    /// <summary>The storages of a device, from its session once read.</summary>
    private IReadOnlyList<Node> StoragesOf(Session session) {
        if (session.Storages is { } known) {
            return known;
        }

        string root = DevicePath.RootOf(session.Device.Name);
        List<Node> storages;
        if (ThroughShell) {
            storages = _shell.Storages(session.Device)
                .Select(s => new Node(s.Name, Folder(s.Name, Path.Combine(root, s.Name), null)))
                .ToList();
        } else {
            storages = WpdOf(session).Storages()
                .Select(s => {
                    string name = DevicePath.SafeName(s.Name);
                    string path = Path.Combine(root, name);
                    var volume = s.Capacity is { } total
                        ? new VolumeInfo(path, name, "", VolumeKind.Removable, total, s.Free ?? 0, IsReady: true)
                        : null;

                    return new Node(s.Id, Folder(name, path, volume));
                })
                .ToList();
        }

        lock (_lock) {
            session.Storages = storages;
            foreach (var storage in storages) {
                session.Nodes[storage.Entry.FullPath] = storage;
            }
        }

        return storages;
    }

    /// <summary>
    /// A folder's listing, read now - what an F5 asks for - and kept in the
    /// session for finding its objects later. The clutter at a storage's
    /// root is marked; a standard folder there is asked whether it is empty
    /// (one request each, 2-16 ms on a phone).
    /// </summary>
    private IReadOnlyList<FileSystemEntry> List(Session session, DevicePath folderPath, Node folder, CancellationToken ct) {
        string here = Path.Combine(new[] { DevicePath.RootOf(folderPath.Device) }.Concat(folderPath.Parts).ToArray());
        var inStorage = folderPath.InStorage;
        var rows = ThroughShell
            ? _shell.List(session.Device, folderPath, ct)
            : WpdOf(session).Children(folder.ObjectId, ct);

        var nodes = new List<Node>(rows.Count);
        foreach (var row in rows) {
            string name = DevicePath.SafeName(row.Name);
            var segments = inStorage.Append(name).ToArray();
            bool? empty = null;
            if (row.IsFolder && inStorage.Count == 0 && DeviceClutter.IsEmptyStandard(name)) {
                empty = LooksEmpty(session, folderPath, row, ct);
            }
            bool clutter = DeviceClutter.IsClutter(segments, row.IsFolder ? empty : null);
            var entry = new FileSystemEntry(
                Name: name,
                FullPath: Path.Combine(here, name),
                Kind: row.IsFolder ? EntryKind.Directory : EntryKind.File,
                Size: row.IsFolder ? null : row.Size,
                ModifiedUtc: row.ModifiedUtc,
                IsHidden: clutter,
                IsReadOnly: false,
                IsSystem: clutter,
                LinksToDirectory: false);
            nodes.Add(new Node(row.Id, entry));
        }

        lock (_lock) {
            foreach (var node in nodes) {
                session.Nodes[node.Entry.FullPath] = node;
            }
        }

        return nodes.Select(n => n.Entry).ToList();
    }

    /// <summary>Whether a standard folder at a storage's root holds nothing but dot-names.</summary>
    private bool LooksEmpty(Session session, DevicePath parent, WpdObject folder, CancellationToken ct) {
        try {
            var names = ThroughShell
                ? _shell.List(session.Device, new DevicePath(parent.Device, parent.Parts.Append(folder.Name).ToArray()), ct).Select(r => r.Name)
                : WpdOf(session).Children(folder.Id, ct).Select(r => r.Name);

            return DeviceClutter.LooksEmpty(names);
        } catch (COMException) {
            return false;
        }
    }

    private static VolumeInfo? VolumeOf(Device device, IReadOnlyList<Node> storages) {
        var volumes = storages.Select(s => s.Entry.Volume).OfType<VolumeInfo>().ToList();
        if (volumes.Count == 0) {
            return null;
        }

        return new VolumeInfo(DevicePath.RootOf(device.Name), device.Name, "", VolumeKind.Removable,
            volumes.Sum(v => v.TotalBytes), volumes.Sum(v => v.FreeBytes), IsReady: true);
    }

    private static FileSystemEntry Folder(string name, string path, VolumeInfo? volume) {
        return new FileSystemEntry(
            Name: name,
            FullPath: path,
            Kind: EntryKind.Directory,
            Size: volume?.TotalBytes,
            ModifiedUtc: DateTime.MinValue,
            IsHidden: false,
            IsReadOnly: false,
            IsSystem: false,
            LinksToDirectory: false,
            Volume: volume);
    }

    private static string NameOf(IShellItem item, uint form) {
        if (item.GetDisplayName(form, out IntPtr buffer) < 0 || buffer == IntPtr.Zero) {
            return "";
        }

        try {
            return Marshal.PtrToStringUni(buffer) ?? "";
        } finally {
            Marshal.FreeCoTaskMem(buffer);
        }
    }

    private static bool Same(string a, string b) {
        return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }


    /// <summary>A device plugged in: Wander's name for it, the interface it opens by, the shell's name for it.</summary>
    internal sealed record Device(string Name, string PnpId, string ShellPath);


    /// <summary>What a path stands for on a device: the object, and its row.</summary>
    private sealed record Node(string ObjectId, FileSystemEntry Entry);


    /// <summary>One opening of a device and every id read from it.</summary>
    private sealed class Session : IDisposable {
        public Session(Device device) {
            Device = device;
        }


        public Device Device { get; }

        public WpdDevice? Wpd { get; set; }

        public IReadOnlyList<Node>? Storages { get; set; }

        /// <summary>Path to object, for everything listed so far.</summary>
        public Dictionary<string, Node> Nodes { get; } = new(StringComparer.OrdinalIgnoreCase);


        public void Dispose() {
            lock (this) {
                Wpd?.Dispose();
                Wpd = null;
            }
        }
    }
}
