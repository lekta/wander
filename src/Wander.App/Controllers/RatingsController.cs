using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Wander.App.Resources;
using Wander.App.ViewModels;
using Wander.Core.Companions;
using Wander.Core.Diagnostics;
using Wander.Core.FileSystem;
using Wander.Core.Icons;
using Wander.Core.Listing;
using Wander.Core.Logging;

namespace Wander.App.Controllers;

/// <summary>
/// Stars and colour labels: reading them off the sidecars beside the photos,
/// writing them back, and keeping the rows on screen in step with both.
///
/// <para>
/// Reading is a second pass on purpose. A folder of five hundred RAW files
/// is five hundred small reads, and making the folder appear is worth more
/// than making it appear complete — the rows are already on screen when the
/// ratings land, and only the ones that gained a rating are replaced.
/// </para>
///
/// <para>
/// Writing never re-lists. That is a requirement, not an optimisation:
/// writing a star used to rebuild the rows, take the selection with them,
/// re-sort a rating-sorted grid and move the picture out from under the
/// cursor — for a change the user made to one number on one file.
/// </para>
/// </summary>
public sealed class RatingsController {
    private readonly IFileSystem _fs;
    private readonly CompanionResolver _companions;
    private readonly CompanionMetadataService? _metadata;
    private readonly SearchController _search;
    private readonly SettingsViewModel _settings;
    private readonly ILogger _log;
    private readonly Func<int, bool> _isCurrent;
    private readonly Action<int, IReadOnlyList<FileSystemEntry>> _publish;
    private readonly Func<string, bool> _ask;
    private CancellationTokenSource? _pass;


    /// <param name="isCurrent">
    /// Whether an epoch is still the listing on screen. Asked when the
    /// background pass comes back, because by then the folder may have been
    /// left, refreshed, or replaced by search results.
    /// </param>
    /// <param name="publish">Hands a computed set of rows to the list, epoch and all.</param>
    /// <param name="ask">
    /// Puts a yes/no question to the user, Cancel-default. A delegate rather
    /// than a message box in here: what to ask is this class's business,
    /// how to ask it is the window's.
    /// </param>
    public RatingsController(
        IFileSystem fs,
        CompanionResolver companions,
        CompanionMetadataService? metadata,
        SearchController search,
        SettingsViewModel settings,
        ILogger log,
        Func<int, bool> isCurrent,
        Action<int, IReadOnlyList<FileSystemEntry>> publish,
        Func<string, bool> ask) {
        _fs = fs;
        _companions = companions;
        _metadata = metadata;
        _search = search;
        _settings = settings;
        _log = log;
        _isCurrent = isCurrent;
        _publish = publish;
        _ask = ask;
    }


    /// <summary>Whether this folder has anything rated — the filter bar hangs off it.</summary>
    public event EventHandler<bool>? HasRatingsChanged;

    /// <summary>Something to tell the user — already localised.</summary>
    public event EventHandler<string>? StatusReported;

    /// <summary>A row's companions changed, so the preview's footer is stale.</summary>
    public event EventHandler? CompanionsChanged;


    /// <summary>
    /// Starts reading the ratings for a freshly listed folder. Cancels the
    /// pass for the folder being left: its answer is about rows that are no
    /// longer on screen.
    /// </summary>
    /// <param name="arriving">
    /// Whether this is a folder the user walked into, as opposed to the one
    /// already on screen being re-listed. It decides when the filter bar is
    /// allowed to disappear: walking into a new folder, nothing is known
    /// about it yet and the bar goes until the pass answers. Re-listing the
    /// folder the user is standing in — F5, a rename, a file operation —
    /// the answer from a moment ago is still the right one, and blinking the
    /// bar out and back in is the list jumping for no reason the user caused.
    /// </param>
    public void StartPass(
        IReadOnlyList<FileSystemEntry> items, string path, SortOptions sort, int epoch, bool arriving) {
        Cancel();

        bool willRun = _metadata is not null && items.Any(e => e.HasCompanions);
        if (arriving || !willRun) {
            HasRatingsChanged?.Invoke(this, false);
        }

        if (!willRun) {
            return;
        }

        _pass = new CancellationTokenSource();
        _ = RunPassAsync(items, path, sort, epoch, _pass.Token);
    }


