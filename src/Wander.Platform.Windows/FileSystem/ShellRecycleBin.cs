using System.IO.Enumeration;
using System.Runtime.InteropServices;
using Wander.Core.FileSystem;
using Wander.Core.Logging;
using Wander.Core.Shell;
using Wander.Platform.Windows.Shell;
using static Wander.Platform.Windows.Shell.ShellItemInterop;

namespace Wander.Platform.Windows.FileSystem;

/// <summary>
/// IRecycleBin on the shell's copy engine, <c>IFileOperation</c>, both ways.
/// It used to be <c>SHFileOperation</c> out and a <c>Shell.Application</c>
/// verb back; what the move bought (stand of 2026-09-21):
///
/// <list type="bullet">
///   <item>The engine says <b>beforehand</b> whether it is going to recycle
///   or destroy (<c>PreDeleteItem</c> flags), so a delete that asked for the
///   bin is refused instead of done for good. <c>SHFileOperation</c> gave a
///   file on an over-long path no such chance: rc 0, nothing in the bin.</item>
///   <item>The bin hands back the item it made (<c>PostDeleteItem</c>), and
///   its id list goes into the handle: Restore is one move, 10-20 ms, with
///   no walk of the bin (0.35 s each on 1433 items), no verb found by its
///   localized name, no deletion date parsed out of a localized string.</item>
///   <item>The same path deleted twice restores the right one each time.</item>
/// </list>
///
/// <para>
/// A batch goes in one engine run (<see cref="SendMany"/>); Restore is
/// still a run an item - an undo step is one item, and 10 ms of it is not
/// what an undo of thousands waits for yet (PLAN, block 0).
/// </para>
///
/// <para>
/// Thin on purpose: a held item is reported "in use" (<see cref="FileInUse"/>)
/// and not waited for here. The wait is the operation's, in Core
/// (<c>BusyGate</c>), one budget for the whole batch (PLAN, block 0, step 4).
/// </para>
/// </summary>
public sealed class ShellRecycleBin : IRecycleBin {
    /// <summary>What the engine answers for an item that is in use, besides its own two codes.</summary>
    private const int HResultSharingViolation = unchecked((int)0x80070020);

    /// <summary>
    /// MAX_PATH less its terminator. Longer than this the bin does not
    /// take; 349 is measured, the exact edge is not - it is the documented
    /// one.
    /// </summary>
    private const int MaxRecyclablePath = 259;

    private readonly ILogger _logger;


    public ShellRecycleBin(ILogger logger) {
        _logger = logger;
    }


    // --- Send ----------------------------------------------------------

    public RecycleHandle Send(string path) {
        var result = SendMany(new[] { path })[0];
        if (result.Error is not null) {
            throw result.Error;
        }

        return result.Handle ?? throw new OperationCanceledException($"Recycle of '{path}' was aborted.");
    }

    /// <summary>
    /// The whole batch in one engine run: 3 ms an item against 11 for a run
    /// each (stand of 2026-09-21, 1000 files in 3.1 s), every item still
    /// with its own result and its own bin id. Without
    /// <c>FOFX_EARLYFAILURE</c> a busy file - or a folder with one inside,
    /// which then stays whole - is skipped in milliseconds and the rest go
    /// on; with it the engine took a second over the same answer and stopped.
    /// The busy one comes back as a <see cref="FileInUse"/> failure.
    ///
    /// <para>
    /// A failure returned from <c>PreDeleteItem</c> ends the run for every
    /// item after it, touched or not. That is the cancellation - and also
    /// what a refusal costs, so after a refused item the rest are simply
    /// run again. Callbacks come in the order the items were queued.
    /// </para>
    /// </summary>
    public IReadOnlyList<RecycleResult> SendMany(
        IReadOnlyList<string> paths, Action<string>? onItemDone = null, CancellationToken ct = default) {
        var results = new RecycleResult?[paths.Count];
        var pending = new List<int>(paths.Count);

        for (int i = 0; i < paths.Count; i++) {
            string path = paths[i];
            if (!File.Exists(path) && !Directory.Exists(path)) {
                results[i] = Failed(path, new FileNotFoundException("Cannot recycle non-existent path", path));
            } else if (TooLongForBin(path, ct) is { } tooLong) {
                // Ours, before the engine sees the item: a folder that is
                // short itself and long inside passes the engine's own test,
                // and then the shell asks its question on screen, "Yes" by
                // default.
                _logger.Warn($"Recycle refused, nothing deleted: {tooLong.Length} characters is more than the bin takes - {tooLong}");
                results[i] = Failed(path, new RecycleUnavailableException(path, RecycleUnavailableReason.PathTooLong));
            } else {
                pending.Add(i);
                continue;
            }
            onItemDone?.Invoke(path);
        }

        RunAll(paths, pending, results, onItemDone, ct);

        // Whatever is still empty was never reached: the batch was cancelled.
        return results.Select((r, i) => r ?? new RecycleResult(paths[i], null, null)).ToList();
    }


