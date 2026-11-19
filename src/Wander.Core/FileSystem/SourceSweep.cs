namespace Wander.Core.FileSystem;

/// <summary>A file of a folder being moved, as it was when copied, or as it is now.</summary>
public readonly record struct SourceFile(string Path, long Size, DateTime ModifiedUtc);

/// <summary>Why a file stayed at the source of a move across volumes.</summary>
public enum LeftReason {
    /// <summary>Came into the folder after its part of the copy was walked.</summary>
    Appeared,
    /// <summary>Its size or time changed after it was copied: the copy has the older one.</summary>
    Changed,
    /// <summary>Copied whole, but would not go: held, denied.</summary>
    NotDeleted,
}

/// <summary>A file a move across volumes left where it was, and why.</summary>
public sealed record SourceLeftover(string Path, LeftReason Reason);

/// <summary>What <see cref="SourceSweep.Plan"/> decided: the files to delete, and the ones that stay.</summary>
public sealed record SourceSweepPlan(IReadOnlyList<string> Delete, IReadOnlyList<SourceLeftover> Keep);


/// <summary>
/// The second half of a folder moved off its volume - a copy, then the
/// source taken away (audit 2026-11-18, F2). The copy takes minutes on a big
/// folder, and a downloader, a sync client or a tethered camera may write
/// into it all along: what was not copied, or was copied before it changed,
/// must not go with the source. The walk and the deletes are the platform's
/// (<c>SystemIOFileSystem.MoveEntry</c>); which files go is decided here.
/// </summary>
public static class SourceSweep {
    /// <param name="copied">Every file the copy took, with its size and time when it was copied.</param>
    /// <param name="now">Every file under the source now.</param>
    public static SourceSweepPlan Plan(IReadOnlyList<SourceFile> copied, IReadOnlyList<SourceFile> now) {
        var asCopied = new Dictionary<string, SourceFile>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in copied) {
            asCopied[file.Path] = file;
        }

        var delete = new List<string>();
        var keep = new List<SourceLeftover>();
        foreach (var file in now) {
            if (!asCopied.TryGetValue(file.Path, out var was)) {
                keep.Add(new SourceLeftover(file.Path, LeftReason.Appeared));
            } else if (was.Size != file.Size || was.ModifiedUtc != file.ModifiedUtc) {
                keep.Add(new SourceLeftover(file.Path, LeftReason.Changed));
            } else {
                delete.Add(file.Path);
            }
        }

        return new SourceSweepPlan(delete, keep);
    }
}
