using Wander.Core.FileSystem;
using Wander.Core.Localization;

namespace Wander.Core.Actions;

/// <summary>Which delete is asked about.</summary>
public enum DeleteQuestionKind {
    /// <summary>To the recycle bin; Ctrl+Z brings it back.</summary>
    Recycle,

    /// <summary>Past the bin (Shift+Delete): nothing brings it back.</summary>
    Permanent,

    /// <summary>The bin refused these during a recycle: past it now, or stop here.</summary>
    Refused,
}


/// <summary>What a row of the delete question stands for.</summary>
public enum DeleteRowKind {
    File,
    Folder,

    /// <summary>A file that goes with the row above it (an .xmp beside its RAW) - known by its path only.</summary>
    Companion,
}


/// <summary>
/// One row of the delete question. A file is known at once; a folder is
/// walked in the background and a companion looked at, and each fills in
/// when done.
/// </summary>
public sealed class DeleteRow {
    internal DeleteRow(FileSystemEntry entry) {
        Entry = entry;
        Name = entry.Name;
        FullPath = entry.FullPath;
        Kind = entry.Kind == EntryKind.Directory ? DeleteRowKind.Folder : DeleteRowKind.File;
    }

    internal DeleteRow(string companion) {
        Name = System.IO.Path.GetFileName(companion);
        FullPath = companion;
        Kind = DeleteRowKind.Companion;
    }


    public string Name { get; }

    public string FullPath { get; }

    public DeleteRowKind Kind { get; }

    /// <summary>The row as the listing had it; null for a companion.</summary>
    public FileSystemEntry? Entry { get; }

    public bool IsFolder => Kind == DeleteRowKind.Folder;

    public bool IsCompanion => Kind == DeleteRowKind.Companion;

    /// <summary>Marked read-only: the delete clears the mark first.</summary>
    public bool IsReadOnly => Entry is { IsReadOnly: true };

    /// <summary>What the folder holds, once walked; null while the walk is on, and always for anything else.</summary>
    public FolderStats? Inside { get; internal set; }

    /// <summary>A folder still being walked, a companion not looked at yet: a spinner where the numbers go.</summary>
    public bool IsCounting => Kind switch {
        DeleteRowKind.Folder => Inside is null,
        DeleteRowKind.Companion => !Weighed,
        _ => false,
    };

    /// <summary>
    /// The bytes the row stands for. Null where the column shows a dash - a
    /// folder with nothing in it or one that could not be read, a companion
    /// that was not there to look at - and while it is being counted.
    /// </summary>
    public long? Size => Kind switch {
        DeleteRowKind.File => Entry!.Size,
        DeleteRowKind.Companion => Weight,
        _ => Inside is { IsUnreadable: false } stats && (stats.Files > 0 || stats.Folders > 0) ? stats.TotalSize : null,
    };

    internal bool Weighed { get; set; }

    internal long? Weight { get; set; }

    /// <summary>What goes into the total: whatever is known so far.</summary>
    internal long Bytes => IsFolder ? Inside?.TotalSize ?? 0 : Size ?? 0;
}


/// <summary>
/// The question before a delete (PLAN 0.6.1, the confirmation as a table):
/// every item on a row of its own, what a folder holds and weighs counted
/// in the background, and the words above and below the rows. Facts in,
/// decisions and words out; the window lays them out and decides nothing
/// (CLAUDE.md: complexity goes where a test can reach it).
///
/// <para>
/// Folders come first, in the order they were given, then the files - the
/// way the list shows them; each item's companions follow it on rows of
/// their own (decision of 2026-11-05: a file apart, its tie visible). One
/// item is described in words - its name in the headline, its path and
/// what it holds below - and only its companions, if any, make a table.
/// The read-only items are asked about here too, not in a second question.
/// </para>
/// </summary>
public sealed class DeleteConfirmation {
    private readonly IFileSystem _fs;
    private readonly ITextSource? _text;
    private readonly List<DeleteRow> _rows = [];
    private readonly IReadOnlyList<DeleteRow> _tableRows;


    /// <param name="items">What the user asked to delete, without the companions.</param>
    /// <param name="kind">Which delete.</param>
    /// <param name="withCompanions">The companions setting: on, each item's companions go along.</param>
    /// <param name="fs">What the folders are walked and the companions looked at on.</param>
    /// <param name="text">The string table; null is the registered one.</param>
    public DeleteConfirmation(
        IReadOnlyList<FileSystemEntry> items, DeleteQuestionKind kind, bool withCompanions, IFileSystem fs, ITextSource? text = null) {
        _fs = fs;
        _text = text;
        Items = items;
        Kind = kind;
        foreach (var entry in items.Where(e => e.Kind == EntryKind.Directory).Concat(items.Where(e => e.Kind != EntryKind.Directory))) {
            _rows.Add(new DeleteRow(entry));
            if (withCompanions && entry.Companions is { } companions) {
                _rows.AddRange(companions.Select(c => new DeleteRow(c)));
            }
        }
        _tableRows = IsSingle ? _rows.Skip(1).ToList() : _rows;
    }


