using System.Runtime.InteropServices;
using Wander.Core.FileSystem;
using Wander.Core.Logging;
using ComTypes = System.Runtime.InteropServices.ComTypes;

namespace Wander.Platform.Windows.FileSystem;

/// <summary>
/// The Windows clipboard, as a file list.
///
/// <para>
/// Written against the plain Win32 clipboard API rather than WPF's
/// <c>System.Windows.Clipboard</c>, so this project stays free of a UI
/// dependency. The payload is exactly what Explorer puts there and expects
/// to find:
/// </para>
/// <list type="bullet">
///   <item><c>CF_HDROP</c> — a <c>DROPFILES</c> header followed by the
///   paths as double-null-terminated wide strings. This is the file list.</item>
///   <item><c>Preferred DropEffect</c> — a registered format holding one
///   DWORD, <c>DROPEFFECT_COPY</c> (1) or <c>DROPEFFECT_MOVE</c> (2). This
///   single value is the whole difference between a copy and a cut; without
///   it every receiver assumes a copy.</item>
/// </list>
///
/// <para>
/// Two Windows details this depends on. Memory handed to
/// <c>SetClipboardData</c> belongs to the system afterwards — which is what
/// makes a copy outlive Wander's own process, so it must not be freed on
/// the success path. And the clipboard opens <b>exclusively</b>: a
/// clipboard manager, Office or an RDP session can hold it for a few
/// milliseconds and every call fails meanwhile, hence
/// <see cref="OpenAttempts"/>.
/// </para>
/// </summary>
public sealed class WindowsClipboard : ISystemClipboard {
    /// <summary>
    /// How many times to retry <c>OpenClipboard</c>. Whoever else holds it
    /// normally holds it for microseconds; five tries spread over a tenth of
    /// a second cover that without the user noticing a stall.
    /// </summary>
    private const int OpenAttempts = 5;

    private const int OpenRetryDelayMs = 20;

    private readonly ILogger _log;


    public WindowsClipboard(ILogger log) {
        _log = log;
    }


    public string? LastError { get; private set; }


    public bool SetFiles(IReadOnlyList<string> paths, bool isCut) {
        if (paths.Count == 0) {
            return true;
        }

        return WithClipboard(nameof(SetFiles), () => {
            if (!EmptyClipboard()) {
                return false;
            }

            IntPtr drop = BuildDropFiles(paths);
            if (drop == IntPtr.Zero) {
                return false;
            }
            if (SetClipboardData(CF_HDROP, drop) == IntPtr.Zero) {
                GlobalFree(drop);
                return false;
            }

            // From here the file list is on the clipboard. The drop effect is
            // a separate format and a separate failure: losing it downgrades
            // a cut to a copy for other applications, which is worth logging
            // but not worth reporting the whole copy as failed — inside
            // Wander the cut still works.
            uint format = RegisterClipboardFormat(PreferredDropEffect);
            if (format == 0) {
                _log.Warn("[clipboard] drop effect format unavailable; other apps will see a copy");
                return true;
            }

            IntPtr effect = BuildDword(isCut ? DROPEFFECT_MOVE : DROPEFFECT_COPY);
            if (effect == IntPtr.Zero) {
                return true;
            }
            if (SetClipboardData(format, effect) == IntPtr.Zero) {
                GlobalFree(effect);
                _log.Warn("[clipboard] drop effect could not be written; other apps will see a copy");
            }

            return true;
        });
    }


    /// <summary>
    /// Hands a shell data object to the clipboard through OLE rather than
    /// through <c>SetClipboardData</c>: the object renders its formats on
    /// demand, and only <c>OleSetClipboard</c> keeps it alive to be asked.
    /// The formats inside are the shell's, so a receiver sees exactly what
    /// it would have seen from Explorer.
    ///
    /// <para>
    /// Not flushed (<c>OleFlushClipboard</c>): flushing renders every
    /// format there and then, which for an archive means unpacking the
    /// bytes into memory before anyone has asked for them. The cost of not
    /// flushing is that the copy dies with the process - which is what
    /// Explorer's own copy out of a zip does too.
    /// </para>
    /// </summary>
    public bool SetShellObject(object dataObject) {
        if (dataObject is not ComTypes.IDataObject data) {
            LastError = nameof(SetShellObject);
            _log.Warn("[clipboard] the object handed over is not an IDataObject");

            return false;
        }

        int hr = OleSetClipboard(data);
        // OLE has to be started on the thread that owns the clipboard, and
        // nothing else in Wander does it - the rest of this class is plain
        // Win32. One initialization on first use, never undone: the thread
        // is the UI thread and it lives as long as the process.
        if (hr == CO_E_NOTINITIALIZED) {
            OleInitialize(IntPtr.Zero);
            hr = OleSetClipboard(data);
        }

        if (hr < 0) {
            LastError = $"{nameof(SetShellObject)}: 0x{hr:X8}";
            _log.Warn($"[clipboard] OleSetClipboard failed (hr=0x{hr:X8})");

            return false;
        }

        LastError = null;

        return true;
    }


