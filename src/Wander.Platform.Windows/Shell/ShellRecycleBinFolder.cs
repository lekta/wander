using System.Runtime.InteropServices;
using Wander.Core.FileSystem;
using Wander.Core.Logging;
using Wander.Core.Shell;
using static Wander.Platform.Windows.Shell.ShellItemInterop;

namespace Wander.Platform.Windows.Shell;

/// <summary>
/// The Recycle Bin listed as a folder, through <c>IShellItem2</c> - the same
/// documented surface <see cref="ShellArchiveFolder"/> browses archives with.
/// It used to go through <c>Shell.Application</c>; what the move bought
/// (measured 2026-09-21 on 1433 items, same rows, same warm speed):
///
/// <list type="bullet">
///   <item>The listing comes item by item, so it stops when the person has
///   left - <c>Folder.Items()</c> was one call, five seconds of it when
///   the bin is cold, and nothing could interrupt it.</item>
///   <item>The deletion time is a FILETIME, not a localized string with
///   direction marks to strip and a culture to guess.</item>
///   <item>A deleted folder carries its real size instead of 0.</item>
/// </list>
///
/// <para>
/// Restoring is <c>ShellRecycleBin.Restore</c>'s; a row from here is found
/// again by its <c>$R</c> file, which is what <c>FullPath</c> carries.
/// </para>
/// </summary>
internal sealed class ShellRecycleBinFolder {
    private readonly ILogger _log;


    public ShellRecycleBinFolder(ILogger log) {
        _log = log;
    }


    /// <summary>
    /// Every item in the bin, newest deletion first - what Explorer shows,
    /// and what somebody looking for "the file I just deleted by mistake"
    /// expects. <c>FullPath</c> is the <c>$R</c> file on disk: enough for
    /// the icon provider and for the launcher. Recycled folders are not
    /// walked into.
    /// </summary>
    /// <exception cref="OperationCanceledException">The person moved on before the listing ended.</exception>
    public IReadOnlyList<FileSystemEntry> Enumerate(CancellationToken ct) {
        return OwnApartment.Run("Wander recycle bin listing", () => ReadAll(ct));
    }


    private IReadOnlyList<FileSystemEntry> ReadAll(CancellationToken ct) {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var iid = IID_IShellItem;
        int hr = SHCreateItemFromParsingName(ShellPaths.RecycleBin, IntPtr.Zero, ref iid, out object rawFolder);
        if (hr < 0 || rawFolder is not IShellItem folder) {
            _log.Warn($"Recycle bin: cannot open the folder (hr=0x{hr:X8}) - it will appear empty.");

            return Array.Empty<FileSystemEntry>();
        }

        IEnumShellItems? items = null;
        var result = new List<FileSystemEntry>();
        try {
            var bhid = BHID_EnumItems;
            var iidEnum = IID_IEnumShellItems;
            hr = folder.BindToHandler(IntPtr.Zero, ref bhid, ref iidEnum, out object rawItems);
            items = rawItems as IEnumShellItems;
            if (hr < 0 || items is null) {
                // An empty bin answers this way on some builds; not an error.
                _log.Info($"Recycle bin: no enumerator (hr=0x{hr:X8})");

                return result;
            }

            long openedMs = watch.ElapsedMilliseconds;
            long firstRowMs = -1;
            var batch = new IShellItem[1];

            while (items.Next(1, batch, out uint fetched) == 0 && fetched == 1) {
                var item = batch[0];
                batch[0] = null!;
                try {
                    if (firstRowMs < 0) {
                        firstRowMs = watch.ElapsedMilliseconds - openedMs;
                    }
                    if (ct.IsCancellationRequested) {
                        _log.Info($"Recycle bin: listing abandoned at {result.Count} rows, {watch.ElapsedMilliseconds} ms");
                        ct.ThrowIfCancellationRequested();
                    }
                    if (BuildEntry(item) is { } entry) {
                        result.Add(entry);
                    }
                } catch (Exception ex) when (ex is not OperationCanceledException) {
                    _log.Warn($"Recycle bin: skipped an item ({ex.Message})");
                } finally {
                    Release(item);
                }
            }

            // The control line for the bin's slowness (PLAN AD2): where a
            // cold listing spends its seconds - before the first row, or
            // spread over all of them.
            _log.Info(
                $"Recycle bin: opened in {openedMs} ms, first row after {Math.Max(firstRowMs, 0)} ms, " +
                $"{result.Count} rows in {watch.ElapsedMilliseconds - openedMs} ms");
        } finally {
            Release(items);
            Release(folder);
        }

        result.Sort((a, b) => b.ModifiedUtc.CompareTo(a.ModifiedUtc));

        return result;
    }

