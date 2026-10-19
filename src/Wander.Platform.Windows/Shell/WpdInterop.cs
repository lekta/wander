using System.Runtime.InteropServices;
using static Wander.Platform.Windows.Shell.ShellItemInterop;
using ComTypes = System.Runtime.InteropServices.ComTypes;

namespace Wander.Platform.Windows.Shell;

// Names as the Windows docs spell them, in the whole file.
// ReSharper disable InconsistentNaming

/// <summary>
/// Windows Portable Devices, the part a reader needs: open a device, list
/// an object's children, read their properties - a batch at a time where
/// the device can (MTP phones), one by one where it cannot (a camera over
/// PTP, stand Y4a) - and open a file's bytes or its thumbnail as a stream.
/// Taken from the stand of 2026-10-19 (<c>artifacts\stands\DeviceStand</c>).
///
/// <para>
/// Interface member order is the vtable order. Where a declaration stops
/// early, nothing after its last member is ever called - an interface is
/// read from the top.
/// </para>
/// </summary>
internal static class WpdInterop {
    /// <summary>The device's own object: its children are its storages.</summary>
    internal const string DeviceObjectId = "DEVICE";

    internal const uint STGM_READ = 0;

    internal const uint GENERIC_READ = 0x80000000;
    internal const uint SECURITY_IMPERSONATION = 0x00020000;

    internal const ushort VT_DATE = 7;

    /// <summary>The interface class of a portable device - in its interface path, and what removal notices are asked for.</summary>
    internal static readonly Guid GUID_DEVINTERFACE_WPD = new("6ac27878-a6fa-4155-ba85-f98f491d4f33");

    /// <summary>The free-threaded device class: callable from any thread, no apartment to keep.</summary>
    internal static readonly Guid CLSID_PortableDeviceFTM = new("f7c0039a-4762-488a-b4b3-760ef9a1ba9b");
    internal static readonly Guid CLSID_PortableDeviceValues = new("0c15d503-d017-47ce-9016-7b3f978721cc");
    internal static readonly Guid CLSID_PortableDeviceKeyCollection = new("de2d022d-2480-43be-97f0-d1fa2cf98f4f");
    internal static readonly Guid CLSID_PortableDevicePropVariantCollection = new("08a99e2f-6d6d-4b80-af5a-baf2bcbe4cb9");

    internal static readonly Guid WPD_CONTENT_TYPE_FOLDER = new("27e2e392-a111-48e0-ab0c-e17705a05f85");
    internal static readonly Guid WPD_CONTENT_TYPE_FUNCTIONAL_OBJECT = new("99ed0160-17ff-4c44-9d98-1d7a6f941921");
    internal static readonly Guid WPD_FUNCTIONAL_CATEGORY_STORAGE = new("23f05bbc-15de-4c2a-a55b-a9af5ce412ef");

    private static readonly Guid _objectProperties = new("ef6b490d-5cd8-437a-affc-da8b60ee4a3c");
    private static readonly Guid _functionalProperties = new("8f052d93-abca-4fc5-a5ac-b01df4dbe598");
    private static readonly Guid _storageProperties = new("01a3057a-74d6-4e80-bea7-dc4c212ce50a");
    private static readonly Guid _clientProperties = new("204d9f0c-2292-4080-9f42-40664e70f859");

    internal static PROPERTYKEY WPD_OBJECT_ID = new(_objectProperties, 2);
    internal static PROPERTYKEY WPD_OBJECT_NAME = new(_objectProperties, 4);
    internal static PROPERTYKEY WPD_OBJECT_CONTENT_TYPE = new(_objectProperties, 7);
    internal static PROPERTYKEY WPD_OBJECT_SIZE = new(_objectProperties, 11);
    internal static PROPERTYKEY WPD_OBJECT_ORIGINAL_FILE_NAME = new(_objectProperties, 12);
    internal static PROPERTYKEY WPD_OBJECT_DATE_MODIFIED = new(_objectProperties, 19);
    internal static PROPERTYKEY WPD_FUNCTIONAL_OBJECT_CATEGORY = new(_functionalProperties, 2);
    internal static PROPERTYKEY WPD_STORAGE_CAPACITY = new(_storageProperties, 4);
    internal static PROPERTYKEY WPD_STORAGE_FREE_SPACE_IN_BYTES = new(_storageProperties, 5);
    internal static PROPERTYKEY WPD_STORAGE_DESCRIPTION = new(_storageProperties, 7);
    internal static PROPERTYKEY WPD_CLIENT_NAME = new(_clientProperties, 2);
    internal static PROPERTYKEY WPD_CLIENT_SECURITY_QUALITY_OF_SERVICE = new(_clientProperties, 8);
    internal static PROPERTYKEY WPD_CLIENT_DESIRED_ACCESS = new(_clientProperties, 9);