    public IReadOnlyList<FileSystemEntry> Items { get; }

    public DeleteQuestionKind Kind { get; }

    /// <summary>Past the bin, whichever way it came to that.</summary>
    public bool Permanent => Kind != DeleteQuestionKind.Recycle;

    /// <summary>Why the bin refused (<see cref="DeleteQuestionKind.Refused"/>), each reason once.</summary>
    public IReadOnlyList<RecycleUnavailableReason> Reasons { get; init; } = [];

    /// <summary>What the same delete did put in the bin (<see cref="DeleteQuestionKind.Refused"/>).</summary>
    public int Recycled { get; init; }

    /// <summary>Every row: folders, then files, each followed by its companions.</summary>
    public IReadOnlyList<DeleteRow> Rows => _rows;

    /// <summary>One item asked about: described in words, no table of its own.</summary>
    public bool IsSingle => Items.Count == 1;

    /// <summary>The rows the table shows: all of them, or for one item just its companions.</summary>
    public IReadOnlyList<DeleteRow> TableRows => _tableRows;

    /// <summary>Column names over the table: not over the companions of one item.</summary>
    public bool ShowsHeader => !IsSingle;

    /// <summary>The "inside" column: only when there is a folder to fill it.</summary>
    public bool ShowsInside => !IsSingle && _rows.Any(r => r.IsFolder);

    /// <summary>How many companion files go along.</summary>
    public int Companions => _rows.Count(r => r.IsCompanion);

    /// <summary>How many of the items are marked read-only; none asked about once the bin refused them - the mark was cleared before.</summary>
    public int ReadOnly => Kind == DeleteQuestionKind.Refused ? 0 : _rows.Count(r => r.IsReadOnly);

    /// <summary>Something is still being counted: the totals will grow.</summary>
    public bool IsCounting => _rows.Any(r => r.IsCounting);

    /// <summary>The window's title: the question in short.</summary>
    public string Title => Kind switch {
        DeleteQuestionKind.Permanent => Say("ConfirmDeleteTitle"),
        DeleteQuestionKind.Refused => Say("RecycleUnavailableTitle"),
        _ => Say("ConfirmRecycleTitle"),
    };

    /// <summary>"Move the folder "X" to the bin?", "Permanently delete 14 items?" - in the words of the resx.</summary>
    public string Headline {
        get {
            if (IsSingle) {
                var row = _rows[0];
                string kind = row.IsFolder ? Say("KindFolder") : Say("KindFile");

                return Kind switch {
                    DeleteQuestionKind.Permanent => Fill("ConfirmDeleteOne", kind, row.Name),
                    DeleteQuestionKind.Refused => Fill("RecycleUnavailableOne", kind, row.Name),
                    _ => Fill("ConfirmRecycleOne", kind, row.Name),
                };
            }

            string caption = CountCaption.Of(Items, _text);

            return Kind switch {
                DeleteQuestionKind.Permanent => Fill("ConfirmDeleteMany", caption),
                DeleteQuestionKind.Refused => Fill("RecycleUnavailableMany", caption),
                _ => Fill("ConfirmRecycleMany", caption),
            };
        }
    }

    /// <summary>One item: where it is. Several: null - the table says it.</summary>
    public string? Path => IsSingle ? _rows[0].FullPath : null;

    /// <summary>Why the bin refused, a line a reason - what the window puts where it cannot be missed.</summary>
    public IReadOnlyList<string> ReasonLines => Reasons.Distinct()
        .Select(r => r == RecycleUnavailableReason.PathTooLong ? Say("RecycleUnavailableTooLong") : Say("RecycleUnavailableNoBin"))
        .ToList();

    /// <summary>"Read-only: 2 - ..." - null when none is.</summary>
    public string? ReadOnlyLine => ReadOnly > 0 ? Fill("ConfirmDeleteReadOnly", ReadOnly) : null;

    /// <summary>"The other 11 are in the bin already." - only after a refusal that left some in.</summary>
    public string? RestLine => Kind == DeleteQuestionKind.Refused && Recycled > 0 ? Fill("RecycleUnavailableRestRecycled", Recycled) : null;

    /// <summary>What cannot be taken back, said last; null for the bin.</summary>
    public string? Warning => Kind switch {
        DeleteQuestionKind.Permanent => Say("ConfirmIrreversible"),
        DeleteQuestionKind.Refused => Say("RecycleUnavailableIrreversible"),
        _ => null,
    };


