namespace Wander.Core.FileSystem;

/// <summary>One extension bucket: how many files and how much they weigh.</summary>
public sealed record FolderTypeGroup(string Extension, int Count, long Size);

/// <summary>One folder right under the one counted: how many files it holds, at any depth, and how much they weigh.</summary>
public sealed record FolderSubfolder(string Name, int Files, long Size);

/// <summary>
/// What the preview pane shows instead of a placeholder when a folder is
/// selected: totals plus the handful of file types and of subfolders that
/// actually fill the folder up.
/// </summary>
public sealed record FolderStats(
    int Files,
    int Folders,
    long TotalSize,
    IReadOnlyList<FolderTypeGroup> Types,
    IReadOnlyList<FolderSubfolder> Subfolders,
    // Subfolders past the list above: the pane says there are more.
    int SubfoldersLeftOut,
    // True when the walk refused to go somewhere: totals are a floor, not
    // the truth, and the UI has to say so rather than lie quietly. By
    // default only the depth guard can do this — see DefaultMaxDepth.
    bool Truncated) {

    public static readonly FolderStats Empty =
        new(0, 0, 0, Array.Empty<FolderTypeGroup>(), Array.Empty<FolderSubfolder>(), 0, false);

    /// <summary>The folder itself could not be read - access denied, gone: the zeros say nothing about what is in it.</summary>
    public bool IsUnreadable { get; init; }
}


/// <summary>
/// Running totals, handed out while the walk is still going.
///
/// <para>
/// There is no percentage here, and there cannot be: knowing how far along
/// the walk is would mean knowing how many files the tree holds, and the
/// only way to learn that is to walk it. Windows keeps no such count for a
/// folder — the number Explorer shows in Properties is produced by the same
/// walk, which is why it counts up there too. So the honest report is "this
/// much so far", not "this much of that much".
/// </para>
/// </summary>
public readonly record struct FolderProgress(int Files, int Folders, long TotalSize);


/// <summary>
/// Recursive folder census, built on <see cref="IFileSystem"/> so it stays
/// testable and platform-free. Iterative rather than recursive: a deep tree
/// (or a reparse-point loop) must not take the stack with it.
/// </summary>
public static class FolderStatistics {
    /// <summary>
    /// No ceiling: the walk finishes what it started.
    ///
    /// <para>
    /// It used to stop at 200 000 files and say "this folder is too big".
    /// The budget was never about memory — the walk holds one dictionary of
    /// extensions and a stack of pending paths — it was about time, and it
    /// bought that time by lying about the numbers. Reporting progress as
    /// it goes buys the same time honestly: the user watches the count
    /// climb and can walk away, which cancels the token. The ceiling stays
    /// as a parameter because a caller with a different bargain may still
    /// want one.
    /// </para>
    /// </summary>
    public const int NoBudget = int.MaxValue;

    /// <summary>How often running totals are handed out, at most.</summary>
    private const long ProgressEveryMs = 150;

    /// <summary>
    /// How deep to descend. This is the guard against reparse-point loops:
    /// a junction pointing at its own ancestor generates an endlessly deeper
    /// chain of *distinct* paths, so remembering where we have been does not
    /// help — only refusing to go deeper does. Real trees do not come close
    /// to this.
    /// </summary>
    public const int DefaultMaxDepth = 64;

    /// <summary>The bucket of the files with no extension to go by.</summary>
    public const string NoExtension = "—";