    /// <summary>The bytes of an object - a file's content.</summary>
    internal static PROPERTYKEY WPD_RESOURCE_DEFAULT = new(new("e81e79be-34f0-41bf-b53f-f1a06ae87842"), 0);

    /// <summary>The device's own small picture of an object.</summary>
    internal static PROPERTYKEY WPD_RESOURCE_THUMBNAIL = new(new("c7c407ba-98fa-46b5-9960-23fec124cfde"), 0);


    internal static T Create<T>(Guid clsid) {
        return (T)Activator.CreateInstance(Type.GetTypeFromCLSID(clsid, throwOnError: true)!)!;
    }

    internal static void Release(object? comObject) {
        if (comObject is not null && Marshal.IsComObject(comObject)) {
            Marshal.ReleaseComObject(comObject);
        }
    }


    /// <summary>A <c>PROPVARIANT</c> as WPD hands one over: the type, and the 8 bytes of the value. 24 bytes on x64.</summary>
    [StructLayout(LayoutKind.Explicit, Size = 24)]
    internal struct PropVariant {
        [FieldOffset(0)] public ushort Vt;
        [FieldOffset(8)] public IntPtr Pointer;
        [FieldOffset(8)] public double Double;
    }


    [ComImport]
    [Guid("625e2df8-6392-4cf0-9ad1-3cfa5f17775c")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IPortableDevice {
        void Open([MarshalAs(UnmanagedType.LPWStr)] string pszPnPDeviceID, IPortableDeviceValues pClientInfo);

        void SendCommand(uint dwFlags, IPortableDeviceValues pParameters, out IPortableDeviceValues ppResults);

        void Content(out IPortableDeviceContent ppContent);

        void Capabilities(out IntPtr ppCapabilities);

        void Cancel();

        void Close();
    }


    [ComImport]
    [Guid("6848f6f2-3155-4f86-b6f5-263eeeab3143")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IPortableDeviceValues {
        void GetCount(out uint pcelt);

        void GetAt(uint index, out PROPERTYKEY pKey, out PropVariant pValue);

        void SetValue(ref PROPERTYKEY key, ref PropVariant pValue);

        void GetValue(ref PROPERTYKEY key, out PropVariant pValue);

        void SetStringValue(ref PROPERTYKEY key, [MarshalAs(UnmanagedType.LPWStr)] string Value);

        void GetStringValue(ref PROPERTYKEY key, [MarshalAs(UnmanagedType.LPWStr)] out string pValue);

        void SetUnsignedIntegerValue(ref PROPERTYKEY key, uint Value);

        void GetUnsignedIntegerValue(ref PROPERTYKEY key, out uint pValue);

        void SetSignedIntegerValue(ref PROPERTYKEY key, int Value);

        void GetSignedIntegerValue(ref PROPERTYKEY key, out int pValue);

        void SetUnsignedLargeIntegerValue(ref PROPERTYKEY key, ulong Value);

        void GetUnsignedLargeIntegerValue(ref PROPERTYKEY key, out ulong pValue);

        void SetSignedLargeIntegerValue(ref PROPERTYKEY key, long Value);

        void GetSignedLargeIntegerValue(ref PROPERTYKEY key, out long pValue);

        void SetFloatValue(ref PROPERTYKEY key, float Value);

        void GetFloatValue(ref PROPERTYKEY key, out float pValue);

        void SetErrorValue(ref PROPERTYKEY key, int Value);

        void GetErrorValue(ref PROPERTYKEY key, out int pValue);

        void SetKeyValue(ref PROPERTYKEY key, ref PROPERTYKEY Value);

        void GetKeyValue(ref PROPERTYKEY key, out PROPERTYKEY pValue);

        void SetBoolValue(ref PROPERTYKEY key, int Value);

        void GetBoolValue(ref PROPERTYKEY key, out int pValue);

        void SetIUnknownValue(ref PROPERTYKEY key, [MarshalAs(UnmanagedType.IUnknown)] object pValue);

        void GetIUnknownValue(ref PROPERTYKEY key, [MarshalAs(UnmanagedType.IUnknown)] out object ppValue);

        void SetGuidValue(ref PROPERTYKEY key, ref Guid Value);

        void GetGuidValue(ref PROPERTYKEY key, out Guid pValue);
    }


    [ComImport]
    [Guid("6a96ed84-7c73-4480-9938-bf5af477d426")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IPortableDeviceContent {
        void EnumObjects(uint dwFlags, [MarshalAs(UnmanagedType.LPWStr)] string pszParentObjectID,
            IPortableDeviceValues? pFilter, out IEnumPortableDeviceObjectIDs ppEnum);

        void Properties(out IPortableDeviceProperties ppProperties);

        void Transfer(out IPortableDeviceResources ppResources);
    }


    [ComImport]
    [Guid("fd8878ac-d841-4d17-891c-e6829cdb6934")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IPortableDeviceResources {
        void GetSupportedResources([MarshalAs(UnmanagedType.LPWStr)] string pszObjectID, out IPortableDeviceKeyCollection ppKeys);

        void GetResourceAttributes([MarshalAs(UnmanagedType.LPWStr)] string pszObjectID, ref PROPERTYKEY Key,
            out IPortableDeviceValues ppResourceAttributes);

        void GetStream([MarshalAs(UnmanagedType.LPWStr)] string pszObjectID, ref PROPERTYKEY Key, uint dwMode,
            ref uint pdwOptimalBufferSize, out ComTypes.IStream ppStream);
    }


    [ComImport]
    [Guid("10ece955-cf41-4728-bfa0-41eedf1bbf19")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IEnumPortableDeviceObjectIDs {
        [PreserveSig]
        int Next(uint cObjects, [Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] IntPtr[] pObjIDs, out uint pcFetched);
    }


    [ComImport]
    [Guid("7f6d695c-03df-4439-a809-59266beee3a6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IPortableDeviceProperties {
        void GetSupportedProperties([MarshalAs(UnmanagedType.LPWStr)] string pszObjectID, out IPortableDeviceKeyCollection ppKeys);

        void GetPropertyAttributes([MarshalAs(UnmanagedType.LPWStr)] string pszObjectID, ref PROPERTYKEY Key,
            out IPortableDeviceValues ppAttributes);

        void GetValues([MarshalAs(UnmanagedType.LPWStr)] string pszObjectID, IPortableDeviceKeyCollection pKeys,
            out IPortableDeviceValues ppValues);
    }


    [ComImport]
    [Guid("dada2357-e0ad-492e-98db-dd61c53ba353")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IPortableDeviceKeyCollection {
        void GetCount(out uint pcElems);

        void GetAt(uint dwIndex, out PROPERTYKEY pKey);

        void Add(ref PROPERTYKEY Key);
    }


    [ComImport]
    [Guid("89b2e422-4f1b-4316-bcef-a44afea83eb3")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IPortableDevicePropVariantCollection {
        void GetCount(out uint pcElems);

        void GetAt(uint dwIndex, out PropVariant pValue);

        void Add(ref PropVariant pValue);
    }


    [ComImport]
    [Guid("482b05c0-4056-44ed-9e0f-5e23b009da93")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IPortableDevicePropertiesBulk {
        void QueueGetValuesByObjectList(IPortableDevicePropVariantCollection pObjectIDs, IPortableDeviceKeyCollection pKeys,
            IPortableDevicePropertiesBulkCallback pCallback, out Guid pContext);

        void QueueGetValuesByObjectFormat(ref Guid pguidObjectFormat, [MarshalAs(UnmanagedType.LPWStr)] string pszParentObjectID,
            uint dwDepth, IPortableDeviceKeyCollection pKeys, IPortableDevicePropertiesBulkCallback pCallback, out Guid pContext);

        void QueueSetValuesByObjectList(IntPtr pObjectValues, IPortableDevicePropertiesBulkCallback pCallback, out Guid pContext);

        void Start(ref Guid pContext);

        void Cancel(ref Guid pContext);
    }


    [ComImport]
    [Guid("9deacb80-11e8-40e3-a9f3-f557986a7845")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IPortableDevicePropertiesBulkCallback {
        [PreserveSig]
        int OnStart(ref Guid pContext);

        [PreserveSig]
        int OnProgress(ref Guid pContext, IPortableDeviceValuesCollection pResults);

        [PreserveSig]
        int OnEnd(ref Guid pContext, int hrStatus);
    }


    [ComImport]
    [Guid("6e3f2d79-4e07-48c4-8208-d8c2e5af4a99")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IPortableDeviceValuesCollection {
        void GetCount(out uint pcElems);

        void GetAt(uint dwIndex, out IPortableDeviceValues ppValues);
    }


    [DllImport("ole32.dll")]
    internal static extern int PropVariantClear(ref PropVariant pvar);
}
