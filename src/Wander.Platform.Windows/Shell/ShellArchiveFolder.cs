using System.Runtime.InteropServices;
using Microsoft.Win32;
using Wander.Core.FileSystem;
using Wander.Core.Logging;
using Wander.Core.Shell;
using static Wander.Platform.Windows.Shell.ShellItemInterop;

namespace Wander.Platform.Windows.Shell;

/// <summary>
/// Archives browsed as folders, through the same shell handlers Explorer
/// uses: <c>CompressedFolder</c> for zip, <c>ArchiveFolder</c> (libarchive)
/// for 7z / rar / tar.gz and a dozen more, <c>CABFolder</c> for cab. Wander
/// writes no archive reader of its own - which formats open is whatever the
/// machine says, and an association handed to 7-Zip or WinRAR closes the
/// door here exactly as it does in Explorer.
///
/// <para>
/// Everything goes through <c>IShellItem</c>, never <c>Shell.Application</c>:
/// the latter answers 0 for <c>FolderItem.Size</c> and 1899 for
/// <c>ModifyDate</c> on <c>ArchiveFolder</c> entries. The Recycle Bin is
/// listed the same way - see <see cref="ShellRecycleBinFolder"/>.
/// </para>
///
/// <para>
/// Reading the bytes is the shell copy engine's job and nobody else's:
/// <c>BHID_Stream</c> and <c>IDataObject</c> both answer
/// <c>E_NOINTERFACE</c> for <c>ArchiveFolder</c>, while
/// <c>IFileOperation::CopyItem</c> unpacks everything - see
/// <see cref="CopyOut"/>.
/// </para>
/// </summary>
public sealed class ShellArchiveFolder {
    // Failures that name no cause: the run was stopped, by us or by a
    // handler that had a question and no window to ask it in (Failure).
    private const int ErrorCancelled = unchecked((int)0x800704C7);
    private const int CopyEngineUserCancelled = unchecked((int)0x80270000);
    private const int CopyEngineCancelled = unchecked((int)0x80270001);


    /// <summary>
    /// The shell handlers that make an archive browsable. An extension
    /// whose ProgID is one of these opens as a folder; anything else -
    /// including a .7z whose association went to 7-Zip - does not.
    /// </summary>
    private static readonly HashSet<string> _folderProgIds = new(StringComparer.OrdinalIgnoreCase) {
        "CompressedFolder",
        "ArchiveFolder",
        "CABFolder",
    };

    /// <summary>
    /// Extensions worth asking the registry about: everything zipfldr.dll
    /// claims on a stock Windows 11, plus cab. The answer for each is the
    /// ProgID, so a machine where 7-Zip owns .7z simply drops it from the
    /// set; an exotic type nobody registers is absent either way.
    /// </summary>
    private static readonly string[] _candidateExtensions = {
        ".zip", ".7z", ".rar", ".tar", ".gz", ".tgz", ".bz2", ".tbz2",
        ".xz", ".txz", ".zst", ".tzst", ".cpio", ".xar", ".uu", ".mtree",
        ".nupkg", ".cab",
    };

    private readonly ILogger _log;

    private HashSet<string>? _extensions;


    public ShellArchiveFolder(ILogger log) {
        _log = log;
    }


    /// <summary>
    /// Extensions the shell of this machine opens as folders, lower case
    /// and dotted. Read once, lazily: the answer only changes when the user
    /// installs an archiver, and that is a restart's worth of news.
    /// </summary>
    public IReadOnlySet<string> Extensions => _extensions ??= ReadExtensions();


    public ArchivePath? Parse(string path) {
        return ArchivePath.Parse(path, Extensions);
    }

    /// <summary>
    /// True when the shell can list this path as a folder. Hits the disk
    /// (the archive has to be opened), so callers keep it off the UI thread.
    /// </summary>
    public bool CanNavigate(string path) {
        var item = CreateItem(path);
        if (item is null) {
            return false;
        }

        try {
            return item.GetAttributes(SFGAO_FOLDER, out uint attributes) >= 0
                && (attributes & SFGAO_FOLDER) != 0;
        } finally {
            Release(item);
        }
    }

