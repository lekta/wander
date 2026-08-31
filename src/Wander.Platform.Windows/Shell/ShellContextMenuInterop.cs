using System.Runtime.InteropServices;

namespace Wander.Platform.Windows.Shell;

/// <summary>
/// Raw Win32 / COM surface used by <see cref="ShellContextMenu"/>. Kept in
/// its own file so the interesting logic next door reads as logic and not
/// as a wall of <c>DllImport</c>.
///
/// <para>
/// Interface member order is the vtable order — do not reorder or omit
/// members, even unused ones, or every call after the gap lands on the
/// wrong function pointer.
/// </para>
/// </summary>
internal static class ShellContextMenuInterop {
    internal static Guid IID_IShellFolder = new("000214E6-0000-0000-C000-000000000046");
    internal static Guid IID_IContextMenu = new("000214e4-0000-0000-c000-000000000046");


    // --- IContextMenu flags --------------------------------------------

    internal const uint CMF_NORMAL = 0x00000000;
    internal const uint CMF_EXPLORE = 0x00000004;
    internal const uint CMF_ITEMMENU = 0x00000080;
    internal const uint CMF_EXTENDEDVERBS = 0x00000100;

    /// <summary>Ask <c>GetCommandString</c> for the canonical (language-neutral) verb.</summary>
    internal const uint GCS_VERBW = 0x00000004;

    /// <summary>
    /// Ask for the item's help text — the sentence Explorer used to show in
    /// its status bar. Localised, unlike the verb, and worth exactly nothing
    /// for identity; it is the only place a handler ever says what its row
    /// actually does, which is what the settings table wants.
    /// </summary>
    internal const uint GCS_HELPTEXTW = 0x00000005;

    internal const uint CMIC_MASK_UNICODE = 0x00004000;

    internal const int SW_SHOWNORMAL = 1;

    internal const uint WM_INITMENUPOPUP = 0x0117;


    // --- Menu flags ------------------------------------------------------

    internal const uint MIIM_STATE = 0x00000001;
    internal const uint MIIM_ID = 0x00000002;
    internal const uint MIIM_SUBMENU = 0x00000004;
    internal const uint MIIM_BITMAP = 0x00000080;
    internal const uint MIIM_FTYPE = 0x00000100;

    internal const uint MFT_SEPARATOR = 0x00000800;
    internal const uint MFT_OWNERDRAW = 0x00000100;

    internal const uint MFS_GRAYED = 0x00000003;

    internal const uint MF_BYPOSITION = 0x00000400;


    [StructLayout(LayoutKind.Sequential)]
    internal struct MENUITEMINFO {
        public int cbSize;
        public uint fMask;
        public uint fType;
        public uint fState;
        public uint wID;
        public IntPtr hSubMenu;
        public IntPtr hbmpChecked;
        public IntPtr hbmpUnchecked;
        public IntPtr dwItemData;
        public IntPtr dwTypeData;
        public uint cch;
        public IntPtr hbmpItem;
    }