    /// <summary>
    /// Runs <paramref name="indices"/> until each has an answer or the batch
    /// is cancelled: a refused item ends its run, and the items after it go
    /// into the next one.
    /// </summary>
    private void RunAll(
        IReadOnlyList<string> paths, IReadOnlyList<int> indices, RecycleResult?[] results,
        Action<string>? onItemDone, CancellationToken ct) {
        var left = indices.ToList();
        while (left.Count > 0 && !ct.IsCancellationRequested) {
            var batch = left.Select(i => paths[i]).ToList();
            DateTime when = DateTime.UtcNow;
            var outcome = OwnApartment.Run("Wander recycle", () => Recycle(batch, onItemDone, ct));

            var unreached = new List<int>();
            for (int n = 0; n < left.Count; n++) {
                int i = left[n];
                var item = outcome.Items[n];
                if (item is null) {
                    unreached.Add(i);
                } else if (item.Refused) {
                    _logger.Warn($"Recycle refused, nothing deleted: the shell was going to destroy {paths[i]} rather than recycle it");
                    results[i] = Failed(paths[i], new RecycleUnavailableException(paths[i], RecycleUnavailableReason.NoBin));
                } else if (IsBusy(item.Result)) {
                    results[i] = Failed(paths[i], FileInUse.Error(paths[i]));
                } else if (item.Result < 0) {
                    results[i] = Failed(paths[i], new IOException($"Recycle failed (hr=0x{item.Result:X8}) for {paths[i]}"));
                } else {
                    if (item.BinItemId is null) {
                        _logger.Warn($"Recycle: the bin named no item for {paths[i]} - Ctrl+Z will not find it");
                    }
                    _logger.Info($"Recycled: {paths[i]}");
                    results[i] = new RecycleResult(paths[i], new RecycleHandle(paths[i], when, item.BinItemId, item.BinFilePath), null);
                }
            }

            if (ct.IsCancellationRequested) {
                return;
            }
            // A run that answered for nothing would come back the same way
            // for ever.
            if (unreached.Count == left.Count) {
                foreach (int i in left) {
                    results[i] = Failed(paths[i], new IOException($"Recycle failed (hr=0x{outcome.RunResult:X8}) for {paths[i]}"));
                    onItemDone?.Invoke(paths[i]);
                }

                return;
            }
            left = unreached;
        }
    }

    /// <summary>
    /// One engine run. The outcome has a slot for every path, empty where
    /// the engine never got to the item - the run was cancelled, or ended by
    /// a refusal before it.
    /// </summary>
    private static RunOutcome Recycle(IReadOnlyList<string> paths, Action<string>? onItemDone, CancellationToken ct) {
        IFileOperation? operation = null;
        var items = new List<IShellItem>(paths.Count);
        try {
            operation = CreateFileOperation();
            Check(
                operation.SetOperationFlags(FOF_NO_UI | FOF_ALLOWUNDO | FOF_WANTNUKEWARNING | FOFX_RECYCLEONDELETE),
                "SetOperationFlags");

            var outcomes = new ItemOutcome?[paths.Count];
            var queued = new List<int>(paths.Count);
            var sink = new Sink(n => onItemDone?.Invoke(paths[queued[n]]), ct);
            for (int i = 0; i < paths.Count; i++) {
                var item = CreateItem(paths[i]);
                if (item is null) {
                    // Gone between the caller's look and ours.
                    outcomes[i] = new ItemOutcome(E_FAIL, Refused: false, null, null);
                    onItemDone?.Invoke(paths[i]);
                    continue;
                }
                items.Add(item);
                queued.Add(i);
                Check(operation.DeleteItem(item, sink), "DeleteItem");
            }

            int run = queued.Count > 0 ? operation.PerformOperations() : 0;
            // Callbacks come in the order queued (stand of 2026-09-21).
            for (int n = 0; n < sink.Deleted.Count; n++) {
                outcomes[queued[n]] = sink.Deleted[n];
            }

            return new RunOutcome(outcomes, run);
        } finally {
            foreach (var item in items) {
                Release(item);
            }
            Release(operation);
        }
    }