    /// <summary>
    /// One level of an archive, as filesystem-shaped rows. Folders inside
    /// carry no size (the shell has none to give) and, in a zip, no date
    /// either - both come back null / <see cref="DateTime.MinValue"/>
    /// rather than as a zero the user would read as a fact.
    /// </summary>
    /// <exception cref="IOException">
    /// The archive could not be opened or listed - a broken file, an
    /// unreadable disk. An archive that is merely empty (or fully
    /// encrypted, which looks the same from here) returns no rows instead.
    /// </exception>
    public IReadOnlyList<FileSystemEntry> Enumerate(string path, CancellationToken ct = default) {
        var folder = CreateItem(path);
        if (folder is null) {
            throw new IOException($"Cannot open '{path}' as a folder.");
        }

        try {
            var bhid = BHID_EnumItems;
            var iid = IID_IEnumShellItems;
            int hr = folder.BindToHandler(IntPtr.Zero, ref bhid, ref iid, out object raw);
            if (hr < 0 || raw is not IEnumShellItems items) {
                // An empty archive answers this way as often as a broken
                // one does; the caller separates the two by looking at the
                // file on disk, and neither is an error here.
                _log.Info($"Archive enumerate: no enumerator for {path} (hr=0x{hr:X8})");

                return Array.Empty<FileSystemEntry>();
            }

            try {
                return ReadAll(items, ct);
            } finally {
                Release(items);
            }
        } finally {
            Release(folder);
        }
    }

    /// <summary>
    /// Unpacks <paramref name="items"/> into <paramref name="targetFolder"/>
    /// with the shell's copy engine. Every item is queued onto one
    /// <c>IFileOperation</c> and the whole batch is performed in a single
    /// pass - a solid 7z is decompressed once that way instead of once per
    /// entry.
    ///
    /// <para>
    /// Overwriting is not attempted: the caller has already cleared the
    /// target or passed a new name. Cancellation is the sink's doing -
    /// the engine asks before each item and takes a failure as "stop".
    /// </para>
    /// </summary>
    public void CopyOut(
        IReadOnlyList<CopyOutItem> items, string targetFolder,
        IProgress<string>? progress, CancellationToken ct,
        IProgress<CopyOutWork>? work = null) {

        if (items.Count == 0) {
            return;
        }

        var operation = CreateFileOperation();
        var target = CreateItem(targetFolder)
            ?? throw new IOException($"Cannot open target folder '{targetFolder}'.");
        var sink = new CopySink(progress, work, ct);
        var sources = new List<IShellItem>(items.Count);

        try {
            int hr = operation.SetOperationFlags(FOF_NO_UI);
            Check(hr, "SetOperationFlags");

            foreach (var item in items) {
                var source = CreateItem(item.Path)
                    ?? throw new FileNotFoundException($"Not found inside the archive: {item.Path}", item.Path);
                sources.Add(source);
                Check(operation.CopyItem(source, target, item.NewName, sink), "CopyItem");
            }

            ct.ThrowIfCancellationRequested();
            hr = operation.PerformOperations();

            // A cancelled run fails or reports aborted too, and the caller is
            // the one that knows which of the two happened.
            ct.ThrowIfCancellationRequested();
            bool aborted = operation.GetAnyOperationsAborted(out bool any) >= 0 && any;
            if (hr < 0 || aborted) {
                throw Failure(hr, sink.FirstFailure);
            }
        } finally {
            foreach (var source in sources) {
                Release(source);
            }
            Release(target);
            Release(operation);
        }
    }


    // --- Enumeration ----------------------------------------------------

    private IReadOnlyList<FileSystemEntry> ReadAll(IEnumShellItems items, CancellationToken ct) {
        var result = new List<FileSystemEntry>();
        var batch = new IShellItem[1];

        while (items.Next(1, batch, out uint fetched) == 0 && fetched == 1) {
            var item = batch[0];
            batch[0] = null!;
            try {
                ct.ThrowIfCancellationRequested();
                if (BuildEntry(item) is { } entry) {
                    result.Add(entry);
                }
            } catch (Exception ex) when (ex is not OperationCanceledException) {
                _log.Warn($"Archive enumerate: skipped an entry ({ex.Message})");
            } finally {
                Release(item);
            }
        }

        return result;
    }

