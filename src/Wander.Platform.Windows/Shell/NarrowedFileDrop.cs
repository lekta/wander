using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace Wander.Platform.Windows.Shell;

/// <summary>
/// The shell's data object for a selection, with a shorter answer to
/// <c>CF_HDROP</c>: the file list names only the files handed in here,
/// while the id list (<c>CFSTR_SHELLIDLIST</c>) and every other format
/// still carry the whole selection.
///
/// <para>
/// For a drag of files with their sidecars. A receiver that asks the
/// shell for items - Explorer, anything on
/// <c>SHCreateShellItemArrayFromDataObject</c> - reads the id list first
/// and gets the sidecars along with the files: on the stand of 2026-10-09
/// a drop through a folder's own <c>IDropTarget</c> landed both
/// <c>a.png</c> and <c>a.png.meta</c> while the file list named one. A
/// receiver that reads only file names - an image editor, a browser, a
/// chat - gets the files alone and never sees <c>icon.png.meta</c>, which
/// it could only refuse.
/// </para>
///
/// <para>
/// Everything but <c>GetData</c> of <c>CF_HDROP</c> is the wrapped
/// object's, <c>SetData</c> included: the drop effect Explorer reports
/// back goes where it always went. <c>QueryGetData</c> and the format
/// enumeration say <c>CF_HDROP</c> is there, and it is.
/// </para>
/// </summary>
[ComVisible(true)]
internal sealed class NarrowedFileDrop : IDataObject {
    private const short CF_HDROP = 15;

    /// <summary><c>DROPFILES</c>: <c>pFiles</c>, <c>pt</c>, <c>fNC</c>, <c>fWide</c>.</summary>
    private const int DropFilesHeader = 20;

    private const int DropFilesWideOffset = 16;
    private const uint GMEM_MOVEABLE = 0x0002;
    private const uint GMEM_ZEROINIT = 0x0040;

    private readonly IDataObject _inner;
    private readonly IReadOnlyList<string> _files;


    public NarrowedFileDrop(IDataObject inner, IReadOnlyList<string> files) {
        _inner = inner;
        _files = files;
    }


    public void GetData(ref FORMATETC format, out STGMEDIUM medium) {
        if (format.cfFormat == CF_HDROP && (format.tymed & TYMED.TYMED_HGLOBAL) != 0) {
            medium = new STGMEDIUM {
                tymed = TYMED.TYMED_HGLOBAL,
                unionmember = BuildDropFiles(_files),
                pUnkForRelease = null,
            };

            return;
        }

        _inner.GetData(ref format, out medium);
    }

    public void GetDataHere(ref FORMATETC format, ref STGMEDIUM medium) {
        _inner.GetDataHere(ref format, ref medium);
    }

    public int QueryGetData(ref FORMATETC format) {
        return _inner.QueryGetData(ref format);
    }

    public int GetCanonicalFormatEtc(ref FORMATETC formatIn, out FORMATETC formatOut) {
        return _inner.GetCanonicalFormatEtc(ref formatIn, out formatOut);
    }

    public void SetData(ref FORMATETC formatIn, ref STGMEDIUM medium, bool release) {
        _inner.SetData(ref formatIn, ref medium, release);
    }

    public IEnumFORMATETC EnumFormatEtc(DATADIR direction) {
        return _inner.EnumFormatEtc(direction);
    }

    public int DAdvise(ref FORMATETC pFormatetc, ADVF advf, IAdviseSink adviseSink, out int connection) {
        return _inner.DAdvise(ref pFormatetc, advf, adviseSink, out connection);
    }

    public void DUnadvise(int connection) {
        _inner.DUnadvise(connection);
    }

    public int EnumDAdvise(out IEnumSTATDATA? enumAdvise) {
        return _inner.EnumDAdvise(out enumAdvise);
    }


    /// <summary>
    /// A <c>DROPFILES</c> header and the paths as one double-null-terminated
    /// wide block - the layout <c>WindowsClipboard.BuildDropFiles</c>
    /// writes, not shared with it because FileSystem already depends on
    /// Shell. The receiver frees the handle (<c>ReleaseStgMedium</c>).
    /// </summary>
    private static IntPtr BuildDropFiles(IReadOnlyList<string> files) {
        int chars = files.Sum(f => f.Length + 1) + 1;
        int bytes = DropFilesHeader + chars * sizeof(char);
        IntPtr mem = GlobalAlloc(GMEM_MOVEABLE | GMEM_ZEROINIT, (UIntPtr)bytes);
        if (mem == IntPtr.Zero) {
            throw new OutOfMemoryException();
        }

        IntPtr block = GlobalLock(mem);
        try {
            Marshal.WriteInt32(block, 0, DropFilesHeader);
            Marshal.WriteInt32(block, DropFilesWideOffset, 1);
            IntPtr cursor = block + DropFilesHeader;
            foreach (string file in files) {
                foreach (char c in file) {
                    Marshal.WriteInt16(cursor, (short)c);
                    cursor += sizeof(char);
                }
                cursor += sizeof(char);
            }
        } finally {
            GlobalUnlock(mem);
        }

        return mem;
    }


    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalLock(IntPtr hMem);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalUnlock(IntPtr hMem);
}
