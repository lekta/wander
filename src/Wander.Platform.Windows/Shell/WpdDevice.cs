using System.Runtime.InteropServices;
using static Wander.Platform.Windows.Shell.ShellItemInterop;
using static Wander.Platform.Windows.Shell.WpdInterop;
using ComTypes = System.Runtime.InteropServices.ComTypes;

namespace Wander.Platform.Windows.Shell;

/// <summary>
/// One phone or camera opened through Windows Portable Devices, read only:
/// its storages, a folder's children with their names, sizes and dates, a
/// file's bytes and its thumbnail. The object ids it hands out are good for
/// this opening only - the device gives new ones after it was away (stand
/// Y4a) - so whoever keeps them keeps them with this object.
///
/// <para>
/// The free-threaded class (<see cref="CLSID_PortableDeviceFTM"/>): any
/// thread may call, none has to be kept for it. A listing asks for the
/// properties of the whole folder in one batch where the device takes one
/// (an MTP phone: 1743 photos in 0.04 s after the ids) and one object at a
/// time where it does not (a camera over PTP answers <c>E_NOINTERFACE</c>,
/// ~6 ms an object).
/// </para>
/// </summary>
internal sealed class WpdDevice : IDisposable {
    /// <summary>How long a batch of properties may take before the listing gives up on it and reads one by one.</summary>
    private static readonly TimeSpan _bulkTimeout = TimeSpan.FromMinutes(3);

    /// <summary>The most of a thumbnail read into memory: the device's own are tens of kilobytes.</summary>
    private const int MaxThumbnailBytes = 4 << 20;

    private readonly IPortableDevice _device;
    private readonly IPortableDeviceContent _content;
    private readonly IPortableDeviceProperties _properties;
    private readonly IPortableDeviceResources _resources;

    /// <summary>Null when the device reads properties only one object at a time.</summary>
    private readonly IPortableDevicePropertiesBulk? _bulk;

    private bool _closed;


    private WpdDevice(string pnpId, IPortableDevice device) {
        PnpId = pnpId;
        _device = device;
        _device.Content(out _content);
        _content.Properties(out _properties);
        _content.Transfer(out _resources);
        _bulk = _properties as IPortableDevicePropertiesBulk;
    }


    /// <summary>The device's interface path - what it was opened by.</summary>
    public string PnpId { get; }


    /// <summary>Opens the device for reading. Throws when it will not open - unplugged, or held by a program that asked for it alone.</summary>
    public static WpdDevice Open(string pnpId) {
        var client = Create<IPortableDeviceValues>(CLSID_PortableDeviceValues);
        var device = Create<IPortableDevice>(CLSID_PortableDeviceFTM);
        try {
            var name = WPD_CLIENT_NAME;
            client.SetStringValue(ref name, "Wander");
            var qos = WPD_CLIENT_SECURITY_QUALITY_OF_SERVICE;
            client.SetUnsignedIntegerValue(ref qos, SECURITY_IMPERSONATION);
            var access = WPD_CLIENT_DESIRED_ACCESS;
            client.SetUnsignedIntegerValue(ref access, GENERIC_READ);
            device.Open(pnpId, client);

            return new WpdDevice(pnpId, device);
        } catch {
            Release(device);

            throw;
        } finally {
            Release(client);
        }
    }


    /// <summary>The storages: a phone's shared storage, a camera's cards. None while a phone is locked (stand Y4a).</summary>
    public IReadOnlyList<WpdObject> Storages() {
        var storages = new List<WpdObject>();
        var keys = Keys(WPD_OBJECT_ID, WPD_OBJECT_NAME, WPD_OBJECT_CONTENT_TYPE, WPD_FUNCTIONAL_OBJECT_CATEGORY,
            WPD_STORAGE_DESCRIPTION, WPD_STORAGE_CAPACITY, WPD_STORAGE_FREE_SPACE_IN_BYTES);
        try {
            foreach (string id in ChildIds(DeviceObjectId, CancellationToken.None)) {
                _properties.GetValues(id, keys, out var values);
                try {
                    // A functional object of another kind - a phone's
                    // "rendering information" - is no storage; one that
                    // names no category is taken for one.
                    if (GuidOf(values, WPD_FUNCTIONAL_OBJECT_CATEGORY) is { } category && category != WPD_FUNCTIONAL_CATEGORY_STORAGE) {
                        continue;
                    }

                    string name = StringOf(values, WPD_OBJECT_NAME) ?? StringOf(values, WPD_STORAGE_DESCRIPTION) ?? id;
                    storages.Add(new WpdObject(id, name, IsFolder: true, Size: null, ModifiedUtc: DateTime.MinValue) {
                        Capacity = (long?)ULongOf(values, WPD_STORAGE_CAPACITY),
                        Free = (long?)ULongOf(values, WPD_STORAGE_FREE_SPACE_IN_BYTES),
                    });
                } finally {
                    Release(values);
                }
            }
        } finally {
            Release(keys);
        }

        return storages;
    }