    private static FileSystemEntry? BuildEntry(IShellItem item) {
        string fullPath = DisplayName(item, SIGDN_DESKTOPABSOLUTEPARSING);
        if (fullPath.Length == 0) {
            return null;
        }

        string name = DisplayName(item, SIGDN_PARENTRELATIVEPARSING);
        if (name.Length == 0) {
            name = Path.GetFileName(fullPath);
        }

        bool isFolder = item.GetAttributes(SFGAO_FOLDER, out uint attributes) >= 0
            && (attributes & SFGAO_FOLDER) != 0;

        long? size = null;
        var modified = DateTime.MinValue;
        if (item is IShellItem2 item2) {
            var sizeKey = PKEY_Size;
            if (item2.GetUInt64(ref sizeKey, out ulong bytes) >= 0) {
                size = (long)bytes;
            }

            var dateKey = PKEY_DateModified;
            if (item2.GetFileTime(ref dateKey, out FILETIME time) >= 0 && time.Ticks > 0) {
                modified = DateTime.FromFileTimeUtc(time.Ticks);
            }
        }

        return new FileSystemEntry(
            Name: name,
            FullPath: fullPath,
            Kind: isFolder ? EntryKind.Directory : EntryKind.File,
            // A folder inside an archive has no size of its own; the shell
            // says so with an error, and null is how the list draws it.
            Size: isFolder ? null : size,
            ModifiedUtc: modified,
            IsHidden: false,
            IsReadOnly: false,
            IsSystem: false,
            LinksToDirectory: false);
    }

    private static string DisplayName(IShellItem item, uint form) {
        if (item.GetDisplayName(form, out IntPtr buffer) < 0 || buffer == IntPtr.Zero) {
            return "";
        }

        try {
            return Marshal.PtrToStringUni(buffer) ?? "";
        } finally {
            Marshal.FreeCoTaskMem(buffer);
        }
    }


    // --- COM plumbing ---------------------------------------------------

    private static IShellItem? CreateItem(string path) {
        var iid = IID_IShellItem;
        int hr = SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out object item);

