using Wander.Core.FileSystem;
using Wander.Core.Logging;
using Wander.Core.Undo;

namespace Wander.Core.Companions;

/// <summary>
/// Reads what a companion has to say about its main file, and — for the one
/// format Wander is allowed to edit — writes it back.
///
/// <para>
/// Writing into somebody else's format is the first thing Wander does that
/// can destroy work it didn't create, so the write path here is deliberately
/// narrow: only <c>[General] Rank</c> of an <em>existing</em> <c>.pp3</c>,
/// only through <see cref="IFileSystem.ReplaceAtomic"/>, always logged, and
/// always with the previous value on the undo stack.
/// </para>
/// </summary>
public sealed class CompanionMetadataService {
    private readonly IFileSystem _fs;
    private readonly UndoService _undo;
    private readonly ILogger _log;


    public CompanionMetadataService(IFileSystem fs, UndoService undo, ILogger log) {
        _fs = fs;
        _undo = undo;
        _log = log;
    }


    /// <summary>Rating of a <c>.pp3</c>, or null when it can't be read.</summary>
    public Pp3Rating? ReadPp3(string path) {
        return TryRead(path, Pp3Sidecar.Read);
    }


    /// <summary>Contents of a Unity <c>.meta</c>, or null when it can't be read.</summary>
    public UnityMetaInfo? ReadUnityMeta(string path) {
        return TryRead(path, UnityMetaSidecar.Read);
    }


    /// <summary>
    /// Sets the star rating in an existing <c>.pp3</c>. Throws
    /// <see cref="FileNotFoundException"/> rather than creating the sidecar:
    /// a <c>.pp3</c> that appears out of nowhere changes how RawTherapee
    /// renders the photo, and that is not a side effect of clicking a star.
    /// </summary>
    public void SetPp3Rank(string path, int rank) {
        int previous = ApplyPp3Rank(path, rank);
        _undo.Push(new Pp3RankAction(this, path, previous, rank));
    }


    /// <summary>
    /// The same write without touching the undo stack — used by
    /// <see cref="Pp3RankAction"/>, which is already being popped off it.
    /// </summary>
    internal int ApplyPp3Rank(string path, int rank) {
        if (!_fs.FileExists(path)) {
            throw new FileNotFoundException("No .pp3 sidecar to write to", path);
        }

        byte[] original = _fs.ReadAllBytes(path);
        // A sidecar with no Rank key reads as "unrated", which is what a
        // rank of 0 means — so undo of the first rating writes Rank=0 rather
        // than deleting the key again.
        int previous = Pp3Sidecar.Read(original).Rank ?? 0;
        _fs.ReplaceAtomic(path, Pp3Sidecar.WithRank(original, rank));
        _log.Info($"pp3 rank: {path} {previous} -> {rank}");

        return previous;
    }


    private T? TryRead<T>(string path, Func<byte[], T> parse) where T : class {
        try {
            return _fs.FileExists(path) ? parse(_fs.ReadAllBytes(path)) : null;
        } catch (Exception ex) {
            _log.Warn($"Companion read failed: {path} ({ex.Message})");

            return null;
        }
    }
}


/// <summary>
/// Undo of a rating change — put the old star count back. Restoring is the
/// same guarded write as setting, so the undo itself stays atomic and lands
/// its own entry on the stack.
/// </summary>
public sealed record Pp3RankAction(CompanionMetadataService Service, string Path, int OldRank, int NewRank) : IUndoableAction {
    public string Description => $"Rating {NewRank} on '{System.IO.Path.GetFileName(Path)}'";


    public void Undo() {
        Service.ApplyPp3Rank(Path, OldRank);
    }
}
