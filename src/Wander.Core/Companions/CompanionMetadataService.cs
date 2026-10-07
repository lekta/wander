using Wander.Core.FileSystem;
using Wander.Core.Icons;
using Wander.Core.Logging;
using Wander.Core.Undo;

namespace Wander.Core.Companions;

/// <summary>Which of a sidecar's two rating fields an edit is about.</summary>
public enum RatingField {
    Rank,
    ColorLabel,
}


/// <summary>What setting a rating field on a photo comes down to - see <see cref="CompanionMetadataService.PlanWrite"/>.</summary>
public enum RatingWrite {
    /// <summary>Nothing to write: not a picture, or clearing what nothing holds.</summary>
    None,

    /// <summary>Into the sidecars the photo has.</summary>
    Edit,

    /// <summary>A sidecar has to be created first - the caller asks.</summary>
    Create,
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
    private readonly EmbeddedRatingCache _photoRatings = new();


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


    /// <summary>
    /// The rating sidecars among <paramref name="companions"/>, in the order
    /// their ratings count (decision 2026-10-01): the editor's own, named
    /// after the whole file - darktable IMG.CR2.xmp, RawTherapee
    /// IMG.CR2.pp3 - before the neutral IMG.xmp, which anybody may have
    /// written. Two of the same kind keep the order given. darktable's
    /// duplicates (IMG_01.CR2.xmp) travel with the photo but are not among
    /// them: the stars are the original's.
    /// </summary>
    public IReadOnlyList<string> RatingSidecars(string mainName, IReadOnlyList<string>? companions) {
        if (companions is not { Count: > 0 }) {
            return Array.Empty<string>();
        }

        return companions
            .Where(IsRatingSidecar)
            .Select(path => (Path: path, Rule: _companions.RuleFor(path, mainName)))
            .Where(c => c.Rule is not null)
            .OrderBy(c => c.Rule!.Naming == CompanionNaming.Appended ? 0 : 1)
            .Select(c => c.Path)
            .ToArray();
    }


    /// <summary>Rating held by a <c>.pp3</c> or <c>.xmp</c>, or null when it can't be read.</summary>
    public SidecarRating? ReadRating(string path) {
        if (!IsRatingSidecar(path)) {
            return null;
        }

        return TryRead(path, bytes => IsPp3(path) ? Pp3Sidecar.Read(bytes) : XmpSidecar.Read(bytes));
    }


    /// <summary>
    /// The rating a row shows: the first of <see cref="RatingSidecars"/> -
    /// the same one the preview pane shows - over what the photo itself
    /// says (<see cref="EmbeddedRating.Merge"/>). Null when neither says
    /// anything.
    /// </summary>
    public SidecarRating? ReadRatingFor(FileSystemEntry entry) {
        var sidecars = RatingSidecars(entry.Name, entry.Companions);

        return EmbeddedRating.Merge(sidecars.Count > 0 ? ReadRating(sidecars[0]) : null, PhotoRating(entry));
    }


    /// <summary>
    /// The rating written into the photo itself (<see cref="EmbeddedRating"/>),
    /// from the cache when the file has not changed since; null for a file
    /// that cannot carry one, says nothing, or cannot be read right now.
    /// </summary>
    public SidecarRating? PhotoRating(FileSystemEntry photo) {
        if (photo.IsFolderLike || !EmbeddedRating.Reads(photo.Name)) {
            return null;
        }
        if (_photoRatings.TryGet(photo, out var known)) {
            return known;
        }

        SidecarRating? rating;
        try {
            using var stream = _fs.OpenRead(photo.FullPath);
            rating = EmbeddedRating.Read(stream);
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException) {
            // Being copied in, or not a plain file (an archive entry, the
            // bin): not cached, the next pass asks again.
            return null;
        } catch (Exception ex) {
            // A header it cannot make out says nothing, and must not take
            // the folder's rating pass down with it.
            _log.Warn($"Photo rating unreadable: {photo.FullPath} ({ex.Message})");
            rating = null;
        }
        _photoRatings.Put(photo, rating);

        return rating;
    }


    /// <summary>The watcher saw this file change: what was read out of it is stale.</summary>
    public void ForgetPhotoRating(string path) {
        _photoRatings.Forget(path);
    }


    /// <summary>F5: the folder is read again from the disk, the photos' own ratings with it.</summary>
    public void ForgetPhotoRatings(string folder) {
        _photoRatings.ForgetFolder(folder);
    }


