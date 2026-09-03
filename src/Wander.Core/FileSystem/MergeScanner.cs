namespace Wander.Core.FileSystem;

/// <summary>
/// What lies inside two folders of one name, for the window to show the
/// moment the user says "merge": every name present on both sides, as a
/// tree - a folder on both sides merges in turn and carries its own
/// collisions under it - and a count of the files that have nothing in
/// their way and will simply cross over.
///
/// <para>
/// Reads through <see cref="IFileSystem"/>, so a test reaches it and a
/// folder inside an archive - which the shell reads, not the file system -
/// is refused before it starts (<see cref="FileConflictInfo.SourceReachable"/>).
/// </para>
/// </summary>
public static class MergeScanner {
    /// <summary>
    /// A junction pointing at an ancestor gives an endless chain of
    /// different paths; no real tree is this deep.
    /// </summary>
    private const int MaxDepth = 64;


    /// <summary>One collision inside; a folder pair carries its own underneath.</summary>
    /// <param name="FreeFiles">Files under this pair that cross over without a question - the whole subtree.</param>
    public sealed record Node(FileConflictInfo Conflict, IReadOnlyList<Node> Children, int FreeFiles);

    /// <param name="FreeFiles">Files anywhere under the scanned folder that cross over without a question.</param>
    public sealed record Result(IReadOnlyList<Node> Conflicts, int FreeFiles);


    /// <summary>Walks <paramref name="sourceFolder"/> against <paramref name="targetFolder"/>.</summary>
    /// <exception cref="OperationCanceledException">The window closed part-way.</exception>
    public static Result Scan(IFileSystem fs, string sourceFolder, string targetFolder, bool isMove, CancellationToken ct = default) {
        return Scan(fs, sourceFolder, targetFolder, isMove, depth: 0, ct);
    }


    private static Result Scan(IFileSystem fs, string sourceFolder, string targetFolder, bool isMove, int depth, CancellationToken ct) {
        ct.ThrowIfCancellationRequested();

        var conflicts = new List<Node>();
        int free = 0;
        if (depth >= MaxDepth) {
            return new Result(conflicts, free);
        }

        foreach (var entry in fs.Enumerate(sourceFolder)) {
            string dest = Path.Combine(targetFolder, entry.Name);
            var existing = fs.GetEntry(dest);
            if (existing is null) {
                free += entry.Kind == EntryKind.Directory ? CountFiles(fs, entry.FullPath, depth + 1, ct) : 1;
                continue;
            }

            var pair = new FileConflictInfo(entry, existing, isMove);
            if (entry.Kind == EntryKind.Directory && existing.Kind == EntryKind.Directory) {
                var inner = Scan(fs, entry.FullPath, dest, isMove, depth + 1, ct);
                conflicts.Add(new Node(pair, inner.Conflicts, inner.FreeFiles));
                free += inner.FreeFiles;
            } else {
                conflicts.Add(new Node(pair, Array.Empty<Node>(), 0));
            }
        }

        return new Result(conflicts, free);
    }

    private static int CountFiles(IFileSystem fs, string folder, int depth, CancellationToken ct) {
        ct.ThrowIfCancellationRequested();
        if (depth >= MaxDepth) {
            return 0;
        }

        int count = 0;
        foreach (var entry in fs.Enumerate(folder)) {
            count += entry.Kind == EntryKind.Directory ? CountFiles(fs, entry.FullPath, depth + 1, ct) : 1;
        }

        return count;
    }
}