    /// <summary>
    /// What a folder (or a storage) holds, with the names, sizes and dates a
    /// listing shows. In the device's order.
    /// </summary>
    public IReadOnlyList<WpdObject> Children(string parentId, CancellationToken ct) {
        var ids = ChildIds(parentId, ct);
        if (ids.Count == 0) {
            return Array.Empty<WpdObject>();
        }

        var keys = Keys(WPD_OBJECT_ID, WPD_OBJECT_NAME, WPD_OBJECT_ORIGINAL_FILE_NAME, WPD_OBJECT_CONTENT_TYPE,
            WPD_OBJECT_SIZE, WPD_OBJECT_DATE_MODIFIED);
        try {
            if (_bulk is not null && Bulk(ids, keys, ct) is { } read) {
                return ids.Select(id => read.TryGetValue(id, out var row) ? row : null).OfType<WpdObject>().ToList();
            }

            var rows = new List<WpdObject>(ids.Count);
            foreach (string id in ids) {
                ct.ThrowIfCancellationRequested();
                _properties.GetValues(id, keys, out var values);
                try {
                    rows.Add(Read(values, id));
                } finally {
                    Release(values);
                }
            }

            return rows;
        } finally {
            Release(keys);
        }
    }

    /// <summary>A file's bytes from its first, as a stream of the length the listing gave.</summary>
    public Stream OpenContent(string objectId, long length) {
        uint optimal = 0;
        var resource = WPD_RESOURCE_DEFAULT;
        _resources.GetStream(objectId, ref resource, STGM_READ, ref optimal, out ComTypes.IStream stream);

        return new ComReadStream(stream, length);
    }

    /// <summary>The device's own picture of a file; null when it keeps none - a device that did not answer throws.</summary>
    public byte[]? Thumbnail(string objectId) {
        uint optimal = 0;
        var resource = WPD_RESOURCE_THUMBNAIL;
        _resources.GetStream(objectId, ref resource, STGM_READ, ref optimal, out ComTypes.IStream stream);
        using var read = new ComReadStream(stream, 0);
        using var bytes = new MemoryStream();
        byte[] buffer = new byte[64 * 1024];
        int got;
        while ((got = read.Read(buffer, 0, buffer.Length)) > 0) {
            bytes.Write(buffer, 0, got);
            if (bytes.Length > MaxThumbnailBytes) {
                return null;
            }
        }

        return bytes.Length > 0 ? bytes.ToArray() : null;
    }

    public void Dispose() {
        if (_closed) {
            return;
        }

        _closed = true;
        try {
            _device.Close();
        } catch (COMException) {
            // Gone already: there is nothing left to close.
        }
        Release(_resources);
        Release(_properties);
        Release(_content);
        Release(_device);
    }


    private List<string> ChildIds(string parentId, CancellationToken ct) {
        _content.EnumObjects(0, parentId, null, out var objects);
        var ids = new List<string>();
        var batch = new IntPtr[256];
        try {
            while (true) {
                ct.ThrowIfCancellationRequested();
                int hr = objects.Next((uint)batch.Length, batch, out uint fetched);
                for (int i = 0; i < fetched; i++) {
                    ids.Add(Marshal.PtrToStringUni(batch[i]) ?? "");
                    Marshal.FreeCoTaskMem(batch[i]);
                }
                if (hr != 0 || fetched == 0) {
                    break;
                }
            }
        } finally {
            Release(objects);
        }

        return ids;
    }

    /// <summary>
    /// The properties of every object in <paramref name="ids"/> in one batch,
    /// by id; null when the batch failed or took too long - the caller then
    /// reads one by one.
    /// </summary>
    private Dictionary<string, WpdObject>? Bulk(List<string> ids, IPortableDeviceKeyCollection keys, CancellationToken ct) {
        var list = Create<IPortableDevicePropVariantCollection>(CLSID_PortableDevicePropVariantCollection);
        using var callback = new BulkCallback();
        Guid context = Guid.Empty;
        bool started = false;
        try {
            foreach (string id in ids) {
                var value = new PropVariant { Vt = 31 /* VT_LPWSTR */, Pointer = Marshal.StringToCoTaskMemUni(id) };
                try {
                    list.Add(ref value);
                } finally {
                    Marshal.FreeCoTaskMem(value.Pointer);
                }
            }

            _bulk!.QueueGetValuesByObjectList(list, keys, callback, out context);
            _bulk.Start(ref context);
            started = true;
            int signalled = WaitHandle.WaitAny(new[] { callback.Done.WaitHandle, ct.WaitHandle }, _bulkTimeout);
            if (signalled != 0) {
                _bulk.Cancel(ref context);
                ct.ThrowIfCancellationRequested();

                return null;
            }

            return callback.Status >= 0 ? callback.Rows : null;
        } catch (COMException) {
            if (started) {
                try {
                    _bulk!.Cancel(ref context);
                } catch (COMException) {
                    // Already over.
                }
            }

            return null;
        } finally {
            Release(list);
        }
    }