    /// <summary>
    /// What setting <paramref name="field"/> to <paramref name="value"/> on
    /// this photo would do. A photo with a rating sidecar is edited. One
    /// without gets a sidecar created - when it is a picture, and when there
    /// is something to say: a value, or a 0 over the photo's own (decision
    /// 2026-10-01: Wander does not write into the RAW, a sidecar with 0 is
    /// how the camera's stars come off). A sidecar created to record "no
    /// stars" over nothing is the file nobody wanted.
    /// </summary>
    public RatingWrite PlanWrite(FileSystemEntry photo, RatingField field, int value) {
        if (photo.IsFolderLike) {
            return RatingWrite.None;
        }
        if (RatingSidecars(photo.Name, photo.Companions).Count > 0) {
            return RatingWrite.Edit;
        }
        if (!ImageFormats.IsImage(photo.Name)) {
            return RatingWrite.None;
        }
        if (value > 0) {
            return RatingWrite.Create;
        }

        var own = PhotoRating(photo);
        int? held = field == RatingField.Rank ? own?.Rank : own?.ColorLabel;

        return held > 0 ? RatingWrite.Create : RatingWrite.None;
    }


    /// <summary>What a sidecar of this format would be called next to <paramref name="mainPath"/>.</summary>
    public string SidecarPathFor(string mainPath, SidecarFormat format) {
        string suffix = format.Suffix();
        var rule = _companions.Rules.FirstOrDefault(
            r => r.Suffix.Equals(suffix, StringComparison.OrdinalIgnoreCase) && r.Naming == format.Naming())
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
        return CreateRatingSidecar(
            mainPath, format, field == RatingField.Rank ? value : 0, field == RatingField.ColorLabel ? value : 0,
            $"{field} = {value}", pushUndo: true);
    }