    /// <summary>
    /// The first path the bin would not take: the item's own, or one inside
    /// the folder. A junction is not walked into - the bin takes the link,
    /// not what it points at. A cancel stops the walk: the batch it belongs
    /// to recycles nothing more once cancelled (<see cref="RunAll"/>), and a
    /// folder of a few hundred thousand files on a share took minutes that
    /// Cancel could not shorten.
    /// </summary>
    private static string? TooLongForBin(string path, CancellationToken ct) {
        if (path.Length > MaxRecyclablePath) {
            return path;
        }
        if (!Directory.Exists(path) || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) {
            return null;
        }

        var options = new EnumerationOptions {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = 0,
        };
        var tooLong = new FileSystemEnumerable<string>(
            path, (ref System.IO.Enumeration.FileSystemEntry entry) => entry.ToFullPath(), options) {
            ShouldIncludePredicate = (ref System.IO.Enumeration.FileSystemEntry entry) =>
                entry.Directory.Length + 1 + entry.FileName.Length > MaxRecyclablePath,
            ShouldRecursePredicate = (ref System.IO.Enumeration.FileSystemEntry entry) =>
                (entry.Attributes & FileAttributes.ReparsePoint) == 0 && !ct.IsCancellationRequested,
        };

        return tooLong.FirstOrDefault();
    }

    private static bool IsBusy(int hr) {
        return hr is COPYENGINE_E_SHARING_VIOLATION_SRC or COPYENGINE_E_SHARING_VIOLATION_DEST
            or HResultSharingViolation;
    }

    private static RecycleResult Failed(string path, Exception error) {
        return new RecycleResult(path, null, error);
    }


    /// <param name="Result">The engine's answer for the item; negative is a failure.</param>
    /// <param name="Refused">Stopped by our own sink: the engine was about to destroy it.</param>
    private sealed record ItemOutcome(int Result, bool Refused, string? BinItemId, string? BinFilePath);

    private sealed record RunOutcome(IReadOnlyList<ItemOutcome?> Items, int RunResult);


    // --- Restore -------------------------------------------------------

    public void Restore(RecycleHandle handle) {
        // Never onto something that took the name in the meantime: the
        // engine under FOF_NO_UI would answer its own "replace?" with yes.
        // The name is settled here, by the one rule for a taken name; the
        // engine's rename-on-collision stays on for the race after this line.
        string target = UniqueNames.Resolve(
            handle.OriginalPath,
            taken => File.Exists(taken) || Directory.Exists(taken),
            folder => Directory.Exists(folder)
                ? Directory.EnumerateFileSystemEntries(folder).Select(inner => Path.GetFileName(inner))
                : Enumerable.Empty<string>());

        OwnApartment.Run("Wander recycle restore", () => {
            MoveOut(handle, target);

            return true;
        });

        _logger.Info(string.Equals(target, handle.OriginalPath, StringComparison.OrdinalIgnoreCase)
            ? $"Restored from recycle: {handle.OriginalPath}"
            : $"Restored from recycle: {handle.OriginalPath} as {Path.GetFileName(target)} - the name was taken");
    }