    private static WpdObject Read(IPortableDeviceValues values, string id) {
        // The file's own name - with its extension - where the device gives
        // it; its title where it does not (both the same on the phones and
        // the camera at hand, stand Y4a).
        string name = StringOf(values, WPD_OBJECT_ORIGINAL_FILE_NAME) ?? StringOf(values, WPD_OBJECT_NAME) ?? id;
        var type = GuidOf(values, WPD_OBJECT_CONTENT_TYPE);
        bool folder = type == WPD_CONTENT_TYPE_FOLDER || type == WPD_CONTENT_TYPE_FUNCTIONAL_OBJECT;

        return new WpdObject(id, name, folder, folder ? null : (long?)ULongOf(values, WPD_OBJECT_SIZE), DateOf(values, WPD_OBJECT_DATE_MODIFIED));
    }

    private static IPortableDeviceKeyCollection Keys(params PROPERTYKEY[] list) {
        var keys = Create<IPortableDeviceKeyCollection>(CLSID_PortableDeviceKeyCollection);
        foreach (var key in list) {
            var k = key;
            keys.Add(ref k);
        }

        return keys;
    }

    private static string? StringOf(IPortableDeviceValues values, PROPERTYKEY key) {
        try {
            values.GetStringValue(ref key, out string value);

            return string.IsNullOrEmpty(value) ? null : value;
        } catch (COMException) {
            return null;
        }
    }

    private static ulong? ULongOf(IPortableDeviceValues values, PROPERTYKEY key) {
        try {
            values.GetUnsignedLargeIntegerValue(ref key, out ulong value);

            return value;
        } catch (COMException) {
            return null;
        }
    }

    private static Guid? GuidOf(IPortableDeviceValues values, PROPERTYKEY key) {
        try {
            values.GetGuidValue(ref key, out Guid value);

            return value;
        } catch (COMException) {
            return null;
        }
    }

    /// <summary>A WPD date - <c>VT_DATE</c> in the computer's local time (stand Y4a) - as UTC; <see cref="DateTime.MinValue"/> when there is none.</summary>
    private static DateTime DateOf(IPortableDeviceValues values, PROPERTYKEY key) {
        try {
            values.GetValue(ref key, out PropVariant value);
            try {
                return value.Vt == VT_DATE
                    ? DateTime.SpecifyKind(DateTime.FromOADate(value.Double), DateTimeKind.Local).ToUniversalTime()
                    : DateTime.MinValue;
            } finally {
                _ = PropVariantClear(ref value);
            }
        } catch (Exception ex) when (ex is COMException or ArgumentException) {
            return DateTime.MinValue;
        }
    }


    /// <summary>The answers of a batch, by object id, as they arrive on the device's own thread.</summary>
    [ComVisible(true)]
    internal sealed class BulkCallback : IPortableDevicePropertiesBulkCallback, IDisposable {
        public ManualResetEventSlim Done { get; } = new();

        public Dictionary<string, WpdObject> Rows { get; } = new(StringComparer.Ordinal);

        public int Status { get; private set; }


        public int OnStart(ref Guid pContext) {
            return 0;
        }

        public int OnProgress(ref Guid pContext, IPortableDeviceValuesCollection pResults) {
            try {
                pResults.GetCount(out uint count);
                for (uint i = 0; i < count; i++) {
                    pResults.GetAt(i, out var values);
                    try {
                        if (StringOf(values, WPD_OBJECT_ID) is { } id) {
                            lock (Rows) {
                                Rows[id] = Read(values, id);
                            }
                        }
                    } finally {
                        Release(values);
                    }
                }
            } catch (COMException) {
                // A broken batch ends with a status of its own in OnEnd.
            }

            return 0;
        }

        public int OnEnd(ref Guid pContext, int hrStatus) {
            Status = hrStatus;
            Done.Set();

            return 0;
        }

        public void Dispose() {
            Done.Dispose();
        }
    }
}


/// <summary>One object of a device: a storage, a folder or a file.</summary>
/// <param name="Size">A file's size; null for a folder.</param>
/// <param name="ModifiedUtc">Its time of last change; <see cref="DateTime.MinValue"/> when the device gives none.</param>
internal sealed record WpdObject(string Id, string Name, bool IsFolder, long? Size, DateTime ModifiedUtc) {
    /// <summary>A storage's size, in bytes.</summary>
    public long? Capacity { get; init; }

    /// <summary>A storage's free space, in bytes.</summary>
    public long? Free { get; init; }
}
