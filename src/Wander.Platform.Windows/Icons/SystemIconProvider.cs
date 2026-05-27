using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using Wander.Core.Icons;

namespace Wander.Platform.Windows.Icons;

/// <summary>
/// Provides system icons via the Win32 shell:
///  - Small/Normal via SHGetFileInfo (16/32 px).
///  - Large via the system image list with SHIL_JUMBO (up to 256 px).
/// PNG bytes are cached per (extension|directory, size).
/// </summary>
public sealed class SystemIconProvider : IIconProvider {
    private readonly Dictionary<string, byte[]> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();


    public byte[]? GetIcon(string path, IconSize size) {
        if (string.IsNullOrEmpty(path)) {
            return null;
        }

        string key = BuildCacheKey(path, size);

        lock (_lock) {
            if (_cache.TryGetValue(key, out byte[]? cached)) {
                return cached;
            }
        }

        try {
            byte[]? bytes = LoadIcon(path, size);
            if (bytes is not null) {
                lock (_lock) {
                    _cache[key] = bytes;
                }
            }
            return bytes;
        } catch {
            return null;
        }
    }


    private static string BuildCacheKey(string path, IconSize size) {
        if (System.IO.Directory.Exists(path)) {
            // Most directories share an icon; but custom folder icons (desktop.ini)
            // would need per-path caching. For now we cache per-path to be safe.
            return $"dir|{path}|{size}";
        }

        string ext = Path.GetExtension(path);

        // Shortcuts (.lnk) display the target's icon with a link overlay — so two
        // different .lnk files have completely different icons. Cache by full path.
        if (ext.Equals(".lnk", StringComparison.OrdinalIgnoreCase)) {
            return $"lnk|{path}|{size}";
        }

        if (string.IsNullOrEmpty(ext)) {
            return $"file|noext|{size}";
        }
        return $"ext|{ext.ToLowerInvariant()}|{size}";
    }

    private static byte[]? LoadIcon(string path, IconSize size) {
        return size switch {
            IconSize.Large => LoadJumboIcon(path),
            _ => LoadShellIcon(path, size),
        };
    }


    // ------------------------------------------------------------------
    // Small / Normal — straight SHGetFileInfo.
    // ------------------------------------------------------------------

    private static byte[]? LoadShellIcon(string path, IconSize size) {
        uint flags = SHGFI_ICON | (size == IconSize.Small ? SHGFI_SMALLICON : SHGFI_LARGEICON);

        bool exists = File.Exists(path) || System.IO.Directory.Exists(path);
        if (!exists) {
            flags |= SHGFI_USEFILEATTRIBUTES;
        }

        // .lnk shortcuts: ask the shell to compose the link-overlay arrow
        // (small ↗ in the corner). Without this flag SHGetFileInfo returns
        // the bare target icon — same as Wander showed before.
        if (path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) {
            flags |= SHGFI_LINKOVERLAY;
        }

        var info = new SHFILEINFO();
        IntPtr result = SHGetFileInfo(
            path,
            FILE_ATTRIBUTE_NORMAL,
            ref info,
            (uint)Marshal.SizeOf<SHFILEINFO>(),
            flags);

        if (result == IntPtr.Zero || info.hIcon == IntPtr.Zero) {
            return null;
        }

        try {
            return HIconToPng(info.hIcon);
        } finally {
            DestroyIcon(info.hIcon);
        }
    }


    // ------------------------------------------------------------------
    // Large — system image list with SHIL_JUMBO (256x256).
    // ------------------------------------------------------------------

    private static byte[]? LoadJumboIcon(string path) {
        // First step: get the icon INDEX via SHGetFileInfo with SHGFI_SYSICONINDEX.
        uint flags = SHGFI_SYSICONINDEX;

        bool exists = File.Exists(path) || System.IO.Directory.Exists(path);
        if (!exists) {
            flags |= SHGFI_USEFILEATTRIBUTES;
        }

        var info = new SHFILEINFO();
        IntPtr result = SHGetFileInfo(
            path,
            FILE_ATTRIBUTE_NORMAL,
            ref info,
            (uint)Marshal.SizeOf<SHFILEINFO>(),
            flags);

        if (result == IntPtr.Zero) {
            return LoadShellIcon(path, IconSize.Normal);
        }

        // Second step: get the image list at jumbo size, then extract that index.
        var iidImageList = new Guid("46EB5926-582E-4017-9FDF-E8998DAA0950");
        int hr = SHGetImageList(SHIL_JUMBO, ref iidImageList, out IImageList list);
        if (hr != 0 || list is null) {
            return LoadShellIcon(path, IconSize.Normal);
        }

        IntPtr hIcon = IntPtr.Zero;
        try {
            int rc = list.GetIcon(info.iIcon, ILD_TRANSPARENT, ref hIcon);
            if (rc != 0 || hIcon == IntPtr.Zero) {
                return LoadShellIcon(path, IconSize.Normal);
            }
            return HIconToPng(hIcon);
        } finally {
            if (hIcon != IntPtr.Zero) {
                DestroyIcon(hIcon);
            }
            Marshal.ReleaseComObject(list);
        }
    }


    private static byte[] HIconToPng(IntPtr hIcon) {
        using var icon = Icon.FromHandle(hIcon);
        using var bmp = icon.ToBitmap();
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }


    // ------------------------------------------------------------------
    // P/Invoke — Win32 shell.
    // ------------------------------------------------------------------

    private const uint SHGFI_ICON = 0x000000100;
    private const uint SHGFI_LARGEICON = 0x000000000;
    private const uint SHGFI_SMALLICON = 0x000000001;
    private const uint SHGFI_SYSICONINDEX = 0x000004000;
    private const uint SHGFI_LINKOVERLAY = 0x000008000;
    private const uint SHGFI_USEFILEATTRIBUTES = 0x000000010;
    private const uint FILE_ATTRIBUTE_NORMAL = 0x00000080;

    private const int SHIL_JUMBO = 0x4;     // 256x256
    private const int ILD_TRANSPARENT = 0x1;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct SHFILEINFO {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SHGetFileInfo(
        string pszPath,
        uint dwFileAttributes,
        ref SHFILEINFO psfi,
        uint cbSizeFileInfo,
        uint uFlags);

    [DllImport("shell32.dll", EntryPoint = "#727")]
    private static extern int SHGetImageList(int iImageList, ref Guid riid, out IImageList ppv);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    // IImageList COM interface — only need GetIcon (11th method in vtable).
    [ComImport]
    [Guid("46EB5926-582E-4017-9FDF-E8998DAA0950")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IImageList {
        [PreserveSig] int Add(IntPtr hbmImage, IntPtr hbmMask, ref int pi);
        [PreserveSig] int ReplaceIcon(int i, IntPtr hicon, ref int pi);
        [PreserveSig] int SetOverlayImage(int iImage, int iOverlay);
        [PreserveSig] int Replace(int i, IntPtr hbmImage, IntPtr hbmMask);
        [PreserveSig] int AddMasked(IntPtr hbmImage, int crMask, ref int pi);
        [PreserveSig] int Draw(IntPtr pimldp);
        [PreserveSig] int Remove(int i);
        [PreserveSig] int GetIcon(int i, int flags, ref IntPtr picon);
    }
}