    /// <summary>Drops the pass in flight. Called when the folder is left.</summary>
    public void Cancel() {
        _pass?.Cancel();
        _pass = null;
    }


    /// <summary>
    /// Sets a rating on one row and answers with what it ended up being —
    /// the preview footer's stars call this and redraw from the answer.
    /// </summary>
    public SidecarRating? ApplyToPrimary(FileSystemEntry entry, RatingField field, int value) {
        var results = Apply(new[] { entry }, field, value);

        return results.Count > 0 ? results[0].Rating : entry.Rating;
    }


    /// <summary>
    /// The single place a rating is written. Sorts the targets into "already
    /// has a sidecar" and "would need one", asks about the second group
    /// <b>once</b>, hands the lot to Core as one undoable step, and then
    /// updates the affected rows in place.
    ///
    /// <para>
    /// The order does not change either, even when the list is sorted by
    /// rating. Re-sorting under the cursor is precisely the jump this
    /// avoids; the new order arrives with the next listing.
    /// </para>
    /// </summary>
    public IReadOnlyList<CompanionMetadataService.RatingResult> Apply(
        IReadOnlyList<FileSystemEntry> entries, RatingField field, int value) {
        var empty = Array.Empty<CompanionMetadataService.RatingResult>();
        if (_metadata is null || entries.Count == 0) {
            return empty;
        }

        var targets = new List<CompanionMetadataService.RatingTarget>();
        var wouldNeedSidecar = new List<FileSystemEntry>();

        foreach (var entry in entries) {
            if (entry.IsFolderLike) {
                continue;
            }
            if (RatingSidecarOf(entry) is { } sidecar) {
                targets.Add(new CompanionMetadataService.RatingTarget(entry.FullPath, sidecar));
                continue;
            }
            if (ImageFormats.IsImage(entry.Name)) {
                wouldNeedSidecar.Add(entry);
            }
        }

        // Clearing a rating never brings a file into existence: a sidecar
        // created to record "no stars" is exactly the file nobody wanted.
        if (value > 0 && wouldNeedSidecar.Count > 0 && _ask(SidecarQuestion(wouldNeedSidecar))) {
            foreach (var entry in wouldNeedSidecar) {
                targets.Add(new CompanionMetadataService.RatingTarget(entry.FullPath, null));
            }
        }

        if (targets.Count == 0) {
            return empty;
        }

        var results = _metadata.ApplyRatingToMany(targets, field, value, _settings.RawRatingFormat);
        ApplyResults(results);

        return results;
    }


    /// <summary>
    /// Re-reads a few rows from disk — their sidecars and what those say —
    /// and swaps them in without touching the rest of the listing. The
    /// answer to an undo that changed metadata and nothing else.
    ///
    /// <para>
    /// The companion lookup goes to the disk once per row, so it runs on a
    /// worker: undoing a rating applied to two hundred photographs is two
    /// hundred directory probes, and the folder must not stop for them.
    /// </para>
    /// </summary>
    public async Task RefreshRowsAsync(IReadOnlyList<string> mainPaths) {
        if (_metadata is null || mainPaths.Count == 0) {
            return;
        }

        var rows = mainPaths
            .Select(FindInSource)
            .Where(r => r is not null)
            .Select(r => r!)
            .ToArray();
        if (rows.Length == 0) {
            return;
        }

        bool integrate = _settings.IntegrateCompanions;
        IReadOnlyList<FileSystemEntry> updated;
        try {
            updated = await Task.Run(() => rows.Select(row => {
                var companions = integrate
                    ? _companions.FindCompanions(row.FullPath, _fs)
                    : Array.Empty<string>();
                var refreshed = row with { Companions = companions.Count > 0 ? companions : null };

                return refreshed with { Rating = _metadata.ReadRatingFor(refreshed) };
            }).ToArray());
        } catch (Exception ex) {
            _log.Warn($"Metadata re-read failed: {ex.Message}");

            return;
        }

        _search.Replace(updated);
    }


    /// <summary>The row for this path in the listing behind the filter, or null.</summary>
    public FileSystemEntry? FindInSource(string path) {
        foreach (var entry in _search.Source) {
            if (PathsEqual(entry.FullPath, path)) {
                return entry;
            }
        }

        return null;
    }