    private void MoveOut(RecycleHandle handle, string target) {
        IFileOperation? operation = null;
        IShellItem? item = null;
        IShellItem? folder = null;
        try {
            item = handle.BinItemId is { } id ? ItemFromId(id)
                : handle.BinFilePath is { } file ? FindByBinFile(file)
                : null;
            if (item is null) {
                throw new IOException($"Item not found in recycle bin: {handle.OriginalPath}");
            }
            string binFile = DisplayName(item, SIGDN_FILESYSPATH);

            // The folder it was deleted from may have gone since; Explorer
            // brings it back too.
            string targetFolder = Path.GetDirectoryName(target)
                ?? throw new IOException($"'{target}' has no folder to restore into.");
            Directory.CreateDirectory(targetFolder);
            folder = CreateItem(targetFolder) ?? throw new IOException($"Cannot open target folder '{targetFolder}'.");

            operation = CreateFileOperation();
            Check(operation.SetOperationFlags(FOF_NO_UI | FOF_RENAMEONCOLLISION | FOFX_EARLYFAILURE), "SetOperationFlags");
            var sink = new Sink();
            Check(operation.MoveItem(item, folder, Path.GetFileName(target), sink), "MoveItem");
            int run = operation.PerformOperations();
            int result = run < 0 ? run : sink.MoveResult ?? run;
            if (result < 0) {
                throw new IOException($"Restore failed (hr=0x{result:X8}) for {handle.OriginalPath}");
            }

            RemoveIndexFile(binFile);
        } finally {
            Release(folder);
            Release(item);
            Release(operation);
        }
    }

    /// <summary>
    /// A move out of the bin takes the <c>$R</c> file and leaves its
    /// <c>$I</c> record behind (stand of 2026-09-21). The bin does not list
    /// such an orphan, and emptying the bin sweeps it - but that is no
    /// reason to leave one per restored file.
    /// </summary>
    private void RemoveIndexFile(string binFile) {
        string name = Path.GetFileName(binFile);
        if (!name.StartsWith("$R", StringComparison.OrdinalIgnoreCase) || File.Exists(binFile) || Directory.Exists(binFile)) {
            return;
        }

        string index = Path.Combine(Path.GetDirectoryName(binFile) ?? "", "$I" + name[2..]);
        try {
            File.Delete(index);
        } catch (Exception ex) {
            _logger.Info($"Restore: the bin's record {index} stays until the bin is emptied ({ex.Message})");
        }
    }

    private static IShellItem? ItemFromId(string binItemId) {
        byte[] bytes = Convert.FromBase64String(binItemId);
        IntPtr pidl = Marshal.AllocCoTaskMem(bytes.Length);
        try {
            Marshal.Copy(bytes, 0, pidl, bytes.Length);
            var iid = IID_IShellItem;

            return SHCreateItemFromIDList(pidl, ref iid, out object item) >= 0 ? item as IShellItem : null;
        } finally {
            Marshal.FreeCoTaskMem(pidl);
        }
    }

