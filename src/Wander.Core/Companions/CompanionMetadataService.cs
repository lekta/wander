using System.Threading;
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
    private readonly CompanionResolver _companions;


    /// <summary>
    /// <paramref name="companions"/> is the rule set that decides what a
    /// sidecar is called for a given photo. It defaults to the standard set
    /// so existing call sites stay as they are; it is a parameter at all
    /// because <see cref="CreateRatingSidecar"/> has to invent a file name,
    /// and inventing it from a second copy of the naming rules is how the
    /// two eventually disagree.
    /// </summary>
    public CompanionMetadataService(
        IFileSystem fs, UndoService undo, ILogger log, CompanionResolver? companions = null) {
        _fs = fs;
        _undo = undo;
        _log = log;
        _companions = companions ?? CompanionResolver.Default;
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


    /// <summary>
    /// The rating carried by one of <paramref name="entry"/>'s companions,
    /// or null when it has none that holds ratings. The first rating
    /// sidecar wins — a photo with both a <c>.pp3</c> and an <c>.xmp</c> is
    /// rare, and picking by listing order is at least the same answer the
    /// preview pane gives for the same file.
    /// </summary>
    public SidecarRating? ReadRatingFor(FileSystemEntry entry) {
        if (entry.Companions is not { Count: > 0 } companions) {
            return null;
        }

        foreach (string path in companions) {
            if (IsRatingSidecar(path)) {
                return ReadRating(path);
            }
        }

        return null;
    }


    /// <summary>
    /// The same listing with <see cref="FileSystemEntry.Rating"/> filled in
    /// from each row's sidecar. Returns the list it was given, unchanged,
    /// when nothing in the folder has a rating — that is the common case,
    /// and it lets the caller skip the whole UI pass rather than reconcile
    /// a list against an identical copy of itself.
    ///
    /// <para>
    /// Cheap by construction: only rows that already carry a companion are
    /// touched, so a folder with no sidecars costs no I/O at all, and a
    /// folder of RAW files costs one small text read per photo. This is
    /// meant to run on a worker thread after the listing has landed, not as
    /// part of it — the listing must not wait on it.
    /// </para>
    /// </summary>
    public IReadOnlyList<FileSystemEntry> WithRatings(
        IReadOnlyList<FileSystemEntry> entries, CancellationToken ct = default) {
        List<FileSystemEntry>? rated = null;

        for (int i = 0; i < entries.Count; i++) {
            ct.ThrowIfCancellationRequested();

            var rating = ReadRatingFor(entries[i]);
            if (rating is null) {
                rated?.Add(entries[i]);
                continue;
            }

            rated ??= new List<FileSystemEntry>(entries.Take(i));
            rated.Add(entries[i] with { Rating = rating });
        }

        return rated ?? entries;
    }


    /// <summary>What a sidecar of this format would be called next to <paramref name="mainPath"/>.</summary>
    public string SidecarPathFor(string mainPath, SidecarFormat format) {
        string suffix = format.Suffix();
        var rule = _companions.Rules.FirstOrDefault(
            r => r.Suffix.Equals(suffix, StringComparison.OrdinalIgnoreCase))
            ?? throw new NotSupportedException($"No companion rule for {suffix}");

        string directory = Path.GetDirectoryName(mainPath) ?? "";

        return Path.Combine(directory, rule.CompanionNameFor(Path.GetFileName(mainPath)));
    }


    /// <summary>
    /// Brings a sidecar into existence for a photo that has none, carrying
    /// one rating field. Returns the path it created.
    ///
    /// <para>
    /// This is the one place in Wander that creates a file the user did not
    /// name, so the rules around it are strict: the file must not already
    /// exist (an existing one is an edit, which is
    /// <see cref="SetRating"/>'s job), the creation is logged, and undo
    /// removes the file rather than blanking it — undoing "give this photo
    /// a star" has to leave the folder exactly as it was, and a leftover
    /// empty <c>.pp3</c> would not.
    /// </para>
    ///
    /// <para>
    /// Whether the user <em>wants</em> a file created is not decided here;
    /// the caller asks first. See <see cref="SidecarFormat.Pp3"/> for why
    /// that question is a real one and not a formality.
    /// </para>
    /// </summary>
    public string CreateRatingSidecar(string mainPath, SidecarFormat format, RatingField field, int value) {
        string path = SidecarPathFor(mainPath, format);
        if (_fs.FileExists(path)) {
            throw new InvalidOperationException($"Sidecar already exists: {path}");
        }

        // The guard is a deny-list for destructive work and creating a file
        // is not that — but this is the one path in Wander that puts a file
        // somewhere the user did not name, and the Windows tree is exactly
        // where it must not do so. Wallpapers live there, and they are
        // pictures.
        if (SystemPathGuard.IsProtected(path, out string reason)) {
            throw new InvalidOperationException(reason);
        }

        int rank = field == RatingField.Rank ? value : 0;
        int color = field == RatingField.ColorLabel ? value : 0;

        byte[] content = format == SidecarFormat.Pp3
            ? Pp3Sidecar.Create(rank, color)
            : XmpSidecar.Create(rank, color);

        _fs.ReplaceAtomic(path, content);
        _log.Info($"Sidecar created: {path} ({field} = {value})");
        _undo.Push(new SidecarCreatedAction(this, path));

        return path;
    }


    /// <summary>Undo of a creation: the file goes away again.</summary>
    internal void DeleteSidecar(string path) {
        if (!_fs.FileExists(path)) {
            return;
        }

        _fs.DeleteFile(path);
        _log.Info($"Sidecar removed: {path}");
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
/// Undo of creating a sidecar — delete the file that was created. Nothing
/// is kept from it: it held one rating and nothing else, which is exactly
/// what makes throwing it away the honest inverse.
/// </summary>
public sealed record SidecarCreatedAction(CompanionMetadataService Service, string Path) : IUndoableAction {
    public string Description => $"Sidecar '{System.IO.Path.GetFileName(Path)}'";


    public void Undo() {
        Service.DeleteSidecar(Path);
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
