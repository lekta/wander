using System.Runtime.InteropServices;

namespace Wander.Platform.Windows.Shell;

/// <summary>
/// The <c>IShellItem</c> family, used by <see cref="ShellArchiveFolder"/> to
/// browse and unpack archives. Separate from
/// <see cref="ShellContextMenuInterop"/> (the older <c>IShellFolder</c> /
/// PIDL surface) because these are the modern, path-based calls and mixing
/// the two in one file made neither readable.
///
/// <para>
/// Interface member order is the vtable order - do not reorder or omit
/// members, even unused ones, or every call after the gap lands on the
/// wrong function pointer. <see cref="IFileOperation"/> is declared in
/// full, all twenty methods, for exactly that reason.
/// </para>
/// </summary>
internal static class ShellItemInterop {
    internal static Guid IID_IShellItem = new("43826d1e-e718-42ee-bc55-a1e261c37bfe");
    internal static Guid IID_IShellItem2 = new("7e9fb0d3-919f-4307-ab2e-9b1860310c93");
    internal static Guid IID_IEnumShellItems = new("70629033-e363-4a28-a567-0db78006e6d7");

    /// <summary>Bind handler that hands back an enumerator over the children of a folder.</summary>
    internal static Guid BHID_EnumItems = new("94f60519-2850-4924-aa5a-d15e84868039");

    internal static Guid CLSID_FileOperation = new("3ad05575-8857-4850-9277-11b85bdb8e09");
    internal static Guid IID_IFileOperation = new("947aab5f-0a5c-4c13-b4d6-4bf7836fc9f8");

    /// <summary>The item is a folder - a subfolder inside an archive included.</summary>
    internal const uint SFGAO_FOLDER = 0x20000000;

    /// <summary>
    /// Everything off: no progress dialog, no confirmations, no error
    /// popups, no "create folder" prompt. Wander asks its own questions
    /// before the engine is started and reports the outcome itself.
    /// </summary>
    internal const uint FOF_NO_UI = 0x0004 | 0x0010 | 0x0400 | 0x0200;

    /// <summary>Full parsing path - inside an archive that is what the address bar shows.</summary>
    internal const uint SIGDN_DESKTOPABSOLUTEPARSING = 0x80028000;

    /// <summary>The name of the item, unaffected by "hide known extensions".</summary>
    internal const uint SIGDN_PARENTRELATIVEPARSING = 0x80018001;

    internal const uint CLSCTX_INPROC_SERVER = 0x1;

    internal const int E_ABORT = unchecked((int)0x80004004);


    /// <summary>System.Size and System.DateModified, both from the storage property set.</summary>
    internal static PROPERTYKEY PKEY_Size = new(new("b725f130-47ef-101a-a5f1-02608c9eebac"), 12);
    internal static PROPERTYKEY PKEY_DateModified = new(new("b725f130-47ef-101a-a5f1-02608c9eebac"), 14);


    [StructLayout(LayoutKind.Sequential)]
    internal struct PROPERTYKEY {
        public Guid fmtid;
        public uint pid;

        public PROPERTYKEY(Guid formatId, uint propertyId) {
            fmtid = formatId;
            pid = propertyId;
        }
    }


    [StructLayout(LayoutKind.Sequential)]
    internal struct FILETIME {
        public uint dwLowDateTime;
        public uint dwHighDateTime;

        public readonly long Ticks => ((long)dwHighDateTime << 32) | dwLowDateTime;
    }


    [ComImport]
    [Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IShellItem {
        [PreserveSig]
        int BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid,
            [MarshalAs(UnmanagedType.Interface)] out object ppv);

        [PreserveSig]
        int GetParent(out IShellItem ppsi);

        [PreserveSig]
        int GetDisplayName(uint sigdnName, out IntPtr ppszName);

        [PreserveSig]
        int GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);