    public ClipboardFiles? GetFiles() {
        ClipboardFiles? result = null;

        bool ok = WithClipboard(nameof(GetFiles), () => {
            bool virtualFiles =
                IsClipboardFormatAvailable(RegisterClipboardFormat(FileGroupDescriptorW)) ||
                IsClipboardFormatAvailable(RegisterClipboardFormat(FileGroupDescriptorA));

            if (!IsClipboardFormatAvailable(CF_HDROP)) {
                // Text, a bitmap, or an attachment that only exists inside
                // the other application — either way, no paths to paste.
                result = new ClipboardFiles(Array.Empty<string>(), false, virtualFiles);
                return true;
            }

            var paths = ReadDropFiles();
            if (paths is null) {
                return false;
            }

            result = new ClipboardFiles(paths, ReadIsCut(), virtualFiles && paths.Count == 0);
            return true;
        });

        return ok ? result : null;
    }


    public void Clear() {
        WithClipboard(nameof(Clear), () => EmptyClipboard());
    }


    // ------------------------------------------------------------------
    // Clipboard session
    // ------------------------------------------------------------------

    /// <summary>
    /// Opens the clipboard, runs <paramref name="body"/>, and closes it
    /// again whatever happens. Every failure — a clipboard nobody would let
    /// go of, a bad allocation, an exception from the marshaller — comes
    /// back as false with <see cref="LastError"/> set, because a clipboard
    /// that cannot be reached is a lost copy and never a crash.
    /// </summary>
    private bool WithClipboard(string what, Func<bool> body) {
        // Opened with a real window rather than with NULL: a clipboard opened
        // by NULL gets a NULL owner from EmptyClipboard, and SetClipboardData
        // is documented to fail after that. GetActiveWindow answers with the
        // active window *of the calling thread*, so on the UI thread — where
        // every call here comes from — that is Wander's own window, and never
        // somebody else's.
        IntPtr owner = GetActiveWindow();

        for (int attempt = 0; attempt < OpenAttempts; attempt++) {
            if (!OpenClipboard(owner)) {
                Thread.Sleep(OpenRetryDelayMs);
                continue;
            }

            try {
                bool ok = body();
                LastError = ok ? null : Describe(what);
                return ok;
            } catch (Exception ex) {
                LastError = ex.Message;
                _log.Warn($"[clipboard] {what} failed: {ex.Message}");
                return false;
            } finally {
                CloseClipboard();
            }
        }

        LastError = Describe(what);
        _log.Warn($"[clipboard] {what}: clipboard busy after {OpenAttempts} attempts");

        return false;
    }

    private static string Describe(string what) {
        int code = Marshal.GetLastWin32Error();

        return code == 0 ? what : $"{what}: 0x{code:X8}";
    }


    // ------------------------------------------------------------------
    // CF_HDROP
    // ------------------------------------------------------------------