    /// <summary>
    /// Walks <paramref name="path"/> and aggregates it. Unreadable subtrees
    /// are skipped rather than aborting the walk — one protected folder
    /// deep inside must not blank the whole panel.
    /// </summary>
    /// <param name="progress">
    /// Told the running totals about six times a second, so a big tree can
    /// show its numbers climbing instead of an unexplained wait. Reported
    /// from whatever thread the walk runs on — an <see cref="IProgress{T}"/>
    /// built on the UI thread (<c>Progress&lt;T&gt;</c>) marshals it back by
    /// itself.
    /// </param>
    public static FolderStats Collect(
        IFileSystem fs,
        string path,
        int maxTypes = 8,
        int fileBudget = NoBudget,
        int maxDepth = DefaultMaxDepth,
        int folderBudget = NoBudget,
        IProgress<FolderProgress>? progress = null,
        int maxSubfolders = 16,
        CancellationToken ct = default) {

        int files = 0;
        int folders = 0;
        long total = 0;
        bool truncated = false;
        bool unreadable = false;
        var byExtension = new Dictionary<string, (int Count, long Size)>(StringComparer.OrdinalIgnoreCase);
        // What each folder right under this one has gathered; every folder
        // below it carries the index of the one it is in.
        var tops = new List<(string Name, int Files, long Size)>();

        var pending = new Stack<(string Path, int Depth, int Top)>();
        pending.Push((path, 0, -1));

        // Started only when somebody is listening: the clock is the whole
        // cost of reporting when nobody is.
        var clock = progress is null ? null : System.Diagnostics.Stopwatch.StartNew();
        bool reportedOnce = false;

        // The first one goes out as soon as there is anything to say — even
        // "nothing yet" — so the panel shows that the walk has started
        // rather than a blank line for the first sixth of a second.
        void ReportIfDue() {
            if (clock is null) {
                return;
            }
            if (reportedOnce && clock.ElapsedMilliseconds < ProgressEveryMs) {
                return;
            }
            reportedOnce = true;
            clock.Restart();
            progress!.Report(new FolderProgress(files, folders, total));
        }

        while (pending.Count > 0) {
            ct.ThrowIfCancellationRequested();
            var (current, depth, top) = pending.Pop();

            IReadOnlyList<FileSystemEntry> children;
            try {
                children = fs.Enumerate(current, ct: ct);
            } catch (OperationCanceledException) {
                throw;
            } catch {
                // Access denied / disappeared mid-walk: skip this subtree.
                unreadable |= depth == 0;
                continue;
            }

            foreach (var child in children) {
                if (child.Kind == EntryKind.Directory) {
                    folders++;
                    int childTop = top;
                    if (depth == 0) {
                        tops.Add((child.Name, 0, 0));
                        childTop = tops.Count - 1;
                    }
                    // Counted either way — it is part of the folder. Whether
                    // we look *inside* is what the depth guard and the
                    // budgets decide.
                    if (depth + 1 > maxDepth || folders >= folderBudget) {
                        truncated = true;
                        continue;
                    }
                    pending.Push((child.FullPath, depth + 1, childTop));
                    continue;
                }

                files++;
                long size = child.Size ?? 0;
                total += size;
                if (top >= 0) {
                    var gathered = tops[top];
                    tops[top] = (gathered.Name, gathered.Files + 1, gathered.Size + size);
                }

                string ext = ExtensionOf(child.Name);
                var bucket = byExtension.TryGetValue(ext, out var found) ? found : default;
                byExtension[ext] = (bucket.Count + 1, bucket.Size + size);

                // One folder can hold the whole tree, so the check cannot
                // live only between folders — but reading the clock per file
                // is a syscall per file, hence the counter in front of it.
                if ((files & 1023) == 0) {
                    ReportIfDue();
                }

                if (files >= fileBudget) {
                    truncated = true;
                    pending.Clear();
                    break;
                }
            }

            // …and a tree of many small folders never reaches 1024 files in
            // one of them.
            ReportIfDue();
        }

        var subfolders = TopSubfolders(tops.Select(t => new FolderSubfolder(t.Name, t.Files, t.Size)), maxSubfolders);

        return new FolderStats(
            files, folders, total, TopTypes(byExtension, maxTypes), subfolders, tops.Count - subfolders.Count, truncated) {
            IsUnreadable = unreadable,
        };
    }


    /// <summary>
    /// Biggest first: "what is eating this folder" is the question the
    /// panel is there to answer. Count breaks ties so the order is stable.
    /// </summary>
    internal static IReadOnlyList<FolderTypeGroup> TopTypes(Dictionary<string, (int Count, long Size)> byExtension, int maxTypes) {
        return byExtension
            .Select(kv => new FolderTypeGroup(kv.Key, kv.Value.Count, kv.Value.Size))
            .OrderByDescending(t => t.Size)
            .ThenByDescending(t => t.Count)
            .ThenBy(t => t.Extension, StringComparer.OrdinalIgnoreCase)
            .Take(maxTypes)
            .ToArray();
    }

    /// <summary>The same question and the same order as <see cref="TopTypes"/>, by subfolder.</summary>
    internal static IReadOnlyList<FolderSubfolder> TopSubfolders(IEnumerable<FolderSubfolder> subfolders, int maxSubfolders) {
        return subfolders
            .OrderByDescending(s => s.Size)
            .ThenByDescending(s => s.Files)
            .ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .Take(maxSubfolders)
            .ToArray();
    }

    /// <summary>
    /// Lower-cased extension without the dot; "—" for a file that has none,
    /// so the bucket is still nameable in the UI. Leading-dot names
    /// (".gitignore") count as extension-less — that is what they are to a
    /// reader, whatever <c>Path.GetExtension</c> says.
    /// </summary>
    internal static string ExtensionOf(string name) {
        int dot = name.LastIndexOf('.');
        if (dot <= 0 || dot == name.Length - 1) {
            return NoExtension;
        }

        return name[(dot + 1)..].ToLowerInvariant();
    }
}