        [PreserveSig]
        int Compare(IShellItem psi, uint hint, out int piOrder);
    }


    /// <summary>
    /// <see cref="IShellItem"/> plus the property-store accessors. Declared
    /// as a separate interface rather than by inheritance so the five base
    /// methods stay visible at the top of the vtable, where they belong.
    /// </summary>
    [ComImport]
    [Guid("7e9fb0d3-919f-4307-ab2e-9b1860310c93")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IShellItem2 {
        [PreserveSig]
        int BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid,
            [MarshalAs(UnmanagedType.Interface)] out object ppv);

        [PreserveSig]
        int GetParent(out IShellItem ppsi);

        [PreserveSig]
        int GetDisplayName(uint sigdnName, out IntPtr ppszName);

        [PreserveSig]
        int GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);

        [PreserveSig]
        int Compare(IShellItem psi, uint hint, out int piOrder);

        [PreserveSig]
        int GetPropertyStore(uint flags, ref Guid riid,
            [MarshalAs(UnmanagedType.Interface)] out object ppv);

        [PreserveSig]
        int GetPropertyStoreWithCreateObject(uint flags, IntPtr punkCreateObject, ref Guid riid,
            [MarshalAs(UnmanagedType.Interface)] out object ppv);

        [PreserveSig]
        int GetPropertyStoreForKeys(IntPtr rgKeys, uint cKeys, uint flags, ref Guid riid,
            [MarshalAs(UnmanagedType.Interface)] out object ppv);

        [PreserveSig]
        int GetPropertyDescriptionList(ref PROPERTYKEY keyType, ref Guid riid,
            [MarshalAs(UnmanagedType.Interface)] out object ppv);

        [PreserveSig]
        int Update(IntPtr pbc);

        [PreserveSig]
        int GetProperty(ref PROPERTYKEY key, IntPtr ppropvar);

        [PreserveSig]
        int GetCLSID(ref PROPERTYKEY key, out Guid pclsid);

        [PreserveSig]
        int GetFileTime(ref PROPERTYKEY key, out FILETIME pft);

        [PreserveSig]
        int GetInt32(ref PROPERTYKEY key, out int pi);

        [PreserveSig]
        int GetString(ref PROPERTYKEY key, out IntPtr ppsz);

        [PreserveSig]
        int GetUInt32(ref PROPERTYKEY key, out uint pui);

        [PreserveSig]
        int GetUInt64(ref PROPERTYKEY key, out ulong pull);

        [PreserveSig]
        int GetBool(ref PROPERTYKEY key, out int pf);
    }


    [ComImport]
    [Guid("70629033-e363-4a28-a567-0db78006e6d7")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IEnumShellItems {
        [PreserveSig]
        int Next(uint celt, [Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] IShellItem[] rgelt,
            out uint pceltFetched);

        [PreserveSig]
        int Skip(uint celt);

        [PreserveSig]
        int Reset();

        [PreserveSig]
        int Clone(out IEnumShellItems ppenum);
    }


    /// <summary>
    /// The copy engine of the shell. The only thing that can read the bytes
    /// of an entry inside an <c>ArchiveFolder</c>: <c>BHID_Stream</c>
    /// answers <c>E_NOINTERFACE</c> there, and so does <c>IDataObject</c>.
    /// </summary>
    [ComImport]
    [Guid("947aab5f-0a5c-4c13-b4d6-4bf7836fc9f8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IFileOperation {
        [PreserveSig]
        int Advise(IFileOperationProgressSink pfops, out uint pdwCookie);

        [PreserveSig]
        int Unadvise(uint dwCookie);

        [PreserveSig]
        int SetOperationFlags(uint dwOperationFlags);

        [PreserveSig]
        int SetProgressMessage([MarshalAs(UnmanagedType.LPWStr)] string pszMessage);

        [PreserveSig]
        int SetProgressDialog(IntPtr popd);

        [PreserveSig]
        int SetProperties(IntPtr pproparray);

        [PreserveSig]
        int SetOwnerWindow(IntPtr hwndOwner);

        [PreserveSig]
        int ApplyPropertiesToItem(IShellItem psiItem);

        [PreserveSig]
        int ApplyPropertiesToItems(IntPtr punkItems);

        [PreserveSig]
        int RenameItem(IShellItem psiItem, [MarshalAs(UnmanagedType.LPWStr)] string pszNewName,
            IFileOperationProgressSink? pfopsItem);

        [PreserveSig]
        int RenameItems(IntPtr pUnkItems, [MarshalAs(UnmanagedType.LPWStr)] string pszNewName);

        [PreserveSig]
        int MoveItem(IShellItem psiItem, IShellItem psiDestinationFolder,
            [MarshalAs(UnmanagedType.LPWStr)] string? pszNewName, IFileOperationProgressSink? pfopsItem);

        [PreserveSig]
        int MoveItems(IntPtr punkItems, IShellItem psiDestinationFolder);

        [PreserveSig]
        int CopyItem(IShellItem psiItem, IShellItem psiDestinationFolder,
            [MarshalAs(UnmanagedType.LPWStr)] string? pszCopyName, IFileOperationProgressSink? pfopsItem);

        [PreserveSig]
        int CopyItems(IntPtr punkItems, IShellItem psiDestinationFolder);

        [PreserveSig]
        int DeleteItem(IShellItem psiItem, IFileOperationProgressSink? pfopsItem);

        [PreserveSig]
        int DeleteItems(IntPtr punkItems);

        [PreserveSig]
        int NewItem(IShellItem psiDestinationFolder, uint dwFileAttributes,
            [MarshalAs(UnmanagedType.LPWStr)] string pszName,
            [MarshalAs(UnmanagedType.LPWStr)] string? pszTemplateName, IFileOperationProgressSink? pfopsItem);

        [PreserveSig]
        int PerformOperations();

        [PreserveSig]
        int GetAnyOperationsAborted([MarshalAs(UnmanagedType.Bool)] out bool pfAnyOperationsAborted);
    }


    /// <summary>
    /// Callbacks the copy engine makes while it works. Wander implements it
    /// for the two things the engine offers no other way to get: per-item
    /// progress, and cancellation - a failure returned from
    /// <c>PreCopyItem</c> is how an operation already under way is stopped.
    /// </summary>
    [ComImport]
    [Guid("04b0f1a7-9490-44bc-96e1-4296a31252e2")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IFileOperationProgressSink {
        [PreserveSig]
        int StartOperations();

        [PreserveSig]
        int FinishOperations(int hrResult);

        [PreserveSig]
        int PreRenameItem(uint dwFlags, IShellItem psiItem, [MarshalAs(UnmanagedType.LPWStr)] string? pszNewName);

        [PreserveSig]
        int PostRenameItem(uint dwFlags, IShellItem psiItem, [MarshalAs(UnmanagedType.LPWStr)] string? pszNewName,
            int hrRename, IShellItem? psiNewlyCreated);

        [PreserveSig]
        int PreMoveItem(uint dwFlags, IShellItem psiItem, IShellItem? psiDestinationFolder,
            [MarshalAs(UnmanagedType.LPWStr)] string? pszNewName);

        [PreserveSig]
        int PostMoveItem(uint dwFlags, IShellItem psiItem, IShellItem? psiDestinationFolder,
            [MarshalAs(UnmanagedType.LPWStr)] string? pszNewName, int hrMove, IShellItem? psiNewlyCreated);

        [PreserveSig]
        int PreCopyItem(uint dwFlags, IShellItem psiItem, IShellItem? psiDestinationFolder,
            [MarshalAs(UnmanagedType.LPWStr)] string? pszNewName);

        [PreserveSig]
        int PostCopyItem(uint dwFlags, IShellItem psiItem, IShellItem? psiDestinationFolder,
            [MarshalAs(UnmanagedType.LPWStr)] string? pszNewName, int hrCopy, IShellItem? psiNewlyCreated);

        [PreserveSig]
        int PreDeleteItem(uint dwFlags, IShellItem psiItem);

        [PreserveSig]
        int PostDeleteItem(uint dwFlags, IShellItem psiItem, int hrDelete, IShellItem? psiNewlyCreated);

        [PreserveSig]
        int PreNewItem(uint dwFlags, IShellItem psiDestinationFolder,
            [MarshalAs(UnmanagedType.LPWStr)] string? pszNewName);

        [PreserveSig]
        int PostNewItem(uint dwFlags, IShellItem psiDestinationFolder,
            [MarshalAs(UnmanagedType.LPWStr)] string? pszNewName,
            [MarshalAs(UnmanagedType.LPWStr)] string? pszTemplateName, uint dwFileAttributes,
            int hrNew, IShellItem? psiNewItem);

        [PreserveSig]
        int UpdateProgress(uint iWorkTotal, uint iWorkSoFar);

        [PreserveSig]
        int ResetTimer();

        [PreserveSig]
        int PauseTimer();

        [PreserveSig]
        int ResumeTimer();
    }


    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    internal static extern int SHCreateItemFromParsingName(
        [MarshalAs(UnmanagedType.LPWStr)] string pszPath, IntPtr pbc, ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out object ppv);

    [DllImport("ole32.dll")]
    internal static extern int CoCreateInstance(ref Guid rclsid, IntPtr pUnkOuter, uint dwClsContext,
        ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object ppv);
}