    /// <summary>
    /// Builds the <c>CF_HDROP</c> payload: a <see cref="DROPFILES"/> header
    /// followed by the paths as one double-null-terminated wide string
    /// block. Caller holds the clipboard.
    /// </summary>
    private static IntPtr BuildDropFiles(IReadOnlyList<string> paths) {
        int header = Marshal.SizeOf<DROPFILES>();
        // Every path plus its own terminator, then one more for the list.
        int chars = paths.Sum(p => p.Length + 1) + 1;
        int bytes = header + (chars * sizeof(char));

        IntPtr mem = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)bytes);
        if (mem == IntPtr.Zero) {
            return IntPtr.Zero;
        }

        IntPtr block = GlobalLock(mem);
        if (block == IntPtr.Zero) {
            GlobalFree(mem);
            return IntPtr.Zero;
        }

        try {
            var drop = new DROPFILES {
                pFiles = (uint)header,
                pt = default,
                fNC = 0,
                fWide = 1,
            };
            Marshal.StructureToPtr(drop, block, fDeleteOld: false);

            IntPtr cursor = block + header;
            foreach (string path in paths) {
                foreach (char c in path) {
                    Marshal.WriteInt16(cursor, (short)c);
                    cursor += sizeof(char);
                }
                Marshal.WriteInt16(cursor, 0);
                cursor += sizeof(char);
            }
            Marshal.WriteInt16(cursor, 0);
        } finally {
            GlobalUnlock(mem);
        }

        return mem;
    }

    /// <summary>Caller holds the clipboard. Null means the handle was there but unreadable.</summary>
    private static IReadOnlyList<string>? ReadDropFiles() {
        IntPtr handle = GetClipboardData(CF_HDROP);
        if (handle == IntPtr.Zero) {
            return null;
        }

        uint count = DragQueryFile(handle, AllFiles, null, 0);
        var paths = new List<string>((int)count);
        for (uint i = 0; i < count; i++) {
            uint length = DragQueryFile(handle, i, null, 0);
            if (length == 0) {
                continue;
            }

            // DragQueryFile reports the length without the terminator and
            // wants room for it.
            var buffer = new char[length + 1];
            if (DragQueryFile(handle, i, buffer, (uint)buffer.Length) > 0) {
                paths.Add(new string(buffer, 0, (int)length));
            }
        }

        return paths;
    }

    /// <summary>
    /// Reads the preferred drop effect. Absent means copy — that is what
    /// every receiver assumes. Checked as a <b>bit</b> rather than compared
    /// whole: applications do write combinations such as COPY | LINK, and
    /// an equality test would read those as a copy-only when they are not.
    /// </summary>
    private static bool ReadIsCut() {
        uint format = RegisterClipboardFormat(PreferredDropEffect);
        if (format == 0 || !IsClipboardFormatAvailable(format)) {
            return false;
        }

        IntPtr handle = GetClipboardData(format);
        if (handle == IntPtr.Zero) {
            return false;
        }

        IntPtr block = GlobalLock(handle);
        if (block == IntPtr.Zero) {
            return false;
        }

        try {
            return (Marshal.ReadInt32(block) & DROPEFFECT_MOVE) != 0;
        } finally {
            GlobalUnlock(handle);
        }
    }

    private static IntPtr BuildDword(int value) {
        IntPtr mem = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)sizeof(int));
        if (mem == IntPtr.Zero) {
            return IntPtr.Zero;
        }

        IntPtr block = GlobalLock(mem);
        if (block == IntPtr.Zero) {
            GlobalFree(mem);
            return IntPtr.Zero;
        }

        try {
            Marshal.WriteInt32(block, value);
        } finally {
            GlobalUnlock(mem);
        }

        return mem;
    }


    // ------------------------------------------------------------------
    // Interop
    // ------------------------------------------------------------------

    private const uint CF_HDROP = 15;
    private const uint GMEM_MOVEABLE = 0x0002;
    private const int DROPEFFECT_COPY = 1;
    private const int DROPEFFECT_MOVE = 2;

    /// <summary>DragQueryFile's "how many files are in there" sentinel.</summary>
    private const uint AllFiles = 0xFFFFFFFF;

    /// <summary>OLE was never started on this thread; see <see cref="SetShellObject"/>.</summary>
    private const int CO_E_NOTINITIALIZED = unchecked((int)0x800401F0);

    private const string PreferredDropEffect = "Preferred DropEffect";
    private const string FileGroupDescriptorW = "FileGroupDescriptorW";
    private const string FileGroupDescriptorA = "FileGroupDescriptor";


    [StructLayout(LayoutKind.Sequential)]
    private struct POINT {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DROPFILES {
        public uint pFiles;
        public POINT pt;
        public int fNC;
        public int fWide;
    }


    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport("user32.dll")]
    private static extern IntPtr GetActiveWindow();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetClipboardData(uint uFormat);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsClipboardFormatAvailable(uint format);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern uint RegisterClipboardFormat(string lpszFormat);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint DragQueryFile(IntPtr hDrop, uint iFile, char[]? lpszFile, uint cch);

    [DllImport("ole32.dll")]
    private static extern int OleSetClipboard(ComTypes.IDataObject pDataObj);

    [DllImport("ole32.dll")]
    private static extern int OleInitialize(IntPtr pvReserved);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalFree(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalLock(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalUnlock(IntPtr hMem);
}
