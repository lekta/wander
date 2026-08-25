using Wander.Core.FileSystem;
using Wander.Core.Logging;
using Wander.Core.Undo;

namespace Wander.Core.Companions;

/// <summary>Which of a sidecar's two rating fields an edit is about.</summary>
public enum RatingField {
    Rank,
    ColorLabel,
}


/// <summary>
/// Reads what a companion has to say about its main file, and — for the
/// formats Wander is allowed to edit — writes it back.
///
/// <para>
/// Writing into somebody else's format is the one thing Wander does that
/// can destroy work it didn't create, so the write path here is deliberately
/// narrow: only the rating fields, only in a file that <em>already exists</em>,
/// only through <see cref="IFileSystem.ReplaceAtomic"/>, always logged, and
/// always with the previous value on the undo stack.
/// </para>
///
/// <para>
/// Which parser handles a path is decided by its extension — the same
/// suffix that made the file a companion in the first place. Unity
/// <c>.meta</c> is read-only on purpose: Unity owns that file and
/// regenerates it on its own terms, and a rewrite of ours could detach an
/// asset from every reference in every scene.
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


    /// <summary>True for a sidecar whose rating Wander knows how to read and write.</summary>
    public static bool IsRatingSidecar(string path) {
        string ext = Path.GetExtension(path);

        return ext.Equals(".pp3", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".xmp", StringComparison.OrdinalIgnoreCase);
    }


    /// <summary>Rating held by a <c>.pp3</c> or <c>.xmp</c>, or null when it can't be read.</summary>
    public SidecarRating? ReadRating(string path) {
        if (!IsRatingSidecar(path)) {
            return null;
        }

        return TryRead(path, bytes => IsPp3(path) ? Pp3Sidecar.Read(bytes) : XmpSidecar.Read(bytes));
    }


    /// <summary>Contents of a Unity <c>.meta</c>, or null when it can't be read.</summary>
    public UnityMetaInfo? ReadUnityMeta(string path) {
        return TryRead(path, UnityMetaSidecar.Read);
    }


    /// <summary>Sets a rating field in an existing sidecar and makes it undoable.</summary>
    public void SetRating(string path, RatingField field, int value) {
        int previous = ApplyRating(path, field, value);
        _undo.Push(new SidecarRatingAction(this, path, field, previous, value));
    }


    /// <summary>
    /// The same write without touching the undo stack — used by
    /// <see cref="SidecarRatingAction"/>, which is already being popped off it.
    /// Returns the value that was there before.
    /// </summary>
    internal int ApplyRating(string path, RatingField field, int value) {
        if (!IsRatingSidecar(path)) {
            throw new NotSupportedException($"No rating support for {Path.GetExtension(path)} files");
        }
        if (!_fs.FileExists(path)) {
            // Creating the sidecar is a different decision with different
            // consequences (an empty .pp3 changes how RawTherapee renders the
            // photo) and is not something a click on a star may do.
            throw new FileNotFoundException("No sidecar to write to", path);
        }

        byte[] original = _fs.ReadAllBytes(path);
        bool pp3 = IsPp3(path);
        var before = pp3 ? Pp3Sidecar.Read(original) : XmpSidecar.Read(original);

        // A sidecar with no such key reads as "unset", which is what 0 means
        // — so undo of the first edit writes 0 rather than deleting the key.
        int previous = (field == RatingField.Rank ? before.Rank : before.ColorLabel) ?? 0;

        byte[] updated = (pp3, field) switch {
            (true, RatingField.Rank) => Pp3Sidecar.WithRank(original, value),
            (true, RatingField.ColorLabel) => Pp3Sidecar.WithColorLabel(original, value),
            (false, RatingField.Rank) => XmpSidecar.WithRating(original, value),
            (false, RatingField.ColorLabel) => XmpSidecar.WithColorLabel(original, value),
            _ => throw new NotSupportedException($"Unknown rating field {field}"),
        };

        _fs.ReplaceAtomic(path, updated);
        _log.Info($"Sidecar {field}: {path} {previous} -> {value}");

        return previous;
    }


    private static bool IsPp3(string path) {
        return Path.GetExtension(path).Equals(".pp3", StringComparison.OrdinalIgnoreCase);
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
/// Undo of a rating change — put the old value back. Restoring is the same
/// guarded write as setting, so the undo itself stays atomic.
/// </summary>
public sealed record SidecarRatingAction(
    CompanionMetadataService Service, string Path, RatingField Field, int OldValue, int NewValue) : IUndoableAction {

    public string Description =>
        Field == RatingField.Rank
            ? $"Rating {NewValue} on '{System.IO.Path.GetFileName(Path)}'"
            : $"Colour {ColorLabels.Name(NewValue)} on '{System.IO.Path.GetFileName(Path)}'";


    public void Undo() {
        Service.ApplyRating(Path, Field, OldValue);
    }
}