        return hr >= 0 ? item as IShellItem : null;
    }

    private static IFileOperation CreateFileOperation() {
        var clsid = CLSID_FileOperation;
        var iid = IID_IFileOperation;
        int hr = CoCreateInstance(ref clsid, IntPtr.Zero, CLSCTX_INPROC_SERVER, ref iid, out object operation);
        if (hr < 0 || operation is not IFileOperation typed) {
            throw new IOException($"Cannot create the shell copy engine (hr=0x{hr:X8}).");
        }

        return typed;
    }

    private static void Check(int hr, string what) {
        if (hr < 0) {
            throw new IOException($"{what} failed (hr=0x{hr:X8}).", Marshal.GetExceptionForHR(hr));
        }
    }

    /// <summary>
    /// What a run that failed or was aborted is reported as. An item's own
    /// answer is asked first, then the run's: one that names a cause - the
    /// disk is full, the medium is read-only - is told in the system's
    /// words. A run that stopped with nothing to name is what a zip with a
    /// password does with no window to ask in.
    /// </summary>
    private static IOException Failure(int runResult, int itemResult) {
        int cause = Names(itemResult) ? itemResult : Names(runResult) ? runResult : 0;
        if (cause == 0) {
            return new ArchiveLockedException();
        }

        return new IOException(Marshal.GetExceptionForHR(cause)?.Message ?? $"0x{cause:X8}", cause);
    }

    /// <summary>A failure that says what went wrong - not a bare "failed" or "cancelled".</summary>
    private static bool Names(int hr) {
        return hr < 0 && hr is not (E_FAIL or E_ABORT or ErrorCancelled or CopyEngineUserCancelled or CopyEngineCancelled);
    }

    private static void Release(object? comObject) {
        if (comObject is not null && Marshal.IsComObject(comObject)) {
            Marshal.ReleaseComObject(comObject);
        }
    }


    // --- Which extensions open as folders --------------------------------

    /// <summary>
    /// Asks the registry which of the candidates the shell still owns.
    /// The user's own choice wins over the class default, the same way
    /// Explorer resolves it - installing 7-Zip and letting it take .7z has
    /// to close the folder view for .7z here too, or Wander would be
    /// offering something the rest of the system does not.
    /// </summary>
    private HashSet<string> ReadExtensions() {
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string extension in _candidateExtensions) {
            try {
                if (_folderProgIds.Contains(ProgIdOf(extension) ?? "")) {
                    found.Add(extension);
                }
            } catch (Exception ex) {
                _log.Warn($"Archive extensions: {extension} not readable ({ex.Message})");
            }
        }

        if (found.Count == 0) {
            // Nothing readable at all (a locked-down machine, a broken
            // hive). Zip is built into every Windows since XP and is the
            // one association it is safe to assume.
            _log.Warn("Archive extensions: registry said nothing, falling back to .zip");
            found.Add(".zip");
        }
        _log.Info($"Archives open as folders: {string.Join(" ", found.Order())}");

        return found;
    }

    private static string? ProgIdOf(string extension) {
        using var choice = Registry.CurrentUser.OpenSubKey(
            $@"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\{extension}\UserChoice");
        if (choice?.GetValue("ProgId") is string chosen && chosen.Length > 0) {
            return chosen;
        }

        using var classes = Registry.ClassesRoot.OpenSubKey(extension);

        return classes?.GetValue(null) as string;
    }


    /// <summary>
    /// Per-item progress and the only way to stop a run: the engine calls
    /// <see cref="PreCopyItem"/> before each item and treats a failure as
    /// "do not do this one", walking the rest of the queue the same way.
    /// Everything else is the empty implementation the interface demands.
    /// </summary>
    private sealed class CopySink : IFileOperationProgressSink {
        private readonly IProgress<string>? _progress;
        private readonly IProgress<CopyOutWork>? _work;
        private readonly CancellationToken _ct;


        public CopySink(IProgress<string>? progress, IProgress<CopyOutWork>? work, CancellationToken ct) {
            _progress = progress;
            _work = work;
            _ct = ct;
        }


        /// <summary>The first item's failure the engine reported, 0 while there is none - see <see cref="Failure"/>.</summary>
        public int FirstFailure { get; private set; }


        public int PreCopyItem(uint dwFlags, IShellItem psiItem, IShellItem? psiDestinationFolder, string? pszNewName) {
            return _ct.IsCancellationRequested ? E_ABORT : 0;
        }

        public int PostCopyItem(uint dwFlags, IShellItem psiItem, IShellItem? psiDestinationFolder,
            string? pszNewName, int hrCopy, IShellItem? psiNewlyCreated) {
            if (hrCopy >= 0) {
                _progress?.Report(DisplayName(psiItem, SIGDN_DESKTOPABSOLUTEPARSING));
            } else if (FirstFailure == 0) {
                FirstFailure = hrCopy;
            }

            return 0;
        }

        public int StartOperations() => 0;
        public int FinishOperations(int hrResult) => 0;
        public int PreRenameItem(uint dwFlags, IShellItem psiItem, string? pszNewName) => 0;
        public int PostRenameItem(uint dwFlags, IShellItem psiItem, string? pszNewName, int hrRename, IShellItem? psiNewlyCreated) => 0;
        public int PreMoveItem(uint dwFlags, IShellItem psiItem, IShellItem? psiDestinationFolder, string? pszNewName) => 0;
        public int PostMoveItem(uint dwFlags, IShellItem psiItem, IShellItem? psiDestinationFolder, string? pszNewName, int hrMove, IShellItem? psiNewlyCreated) => 0;
        public int PreDeleteItem(uint dwFlags, IShellItem psiItem) => 0;
        public int PostDeleteItem(uint dwFlags, IShellItem psiItem, int hrDelete, IShellItem? psiNewlyCreated) => 0;
        public int PreNewItem(uint dwFlags, IShellItem psiDestinationFolder, string? pszNewName) => 0;
        public int PostNewItem(uint dwFlags, IShellItem psiDestinationFolder, string? pszNewName, string? pszTemplateName, uint dwFileAttributes, int hrNew, IShellItem? psiNewItem) => 0;
        /// <summary>
        /// The engine's own measure of how far along the whole batch is.
        /// Whatever a unit of it is - the documentation does not say, and it
        /// is not bytes - the ratio is honest, and it is the only thing that
        /// moves while a single large entry is being decompressed.
        /// </summary>
        public int UpdateProgress(uint iWorkTotal, uint iWorkSoFar) {
            _work?.Report(new CopyOutWork(iWorkSoFar, iWorkTotal));

            return 0;
        }
        public int ResetTimer() => 0;
        public int PauseTimer() => 0;
        public int ResumeTimer() => 0;
    }
}
