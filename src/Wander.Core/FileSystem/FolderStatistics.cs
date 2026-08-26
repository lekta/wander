namespace Wander.Core.FileSystem;

/// <summary>One extension bucket: how many files and how much they weigh.</summary>
public sealed record FolderTypeGroup(string Extension, int Count, long Size);

/// <summary>
/// What the preview pane shows instead of a placeholder when a folder is
/// selected: totals plus the handful of file types that actually fill the
/// folder up.
/// </summary>
public sealed record FolderStats(
    int Files,
    int Folders,
    long TotalSize,
    IReadOnlyList<FolderTypeGroup> Types,
    // True when the walk stopped on the file budget: totals are a floor,
    // not the truth, and the UI has to say so rather than lie quietly.
    bool Truncated) {

    public static readonly FolderStats Empty =
        new(0, 0, 0, Array.Empty<FolderTypeGroup>(), false);
}


/// <summary>
/// Recursive folder census, built on <see cref="IFileSystem"/> so it stays
/// testable and platform-free. Iterative rather than recursive: a deep tree
/// (or a reparse-point loop) must not take the stack with it.
/// </summary>
public static class FolderStatistics {
    /// <summary>How many files to look at before giving up and reporting partial results.</summary>
    public const int DefaultFileBudget = 200_000;

    /// <summary>
    /// How deep to descend. This is the guard against reparse-point loops:
    /// a junction pointing at its own ancestor generates an endlessly deeper
    /// chain of *distinct* paths, so remembering where we have been does not
    /// help — only refusing to go deeper does. Real trees do not come close
    /// to this.
    /// </summary>
    public const int DefaultMaxDepth = 64;

    /// <summary>
    /// How many folders to visit. Bounds a walk that is wide rather than
    /// deep, and bounds the pending stack with it.
    /// </summary>
    public const int DefaultFolderBudget = 100_000;


    /// <summary>
    /// Walks <paramref name="path"/> and aggregates it. Unreadable subtrees
    /// are skipped rather than aborting the walk — one protected folder
    /// deep inside must not blank the whole panel.
    /// </summary>
    public static FolderStats Collect(
        IFileSystem fs,
        string path,
        int maxTypes = 8,
        int fileBudget = DefaultFileBudget,
        int maxDepth = DefaultMaxDepth,
        int folderBudget = DefaultFolderBudget,
        CancellationToken ct = default) {

        int files = 0;
        int folders = 0;
        long total = 0;
        bool truncated = false;
        var byExtension = new Dictionary<string, (int Count, long Size)>(StringComparer.OrdinalIgnoreCase);

        var pending = new Stack<(string Path, int Depth)>();
        pending.Push((path, 0));

        while (pending.Count > 0) {
            ct.ThrowIfCancellationRequested();
            var (current, depth) = pending.Pop();

            IReadOnlyList<FileSystemEntry> children;
            try {
                children = fs.Enumerate(current);
            } catch {
                // Access denied / disappeared mid-walk: skip this subtree.
                continue;
            }

            foreach (var child in children) {
                if (child.Kind == EntryKind.Directory) {
                    folders++;
                    // Counted either way — it is part of the folder. Whether
                    // we look *inside* is what the budgets decide.
                    if (depth + 1 > maxDepth || folders >= folderBudget) {
                        truncated = true;
                        continue;
                    }
                    pending.Push((child.FullPath, depth + 1));
                    continue;
                }

                files++;
                long size = child.Size ?? 0;
                total += size;

                string ext = ExtensionOf(child.Name);
                var bucket = byExtension.TryGetValue(ext, out var found) ? found : default;
                byExtension[ext] = (bucket.Count + 1, bucket.Size + size);

                if (files >= fileBudget) {
                    truncated = true;
                    pending.Clear();
                    break;
                }
            }
        }

        // Biggest first: "what is eating this folder" is the question the
        // panel is there to answer. Count breaks ties so the order is stable.
        var types = byExtension
            .Select(kv => new FolderTypeGroup(kv.Key, kv.Value.Count, kv.Value.Size))
            .OrderByDescending(t => t.Size)
            .ThenByDescending(t => t.Count)
            .ThenBy(t => t.Extension, StringComparer.OrdinalIgnoreCase)
            .Take(maxTypes)
            .ToArray();

        return new FolderStats(files, folders, total, types, truncated);
    }


    /// <summary>
    /// Lower-cased extension without the dot; "—" for a file that has none,
    /// so the bucket is still nameable in the UI. Leading-dot names
    /// (".gitignore") count as extension-less — that is what they are to a
    /// reader, whatever <c>Path.GetExtension</c> says.
    /// </summary>
    private static string ExtensionOf(string name) {
        int dot = name.LastIndexOf('.');
        if (dot <= 0 || dot == name.Length - 1) {
            return "—";
        }

        return name[(dot + 1)..].ToLowerInvariant();
    }
}
