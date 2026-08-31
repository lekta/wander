using Windows.Data.Pdf;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Wander.Platform.Windows.Icons;

/// <summary>
/// Renders the first page of a PDF, so a shelf of PDFs shows its covers.
///
/// <para>
/// Not what the shell would do. A PDF only gets a thumbnail if something
/// installed registered a thumbnail provider for it, and plenty of readers
/// (SumatraPDF among them) take over the file association without
/// registering one — after which Explorer, and anything asking the shell
/// the way Wander does, shows the same grey icon for every document on the
/// disk. Rendering the page ourselves gives the same answer on every
/// machine.
/// </para>
///
/// <para>
/// <c>Windows.Data.Pdf</c> is part of Windows, not a package: no dependency
/// is added by this, only a minimum Windows version on the two projects
/// that target Windows at all. Measured on a 2.5 MB, 355-page book: 14 ms
/// to open, 13 ms to render the page at 256 px.
/// </para>
/// </summary>
internal static class PdfPageImage {
    /// <summary>
    /// Documents past this size are left to the shell. Rendering does not
    /// read the whole file, but a listing full of huge scans should not
    /// find out the hard way.
    /// </summary>
    private const long MaxFileSize = 256L * 1024 * 1024;


    public static bool Supports(string path) {
        return path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);
    }


    /// <summary>
    /// The first page as PNG bytes, <paramref name="height"/> pixels tall,
    /// or null when the file is not a readable PDF — encrypted, damaged, or
    /// simply not one. The caller then falls back to the shell.
    ///
    /// <para>
    /// Synchronous on purpose: every caller is already on the background
    /// thumbnail thread, and handing an async signature up through
    /// <c>IIconProvider</c> would turn the whole icon pipeline inside out
    /// for one format. Blocking on a WinRT operation is safe here — it
    /// completes on the thread pool, and no synchronization context is
    /// captured.
    /// </para>
    /// </summary>
    public static byte[]? RenderFirstPage(string path, int height) {
        try {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > MaxFileSize) {
                return null;
            }

            var file = StorageFile.GetFileFromPathAsync(path).AsTask().GetAwaiter().GetResult();
            var document = PdfDocument.LoadFromFileAsync(file).AsTask().GetAwaiter().GetResult();
            if (document.PageCount == 0) {
                return null;
            }

            using var page = document.GetPage(0);
            using var stream = new InMemoryRandomAccessStream();
            page.RenderToStreamAsync(stream, new PdfPageRenderOptions {
                DestinationHeight = (uint)Math.Max(1, height),
            }).AsTask().GetAwaiter().GetResult();

            return ReadAll(stream);
        } catch {
            // Deliberately everything. Two families of failure meet here and
            // neither is worth a tile: the document (locked with a password,
            // truncated, not actually a PDF — which arrives as a COMException
            // carrying an HRESULT), and the platform. Windows.Data.Pdf has
            // shipped since Windows 8.1, but a build old enough not to
            // project it would throw on the type rather than on the call, and
            // an old machine should lose the cover, not the icon.
            return null;
        }
    }


    private static byte[] ReadAll(IRandomAccessStream stream) {
        var bytes = new byte[stream.Size];
        using var reader = new DataReader(stream.GetInputStreamAt(0));
        reader.LoadAsync((uint)stream.Size).AsTask().GetAwaiter().GetResult();
        reader.ReadBytes(bytes);

        return bytes;
    }
}