    /// <summary>
    /// The bin item kept in <paramref name="binFile"/> - a row of the bin's
    /// listing knows nothing else about itself. A walk, stopped at the match:
    /// 78 ms to find one among 1437, 270 ms for all of them.
    /// </summary>
    private static IShellItem? FindByBinFile(string binFile) {
        IShellItem? folder = null;
        IEnumShellItems? items = null;
        try {
            folder = CreateItem(ShellPaths.RecycleBin);
            if (folder is null) {
                return null;
            }

            var bhid = BHID_EnumItems;
            var iidEnum = IID_IEnumShellItems;
            if (folder.BindToHandler(IntPtr.Zero, ref bhid, ref iidEnum, out object rawItems) < 0) {
                return null;
            }
            items = rawItems as IEnumShellItems;
            if (items is null) {
                return null;
            }

            var batch = new IShellItem[1];
            while (items.Next(1, batch, out uint fetched) == 0 && fetched == 1) {
                var item = batch[0];
                batch[0] = null!;
                if (string.Equals(DisplayName(item, SIGDN_FILESYSPATH), binFile, StringComparison.OrdinalIgnoreCase)) {
                    return item;
                }
                Release(item);
            }

            return null;
        } finally {
            Release(items);
            Release(folder);
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

    private static void Check(int hr, string what) {
        if (hr < 0) {
            throw new IOException($"{what} failed (hr=0x{hr:X8}).", Marshal.GetExceptionForHR(hr));
        }
    }

    private static void Release(object? comObject) {
        if (comObject is not null && Marshal.IsComObject(comObject)) {
            Marshal.ReleaseComObject(comObject);
        }
    }


    /// <summary>
    /// What the engine tells about the one item of a run. The delete half is
    /// also the guard: an item the engine is about to destroy rather than
    /// recycle is refused in <see cref="PreDeleteItem"/>, which ends the run
    /// with the item untouched. Callbacks come through COM - nothing is
    /// allowed to throw out of them.
    /// </summary>
    private sealed class Sink : IFileOperationProgressSink {
        private readonly Action<int>? _onDeleted;
        private readonly CancellationToken _ct;

        private bool _refused;
        private bool _cancelled;


        /// <param name="onDeleted">Called with the item's place in the queue once its answer is final.</param>
        /// <param name="ct">Looked at before every item.</param>
        public Sink(Action<int>? onDeleted = null, CancellationToken ct = default) {
            _onDeleted = onDeleted;
            _ct = ct;
        }


        /// <summary>The items the engine answered for, in the order queued. The one a cancel landed on is not among them.</summary>
        public List<ItemOutcome> Deleted { get; } = new();

        public int? MoveResult { get; private set; }


        public int PreDeleteItem(uint dwFlags, IShellItem psiItem) {
            if (_ct.IsCancellationRequested) {
                _cancelled = true;

                return E_ABORT;
            }
            if ((dwFlags & TSF_DELETE_RECYCLE_IF_POSSIBLE) != 0) {
                return 0;
            }
            _refused = true;

            return E_FAIL;
        }

        public int PostDeleteItem(uint dwFlags, IShellItem psiItem, int hrDelete, IShellItem? psiNewlyCreated) {
            if (_cancelled) {
                return 0;
            }

            string? binItemId = null;
            string? binFilePath = null;
            try {
                if (hrDelete >= 0 && psiNewlyCreated is not null) {
                    binFilePath = DisplayName(psiNewlyCreated, SIGDN_FILESYSPATH);
                    if (SHGetIDListFromObject(psiNewlyCreated, out IntPtr pidl) >= 0 && pidl != IntPtr.Zero) {
                        try {
                            byte[] bytes = new byte[ILGetSize(pidl)];
                            Marshal.Copy(pidl, bytes, 0, bytes.Length);
                            binItemId = Convert.ToBase64String(bytes);
                        } finally {
                            Marshal.FreeCoTaskMem(pidl);
                        }
                    }
                }
            } catch (Exception) {
                // The item is in the bin either way; SendMany reports a missing id.
            }

            Deleted.Add(new ItemOutcome(hrDelete, _refused, binItemId, binFilePath));
            try {
                _onDeleted?.Invoke(Deleted.Count - 1);
            } catch (Exception) {
                // A progress report is not worth a failed delete.
            }

            return 0;
        }

        public int PostMoveItem(uint dwFlags, IShellItem psiItem, IShellItem? psiDestinationFolder,
            string? pszNewName, int hrMove, IShellItem? psiNewlyCreated) {
            MoveResult = hrMove;

            return 0;
        }

        public int StartOperations() => 0;
        public int FinishOperations(int hrResult) => 0;
        public int PreRenameItem(uint dwFlags, IShellItem psiItem, string? pszNewName) => 0;
        public int PostRenameItem(uint dwFlags, IShellItem psiItem, string? pszNewName, int hrRename, IShellItem? psiNewlyCreated) => 0;
        public int PreMoveItem(uint dwFlags, IShellItem psiItem, IShellItem? psiDestinationFolder, string? pszNewName) => 0;
        public int PreCopyItem(uint dwFlags, IShellItem psiItem, IShellItem? psiDestinationFolder, string? pszNewName) => 0;
        public int PostCopyItem(uint dwFlags, IShellItem psiItem, IShellItem? psiDestinationFolder, string? pszNewName, int hrCopy, IShellItem? psiNewlyCreated) => 0;
        public int PreNewItem(uint dwFlags, IShellItem psiDestinationFolder, string? pszNewName) => 0;
        public int PostNewItem(uint dwFlags, IShellItem psiDestinationFolder, string? pszNewName, string? pszTemplateName, uint dwFileAttributes, int hrNew, IShellItem? psiNewItem) => 0;
        public int UpdateProgress(uint iWorkTotal, uint iWorkSoFar) => 0;
        public int ResetTimer() => 0;
        public int PauseTimer() => 0;
        public int ResumeTimer() => 0;
    }
}
