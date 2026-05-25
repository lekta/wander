namespace Wander.Core.FileSystem;

public sealed class FileOperationService {
    private readonly IFileSystem _fs;


    public FileOperationService(IFileSystem fs) {
        _fs = fs;
    }

    public FileOperationService() : this(ServiceLocator.Get<IFileSystem>()) {
    }


    public void Copy(string source, string destination, bool overwrite = false) {
        if (_fs.DirectoryExists(source)) {
            _fs.CopyDirectory(source, destination, overwrite);
            return;
        }

        if (_fs.FileExists(source)) {
            _fs.CopyFile(source, destination, overwrite);
            return;
        }

        throw new FileNotFoundException("Source not found", source);
    }

    public void Move(string source, string destination) {
        if (!_fs.FileExists(source) && !_fs.DirectoryExists(source)) {
            throw new FileNotFoundException("Source not found", source);
        }
        _fs.MoveEntry(source, destination);
    }

    public void Delete(string path) {
        if (_fs.DirectoryExists(path)) {
            _fs.DeleteDirectory(path, recursive: true);
            return;
        }

        if (_fs.FileExists(path)) {
            _fs.DeleteFile(path);
            return;
        }

        throw new FileNotFoundException("Path not found", path);
    }

    public void Rename(string path, string newName) {
        if (string.IsNullOrWhiteSpace(newName)) {
            throw new ArgumentException("New name cannot be empty", nameof(newName));
        }
        _fs.Rename(path, newName);
    }

    public void CreateFolder(string parent, string name) {
        var path = Path.Combine(parent, name);
        _fs.CreateDirectory(path);
    }


    // --- Batch operations with conflict resolution ---------------------

    /// <summary>
    /// Per-item result from a batch operation, so callers can report which
    /// items succeeded / were skipped / failed.
    /// </summary>
    public sealed record BatchItemResult(string Source, string FinalDestination, BatchItemStatus Status, Exception? Error);

    public enum BatchItemStatus { Ok, Skipped, Replaced, Renamed, Cancelled, Failed }

    public IReadOnlyList<BatchItemResult> CopyMany(IReadOnlyList<string> sources, string targetFolder, IConflictResolver resolver) {
        return ApplyBatch(sources, targetFolder, isMove: false, resolver);
    }

    public IReadOnlyList<BatchItemResult> MoveMany(IReadOnlyList<string> sources, string targetFolder, IConflictResolver resolver) {
        return ApplyBatch(sources, targetFolder, isMove: true, resolver);
    }


    private IReadOnlyList<BatchItemResult> ApplyBatch(IReadOnlyList<string> sources, string targetFolder, bool isMove, IConflictResolver resolver) {
        var pairs = new List<(string src, string dest)>();
        foreach (string src in sources) {
            string name = Path.GetFileName(src.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            pairs.Add((src, Path.Combine(targetFolder, name)));
        }

        int conflictCount = pairs.Count(p => Exists(p.dest));
        ConflictResolution? batchOverride = null;

        if (conflictCount > 0) {
            batchOverride = resolver.StartBatch(conflictCount);
            if (batchOverride == ConflictResolution.Cancel) {
                return pairs.Select(p => new BatchItemResult(p.src, p.dest, BatchItemStatus.Cancelled, null)).ToList();
            }
            // "Rename all" / "Skip all" / "Replace all" makes sense; null means per-item.
        }

        var results = new List<BatchItemResult>(pairs.Count);
        foreach (var (src, originalDest) in pairs) {
            string dest = originalDest;
            BatchItemStatus statusKind = BatchItemStatus.Ok;
            bool exists = Exists(dest);

            ConflictResolution? choice = exists
                ? (batchOverride ?? resolver.Resolve(BuildInfo(src, dest)))
                : null;

            switch (choice) {
                case ConflictResolution.Cancel:
                    results.Add(new BatchItemResult(src, dest, BatchItemStatus.Cancelled, null));
                    // Mark the remainder as cancelled too.
                    foreach (var (rsrc, rdest) in pairs.Skip(results.Count)) {
                        results.Add(new BatchItemResult(rsrc, rdest, BatchItemStatus.Cancelled, null));
                    }
                    return results;
                case ConflictResolution.Skip:
                    results.Add(new BatchItemResult(src, dest, BatchItemStatus.Skipped, null));
                    continue;
                case ConflictResolution.Rename:
                    dest = GenerateUniqueName(dest);
                    statusKind = BatchItemStatus.Renamed;
                    break;
                case ConflictResolution.Replace:
                    statusKind = BatchItemStatus.Replaced;
                    break;
                case null:
                    break;
            }

            try {
                ApplyOne(src, dest, isMove, allowOverwrite: choice == ConflictResolution.Replace);
                results.Add(new BatchItemResult(src, dest, statusKind, null));
            } catch (Exception ex) {
                results.Add(new BatchItemResult(src, dest, BatchItemStatus.Failed, ex));
            }
        }

        return results;
    }

    private void ApplyOne(string src, string dest, bool isMove, bool allowOverwrite) {
        if (isMove) {
            if (allowOverwrite) {
                // .NET's Move doesn't support overwrite-for-folders; clear target first.
                if (_fs.FileExists(dest)) {
                    _fs.DeleteFile(dest);
                } else if (_fs.DirectoryExists(dest)) {
                    _fs.DeleteDirectory(dest, recursive: true);
                }
            }
            _fs.MoveEntry(src, dest);
            return;
        }

        if (_fs.DirectoryExists(src)) {
            _fs.CopyDirectory(src, dest, overwrite: allowOverwrite);
        } else {
            _fs.CopyFile(src, dest, overwrite: allowOverwrite);
        }
    }

    private bool Exists(string path) => _fs.FileExists(path) || _fs.DirectoryExists(path);

    private string GenerateUniqueName(string desiredPath) {
        string dir = Path.GetDirectoryName(desiredPath) ?? "";
        string nameNoExt = Path.GetFileNameWithoutExtension(desiredPath);
        string ext = Path.GetExtension(desiredPath);
        int i = 1;
        while (true) {
            string candidate = Path.Combine(dir, $"{nameNoExt} ({i}){ext}");
            if (!Exists(candidate)) {
                return candidate;
            }
            i++;
        }
    }

    private FileConflictInfo BuildInfo(string src, string dest) {
        var srcEntry = _fs.GetEntry(src) ?? Unknown(src);
        var dstEntry = _fs.GetEntry(dest) ?? Unknown(dest);
        return new FileConflictInfo(srcEntry, dstEntry);
    }

    private static FileSystemEntry Unknown(string path) {
        return new FileSystemEntry(
            Name: Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
            FullPath: path,
            Kind: EntryKind.File,
            Size: null,
            ModifiedUtc: DateTime.MinValue,
            IsHidden: false,
            IsReadOnly: false);
    }
}