    /// <summary>
    /// The grey line under the headline. One file - its size; one folder -
    /// "Inside: 1,284 files, 23 folders, 18.4 GB", or what stands in for
    /// that while it is counted or when it cannot be read. Several items -
    /// "Total: ..." over everything that goes, companions and the insides of
    /// the folders included, with "counting" while it still grows.
    /// </summary>
    /// <param name="size">Bytes in words, the way the list writes them.</param>
    public string Summary(Func<long, string> size) {
        if (IsSingle) {
            var row = _rows[0];
            if (!row.IsFolder) {
                return row.Size is { } bytes ? size(bytes) : "";
            }

            string inside = row.Inside switch {
                null => Say("ConfirmDeleteCounting"),
                { IsUnreadable: true } => Say("ConfirmDeleteUnreadable"),
                { Files: 0, Folders: 0 } => Say("ConfirmDeleteEmpty"),
                var stats => Fill("ConfirmDeleteAmount", Counts(stats.Files, stats.Folders), size(stats.TotalSize)),
            };

            return Fill("ConfirmDeleteInside", inside);
        }

        long files = 0;
        long folders = 0;
        long total = 0;
        foreach (var row in _rows) {
            if (row.IsFolder) {
                folders++;
            } else {
                files++;
            }
            if (row.Inside is { } stats) {
                files += stats.Files;
                folders += stats.Folders;
            }
            total += row.Bytes;
        }

        string amount = Fill("ConfirmDeleteAmount", Counts(files, folders), size(total));

        return IsCounting ? Fill("ConfirmDeleteTotalCounting", amount) : Fill("ConfirmDeleteTotal", amount);
    }

    /// <summary>The "inside" cell: "1,284 files, 23 folders", "empty"; nothing for a file and while the folder is counted.</summary>
    public string InsideOf(DeleteRow row) {
        return row.Inside switch {
            null => "",
            { IsUnreadable: true } => Say("ConfirmDeleteUnreadable"),
            var stats => Counts(stats.Files, stats.Folders),
        };
    }

    /// <summary>
    /// Weighs what is not known yet, off the calling thread: the companions
    /// first - a look each - then the folders top to bottom, one at a time,
    /// the disk being one. After each answer the model is updated back on
    /// the caller's context and <paramref name="changed"/> is told the row
    /// (null - several rows at once). Stops quietly once
    /// <paramref name="ct"/> is cancelled: the window has closed, and nobody
    /// is waiting for the numbers.
    /// </summary>
    public async Task CountAsync(Action<DeleteRow?> changed, CancellationToken ct) {
        try {
            var companions = _rows.Where(r => r.IsCompanion && !r.Weighed).ToArray();
            if (companions.Length > 0) {
                string[] paths = companions.Select(r => r.FullPath).ToArray();
                long?[] weights = await Task.Run(() => paths.Select(Weigh).ToArray(), ct);
                ct.ThrowIfCancellationRequested();
                for (int i = 0; i < companions.Length; i++) {
                    companions[i].Weight = weights[i];
                    companions[i].Weighed = true;
                }
                changed(null);
            }

            foreach (var row in _rows.Where(r => r.IsFolder && r.IsCounting).ToArray()) {
                string path = row.FullPath;
                var inside = await Task.Run(() => Walk(path, ct), ct);
                ct.ThrowIfCancellationRequested();
                row.Inside = inside;
                changed(row);
            }
        } catch (OperationCanceledException) {
            // The window is gone.
        }
    }


    /// <summary>Any thread. A companion that is not there to look at weighs nothing - a dash, not an error.</summary>
    private long? Weigh(string path) {
        try {
            return _fs.GetEntry(path)?.Size;
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
            return null;
        }
    }

    /// <summary>Any thread. A walk that fails outright counts as a folder that cannot be read.</summary>
    private FolderStats Walk(string path, CancellationToken ct) {
        try {
            return FolderStatistics.Collect(_fs, path, maxTypes: 0, maxSubfolders: 0, ct: ct);
        } catch (Exception ex) when (ex is not OperationCanceledException and not OutOfMemoryException) {
            return FolderStats.Empty with { IsUnreadable = true };
        }
    }

    /// <summary>"1,284 files, 23 folders", "3 folders", "empty".</summary>
    private string Counts(long files, long folders) {
        string? filesText = files > 0 ? Text.PluralForm(Say("MenuCaptionFileCount"), files) : null;
        string? foldersText = folders > 0 ? Text.PluralForm(Say("MenuCaptionFolderCount"), folders) : null;

        return (filesText, foldersText) switch {
            ( { } f, { } d) => Fill("ConfirmDeleteFilesAndFolders", f, d),
            ( { } f, null) => f,
            (null, { } d) => d,
            _ => Say("ConfirmDeleteEmpty"),
        };
    }

    private string Say(string key) {
        return _text is null ? Text.Get(key) : _text.Get(key);
    }

    private string Fill(string key, params object[] args) {
        string template = Say(key);
        try {
            return string.Format(template, args);
        } catch (FormatException) {
            return template;
        }
    }
}