    private async Task RunPassAsync(
        IReadOnlyList<FileSystemEntry> items, string path, SortOptions sort, int epoch,
        CancellationToken token) {
        IReadOnlyList<FileSystemEntry> rated;
        try {
            rated = await Task.Run(() => {
                using var pass = PerfLog.Measure("bg.ratings");
                var withRatings = RatedListing.WithRatings(items, _metadata!.ReadRatingFor, token);

                // Sorting by rating is the one key a directory scan cannot
                // answer, so the order it produced was a placeholder. Only
                // this key needs redoing; the other four were right the
                // first time.
                //
                // The name tiebreaker here is the ordinal one rather than
                // Explorer's natural order — it only decides between photos
                // with the same number of stars, and reaching the platform
                // comparer from up here would mean a new abstraction across
                // the whole filesystem interface for that.
                return sort.Key == SortKey.Rating
                    ? EntryComparers.Sort(withRatings, sort)
                    : withRatings;
            }, token);
        } catch (OperationCanceledException) {
            return;
        } catch (Exception ex) {
            _log.Warn($"Rating pass failed: {path} ({ex.Message})");

            return;
        }

        if (token.IsCancellationRequested || !_isCurrent(epoch)) {
            return;
        }

        HasRatingsChanged?.Invoke(this, rated.Any(e => e.Rating is not null));
        if (!ReferenceEquals(rated, items)) {
            _publish(epoch, rated);
        }
    }


    /// <summary>
    /// One question for the whole batch. Rating six selected photos is one
    /// gesture, and six dialogs would be six answers to a question the user
    /// asked once.
    ///
    /// <para>
    /// For a <c>.pp3</c> the question carries the part the user cannot be
    /// expected to know: RawTherapee applies its default processing profile
    /// only to photos <em>without</em> a sidecar, so the file about to be
    /// created changes how the photo opens there. That is the whole reason
    /// the format is a setting and its default is XMP.
    /// </para>
    /// </summary>
    private string SidecarQuestion(IReadOnlyList<FileSystemEntry> entries) {
        var format = _settings.RawRatingFormat;
        string question = entries.Count == 1
            ? string.Format(
                Strings.ConfirmCreateSidecar,
                Path.GetFileName(_metadata!.SidecarPathFor(entries[0].FullPath, format)),
                entries[0].Name)
            : string.Format(Strings.ConfirmCreateSidecarMany, entries.Count, format.Suffix());

        if (format == SidecarFormat.Pp3) {
            question += Environment.NewLine + Environment.NewLine + Strings.ConfirmCreateSidecarPp3Warning;
        }

        return question;
    }


    private void ApplyResults(IReadOnlyList<CompanionMetadataService.RatingResult> results) {
        if (results.Count == 0) {
            return;
        }

        var updated = new List<FileSystemEntry>(results.Count);
        foreach (var result in results) {
            if (FindInSource(result.MainPath) is not { } row) {
                continue;
            }

            var companions = row.Companions ?? Array.Empty<string>();
            if (!companions.Any(c => PathsEqual(c, result.SidecarPath))) {
                companions = companions.Append(result.SidecarPath).ToArray();
            }
            updated.Add(row with { Companions = companions, Rating = result.Rating });
        }

        if (updated.Count == 0) {
            return;
        }

        _search.Replace(updated);
        if (updated.Any(e => e.Rating is not null)) {
            HasRatingsChanged?.Invoke(this, true);
        }
        CompanionsChanged?.Invoke(this, EventArgs.Empty);

        int rank = results[0].Rating.Rank ?? 0;
        StatusReported?.Invoke(this, rank > 0
            ? string.Format(Strings.StatusRatingApplied, rank, results.Count)
            : string.Format(Strings.StatusRatingCleared, results.Count));
    }


    /// <summary>Path of the companion that holds this row's rating, or null when it has none.</summary>
    private static string? RatingSidecarOf(FileSystemEntry entry) {
        if (entry.Companions is not { Count: > 0 } companions) {
            return null;
        }

        foreach (string path in companions) {
            if (CompanionMetadataService.IsRatingSidecar(path)) {
                return path;
            }
        }

        return null;
    }


    private static bool PathsEqual(string? a, string? b) {
        return a is not null && b is not null && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }
}