    /// <summary>
    /// The Unicode-capable invoke block. All string members are declared as
    /// <see cref="IntPtr"/> because <c>lpVerb</c> is not a string at all in
    /// our usage — it carries a command offset via <c>MAKEINTRESOURCE</c>,
    /// which the default string marshaller would happily dereference and
    /// crash on.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct CMINVOKECOMMANDINFOEX {
        public int cbSize;
        public uint fMask;
        public IntPtr hwnd;
        public IntPtr lpVerb;
        public IntPtr lpParameters;
        public IntPtr lpDirectory;
        public int nShow;
        public uint dwHotKey;
        public IntPtr hIcon;
        public IntPtr lpTitle;
        public IntPtr lpVerbW;
        public IntPtr lpParametersW;
        public IntPtr lpDirectoryW;
        public IntPtr lpTitleW;
        public POINT ptInvoke;
    }


    [StructLayout(LayoutKind.Sequential)]
    internal struct POINT {
        public int x;
        public int y;
    }


    [StructLayout(LayoutKind.Sequential)]
    internal struct BITMAP {
        public int bmType;
        public int bmWidth;
        public int bmHeight;
        public int bmWidthBytes;
        public ushort bmPlanes;
        public ushort bmBitsPixel;
        public IntPtr bmBits;
    }


    [StructLayout(LayoutKind.Sequential)]
    internal struct BITMAPINFOHEADER {
        public int biSize;
        public int biWidth;

        /// <summary>Negative for a top-down DIB — the one fact BITMAP alone cannot tell us.</summary>
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public int biCompression;
        public int biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public int biClrUsed;
        public int biClrImportant;
    }


    /// <summary>
    /// <c>GetObject</c> fills this only for DIB sections; a device-dependent
    /// bitmap fills just the leading <see cref="BITMAP"/> and reports the
    /// smaller size. That difference is how we tell the two apart.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct DIBSECTION {
        public BITMAP dsBm;
        public BITMAPINFOHEADER dsBmih;
        public uint dsBitfield0;
        public uint dsBitfield1;
        public uint dsBitfield2;
        public IntPtr dshSection;
        public uint dsOffset;
    }


    [ComImport]
    [Guid("000214E6-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IShellFolder {
        [PreserveSig]
        int ParseDisplayName(IntPtr hwnd, IntPtr pbc, [MarshalAs(UnmanagedType.LPWStr)] string pszDisplayName,
            ref uint pchEaten, out IntPtr ppidl, ref uint pdwAttributes);

        [PreserveSig]
        int EnumObjects(IntPtr hwnd, int grfFlags, out IntPtr ppenumIDList);

        [PreserveSig]
        int BindToObject(IntPtr pidl, IntPtr pbc, ref Guid riid,
            [MarshalAs(UnmanagedType.Interface)] out object ppv);

        [PreserveSig]
        int BindToStorage(IntPtr pidl, IntPtr pbc, ref Guid riid,
            [MarshalAs(UnmanagedType.Interface)] out object ppv);

        [PreserveSig]
        int CompareIDs(IntPtr lParam, IntPtr pidl1, IntPtr pidl2);

        [PreserveSig]
        int CreateViewObject(IntPtr hwndOwner, ref Guid riid,
            [MarshalAs(UnmanagedType.Interface)] out object ppv);

        [PreserveSig]
        int GetAttributesOf(uint cidl, [In, MarshalAs(UnmanagedType.LPArray)] IntPtr[] apidl, ref uint rgfInOut);

        [PreserveSig]
        int GetUIObjectOf(IntPtr hwndOwner, uint cidl, [In, MarshalAs(UnmanagedType.LPArray)] IntPtr[] apidl,
            ref Guid riid, IntPtr rgfReserved, [MarshalAs(UnmanagedType.Interface)] out object ppv);

        [PreserveSig]
        int GetDisplayNameOf(IntPtr pidl, uint uFlags, IntPtr pName);

        [PreserveSig]
        int SetNameOf(IntPtr hwnd, IntPtr pidl, [MarshalAs(UnmanagedType.LPWStr)] string pszName,
            uint uFlags, out IntPtr ppidlOut);
    }


    [ComImport]
    [Guid("000214e4-0000-0000-c000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IContextMenu {
        [PreserveSig]
        int QueryContextMenu(IntPtr hmenu, uint indexMenu, int idCmdFirst, int idCmdLast, uint uFlags);

        [PreserveSig]
        int InvokeCommand(ref CMINVOKECOMMANDINFOEX pici);

        [PreserveSig]
        int GetCommandString(IntPtr idCmd, uint uType, IntPtr pReserved,
            [Out, MarshalAs(UnmanagedType.LPArray)] byte[] pszName, uint cchMax);
    }


    /// <summary>
    /// Handlers that build their submenus lazily populate them in response
    /// to <c>WM_INITMENUPOPUP</c>. Since Wander renders the menu itself
    /// instead of handing the <c>HMENU</c> to <c>TrackPopupMenu</c>, that
    /// message never arrives on its own — we forward it by hand before
    /// walking a submenu.
    /// </summary>
    [ComImport]
    [Guid("000214f4-0000-0000-c000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IContextMenu2 {
        [PreserveSig]
        int QueryContextMenu(IntPtr hmenu, uint indexMenu, int idCmdFirst, int idCmdLast, uint uFlags);

        [PreserveSig]
        int InvokeCommand(ref CMINVOKECOMMANDINFOEX pici);

        [PreserveSig]
        int GetCommandString(IntPtr idCmd, uint uType, IntPtr pReserved,
            [Out, MarshalAs(UnmanagedType.LPArray)] byte[] pszName, uint cchMax);

        [PreserveSig]
        int HandleMenuMsg(uint uMsg, IntPtr wParam, IntPtr lParam);
    }


    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    internal static extern int SHParseDisplayName(string pszName, IntPtr pbc, out IntPtr ppidl,
        uint sfgaoIn, out uint psfgaoOut);

    [DllImport("shell32.dll")]
    internal static extern int SHBindToParent(IntPtr pidl, ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out object ppv, out IntPtr ppidlLast);

    [DllImport("shell32.dll")]
    internal static extern int SHBindToObject(IntPtr psf, IntPtr pidl, IntPtr pbc, ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out object ppv);

    [DllImport("shell32.dll")]
    internal static extern IntPtr ILFindLastID(IntPtr pidl);

    [DllImport("ole32.dll")]
    internal static extern void CoTaskMemFree(IntPtr pv);

    [DllImport("user32.dll")]
    internal static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll")]
    internal static extern int GetMenuItemCount(IntPtr hMenu);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetMenuItemInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetMenuItemInfo(IntPtr hMenu, uint item,
        [MarshalAs(UnmanagedType.Bool)] bool fByPosition, ref MENUITEMINFO lpmii);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetMenuStringW")]
    internal static extern int GetMenuString(IntPtr hMenu, uint uIDItem,
        System.Text.StringBuilder? lpString, int cchMax, uint flags);

    [DllImport("user32.dll")]
    internal static extern IntPtr GetActiveWindow();

    [DllImport("gdi32.dll")]
    internal static extern int GetObject(IntPtr hgdiobj, int cbBuffer, ref DIBSECTION lpvObject);
}