    /// <summary>
    /// Sets one rating field on many photos at once — writing into the
    /// sidecars that exist and creating the ones that do not — and puts the
    /// whole thing on the undo stack as <b>one</b> step.
    ///
    /// <para>
    /// One step is the point. Rating five selected photos with a keypress
    /// is one gesture, and five presses of <c>Ctrl</c> + <c>Z</c> to take
    /// it back would be five answers to a question the user asked once.
    /// Creation and editing land in the same composite for the same reason:
    /// the user did not distinguish them.
    /// </para>
    ///
    /// <para>
    /// A photo with two rating sidecars - the neutral IMG.xmp and the one
    /// RawTherapee or darktable added - has the field written into every
    /// one that holds it (decision 2026-10-01), so the two editors keep
    /// agreeing; none holding it, the first of <see cref="RatingSidecars"/>.
    /// A created sidecar takes the photo's own other field along: a label
    /// put on a frame the camera gave five stars must not take the stars
    /// off with the new file's 0.
    /// </para>
    ///
    /// <para>
    /// Whether the ones needing a new file may have one is <em>not</em>
    /// decided here — the caller asks first (<see cref="PlanWrite"/> says
    /// which) and simply leaves out the photos it was told no about. See
    /// <see cref="SidecarFormat.Pp3"/> for why that question is a real one.
    /// </para>
    ///
    /// <para>
    /// A photo whose write fails is skipped and logged rather than taking
    /// the rest down with it: half a folder rated is better than a batch
    /// that stops on the one read-only file in it.
    /// </para>
    /// </summary>
    /// <param name="failed">
    /// Told about each photo whose write failed, with why - the caller
    /// says so to the user; the result list only has the ones that went.
    /// </param>
    public IReadOnlyList<RatingResult> ApplyRatingToMany(
        IReadOnlyList<FileSystemEntry> photos, RatingField field, int value, SidecarFormat createFormat,
        Action<string, Exception>? failed = null) {
        var steps = new List<IUndoableAction>();
        var results = new List<RatingResult>();
        var touched = new List<string>();

        foreach (var photo in photos) {
            var plan = PlanWrite(photo, field, value);
            if (plan == RatingWrite.None) {
                continue;
            }

            try {
                var sidecars = RatingSidecars(photo.Name, photo.Companions);
                if (plan == RatingWrite.Edit) {
                    foreach (string path in Holding(sidecars, field)) {
                        int previous = ApplyRating(path, field, value);
                        steps.Add(new SidecarRatingAction(this, path, field, previous, value, photo.FullPath));
                    }
                } else {
                    var own = PhotoRating(photo);
                    string created = CreateRatingSidecar(
                        photo.FullPath, createFormat,
                        field == RatingField.Rank ? value : own?.Rank ?? 0,
                        field == RatingField.ColorLabel ? value : own?.ColorLabel ?? 0,
                        $"{field} = {value}", pushUndo: false);
                    steps.Add(new SidecarCreatedAction(this, created, photo.FullPath));
                    sidecars = new[] { created };
                }
                touched.Add(photo.Name);

                var rating = EmbeddedRating.Merge(ReadRating(sidecars[0]), PhotoRating(photo));
                if (rating is not null) {
                    results.Add(new RatingResult(photo.FullPath, sidecars[0], rating));
                }
            } catch (Exception ex) {
                _log.Warn($"Rating {field}={value} failed for {photo.FullPath}: {ex.Message}");
                failed?.Invoke(photo.FullPath, ex);
            }
        }

        if (steps.Count == 1) {
            _undo.Push(steps[0]);
        } else if (steps.Count > 1) {
            string what = field == RatingField.Rank ? $"Rating {value}" : $"Colour {ColorLabels.Name(value)}";
            string on = touched.Count == 1 ? $"'{touched[0]}'" : $"{touched.Count} items";
            _undo.Push(new CompositeAction($"{what} on {on}", steps));
        }

        return results;
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


    /// <summary>
    /// Sets a rating field in an existing sidecar and makes it undoable.
    ///
    /// <para>
    /// Internal: the app writes ratings through
    /// <see cref="ApplyRatingToMany"/>, which is the same thing for one file
    /// and the only thing that works for several. This stays as the step
    /// that one is built from, and as the narrowest surface the tests can
    /// aim at — the same arrangement the single-file operations on
    /// <c>FileOperationService</c> ended up in.
    /// </para>
    /// </summary>
    /// <param name="mainPath">
    /// The photograph this sidecar belongs to. Only undo uses it, and only
    /// to know which row to re-read; passing the sidecar itself is harmless
    /// but costs the caller a listing refresh it did not need.
    /// </param>
    internal void SetRating(string path, RatingField field, int value, string? mainPath = null) {
        int previous = ApplyRating(path, field, value);
        _undo.Push(new SidecarRatingAction(this, path, field, previous, value, mainPath ?? path));
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

        byte[] original = _fs.ReadAllBytesForUpdate(path);
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


    /// <param name="what">The edit asked for, for the log - the other field may have come from the photo.</param>
    /// <param name="pushUndo">
    /// False when the caller is assembling a composite of its own — see
    /// <see cref="ApplyRatingToMany"/>. The step still exists, it is just
    /// pushed by somebody else.
    /// </param>
    private string CreateRatingSidecar(
        string mainPath, SidecarFormat format, int rank, int color, string what, bool pushUndo) {
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

        byte[] content = format == SidecarFormat.Pp3
            ? Pp3Sidecar.Create(rank, color)
            : XmpSidecar.Create(rank, color);

        _fs.ReplaceAtomic(path, content);
        _log.Info($"Sidecar created: {path} ({what})");
        if (pushUndo) {
            _undo.Push(new SidecarCreatedAction(this, path, mainPath));
        }

        return path;
    }


    /// <summary>The sidecars that hold <paramref name="field"/>; none does - the first, as before there were two.</summary>
    private IReadOnlyList<string> Holding(IReadOnlyList<string> sidecars, RatingField field) {
        var holding = sidecars
            .Where(path => ReadRating(path) is { } rating && (field == RatingField.Rank ? rating.Rank : rating.ColorLabel) is not null)
            .ToArray();

        return holding.Length > 0 ? holding : new[] { sidecars[0] };
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


    /// <summary>
    /// The result for one photo: the first sidecar its rating now lives in,
    /// and what the row shows now - that sidecar over the photo's own.
    /// </summary>
    public readonly record struct RatingResult(string MainPath, string SidecarPath, SidecarRating Rating);
}


/// <summary>
/// Undo of creating a sidecar — delete the file that was created. Nothing
/// is kept from it: it held one rating and nothing else, which is exactly
/// what makes throwing it away the honest inverse.
/// </summary>
internal sealed record SidecarCreatedAction(
    CompanionMetadataService Service, string Path, string MainPath) : IUndoableAction {

    public string Description => $"Sidecar '{System.IO.Path.GetFileName(Path)}'";

    public IReadOnlyList<string> PathsBeforeUndo => new[] { Path };

    /// <summary>
    /// The photograph, not the file being deleted. Undoing this removes a
    /// sidecar that was folded into the photo's row, so that row — and only
    /// that row — has to be re-read.
    /// </summary>
    public IReadOnlyList<string> MetadataTargets => new[] { MainPath };


    public void Undo() {
        Service.DeleteSidecar(Path);
    }
}


/// <summary>
/// Undo of a rating change — put the old value back. Restoring is the same
/// guarded write as setting, so the undo itself stays atomic.
/// </summary>
internal sealed record SidecarRatingAction(
    CompanionMetadataService Service, string Path, RatingField Field, int OldValue, int NewValue, string MainPath)
    : IUndoableAction {

    public string Description =>
        Field == RatingField.Rank
            ? $"Rating {NewValue} on '{System.IO.Path.GetFileName(Path)}'"
            : $"Colour {ColorLabels.Name(NewValue)} on '{System.IO.Path.GetFileName(Path)}'";

    public IReadOnlyList<string> PathsBeforeUndo => new[] { Path };

    /// <summary>The photograph the sidecar belongs to — see <see cref="IUndoableAction.MetadataTargets"/>.</summary>
    public IReadOnlyList<string> MetadataTargets => new[] { MainPath };


    public void Undo() {
        Service.ApplyRating(Path, Field, OldValue);
    }
}