    private static FileSystemEntry? BuildEntry(IShellItem item) {
        string fullPath = DisplayName(item, SIGDN_FILESYSPATH);
        if (fullPath.Length == 0) {
            return null;
        }

        // The display name of a recycled item is the path it was deleted
        // from; its last part is the name Explorer shows in the bin. Only
        // the fallback: a display name hides the .lnk of a shortcut always
        // and every extension when Explorer is told to, and the name here
        // has to be the one on disk - an ordinary folder lists it that way,
        // and Restore matches on it.
        string original = DisplayName(item, SIGDN_NORMALDISPLAY);
        string name = Path.GetFileName(original);

        bool isFolder = item.GetAttributes(SFGAO_FOLDER, out uint attributes) >= 0
            && (attributes & SFGAO_FOLDER) != 0;

        long? size = null;
        var deletedUtc = DateTime.MinValue;
        string? originalLocation = Path.GetDirectoryName(original);
        if (item is IShellItem2 item2) {
            if (StringProperty(item2, PKEY_FileName) is { Length: > 0 } fileName) {
                name = fileName;
            }

            var sizeKey = PKEY_Size;
            if (item2.GetUInt64(ref sizeKey, out ulong bytes) >= 0) {
                size = (long)bytes;
            }

            // ModifiedUtc carries the deletion time for bin entries: the
            // list sorts on it and Restore matches on it. The file's own
            // date only when the bin's column is missing.
            var deletedKey = PKEY_Recycle_DateDeleted;
            var modifiedKey = PKEY_DateModified;
            if (item2.GetFileTime(ref deletedKey, out FILETIME deleted) >= 0 && deleted.Ticks > 0) {
                deletedUtc = DateTime.FromFileTimeUtc(deleted.Ticks);
            } else if (item2.GetFileTime(ref modifiedKey, out FILETIME modified) >= 0 && modified.Ticks > 0) {
                deletedUtc = DateTime.FromFileTimeUtc(modified.Ticks);
            }

            originalLocation = StringProperty(item2, PKEY_Recycle_DeletedFrom) ?? originalLocation;
        }

        if (name.Length == 0) {
            return null;
        }

        return new FileSystemEntry(
            Name: name,
            FullPath: fullPath,
            Kind: isFolder ? EntryKind.Directory : EntryKind.File,
            Size: size,
            ModifiedUtc: deletedUtc,
            IsHidden: false,
            IsReadOnly: false,
            IsSystem: false,
            // A recycled .lnk-to-folder does not masquerade as a folder:
            // Wander does not navigate into bin entries either way.
            LinksToDirectory: false,
            OriginalLocation: string.IsNullOrEmpty(originalLocation) ? null : originalLocation);
    }

    private static string? StringProperty(IShellItem2 item, PROPERTYKEY key) {
        if (item.GetString(ref key, out IntPtr buffer) < 0 || buffer == IntPtr.Zero) {
            return null;
        }

        try {
            return Marshal.PtrToStringUni(buffer);
        } finally {
            Marshal.FreeCoTaskMem(buffer);
        }
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

    private static void Release(object? comObject) {
        if (comObject is not null && Marshal.IsComObject(comObject)) {
            Marshal.ReleaseComObject(comObject);
        }
    }
}
