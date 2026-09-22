using Wander.App.Resources;
using Wander.Core.Diagnostics;
using Wander.Core.FileSystem;
using Wander.Core.Icons;
using Wander.Core.Listing;
using Wander.Core.Logging;

namespace Wander.App.Controllers;

/// <summary>
/// "Sharp is lighter" in the gallery (RAWHELPERS, step 6): a second pass
/// over the photographs of the folder, like the ratings pass, that scores
/// each one's sharpness and hands the rows back with
/// <see cref="FileSystemEntry.Sharpness"/> filled in - the number in the
/// corner of every cell.
///
/// <para>
/// Runs only while it is asked for - the gallery on screen and the
/// sharpness helper on (<see cref="SetActive"/>): a folder of three hundred
/// RAW files is three hundred decodes, and nobody should pay them for a
/// table view. Scores are kept in memory by path and file stamp, so coming
/// back to a folder, or the folder being re-listed, costs lookups.
/// </para>
///
/// <para>
/// Published once, when the pass is over, rather than file by file: a row
/// replaced is a row rebuilt, and three hundred of them one at a time is
/// the list working all the way through the pass. Every listing published
/// afterwards is put through <see cref="Decorate"/>: the ratings pass and
/// the folder's own re-listing publish rows made before the scores existed,
/// and would otherwise wash them out.
/// </para>
/// </summary>
public sealed class SharpnessController {
    /// <summary>How many files are decoded at once. The disk is shared with the thumbnails.</summary>
    private const int Parallelism = 3;

    /// <summary>Scores kept in memory; past this the cache starts over.</summary>
    private const int CacheLimit = 2000;

    /// <summary>The status line is told every this many files, not on every one.</summary>
    private const int ReportEvery = 10;


    private readonly ISharpnessProbe? _probe;
    private readonly ILogger _log;
    private readonly Func<int, bool> _isCurrent;
    private readonly Func<IReadOnlyList<FileSystemEntry>> _rows;
    private readonly Action<int, IReadOnlyList<FileSystemEntry>> _publish;
    private readonly Lock _cacheLock = new();
    private readonly Dictionary<string, (FileStamp Stamp, double Score)> _cache = new(StringComparer.OrdinalIgnoreCase);

    // What the rows on screen are to say: the last finished pass's answers,
    // relative to its folder, with the stamp of the file each was taken of.
    private Dictionary<string, (FileStamp Stamp, double Value)> _shown = new(StringComparer.OrdinalIgnoreCase);

    // The listing on screen, remembered so switching the helper on can
    // start a pass without waiting for the folder to be listed again.
    private (IReadOnlyList<FileSystemEntry> Items, string Path, int Epoch)? _listing;
    private bool _active;
    private CancellationTokenSource? _pass;


    /// <param name="isCurrent">Whether an epoch is still the listing on screen.</param>
    /// <param name="rows">The rows on screen now - what the answers are put into.</param>
    /// <param name="publish">Hands a set of rows to the list, epoch and all.</param>
    public SharpnessController(
        ISharpnessProbe? probe,
        ILogger log,
        Func<int, bool> isCurrent,
        Func<IReadOnlyList<FileSystemEntry>> rows,
        Action<int, IReadOnlyList<FileSystemEntry>> publish) {
        _probe = probe;
        _log = log;
        _isCurrent = isCurrent;
        _rows = rows;
        _publish = publish;
    }


    /// <summary>How far the pass has got, for the status line - already localised.</summary>
    public event EventHandler<string>? StatusReported;


    /// <summary>
    /// A folder was listed. Remembered either way; scored now when the pass
    /// is wanted. Cancels the pass for the listing before.
    /// </summary>
    public void StartPass(IReadOnlyList<FileSystemEntry> items, string path, int epoch) {
        Cancel();
        _listing = (items, path, epoch);
        if (_active) {
            Run(items, path, epoch);
        }
    }


    /// <summary>
    /// Whether the pass is wanted: the gallery is on screen and the
    /// sharpness helper is on. Switching it off drops the pass and takes
    /// the scores off the rows.
    /// </summary>
    public void SetActive(bool active) {
        if (_active == active) {
            return;
        }

        _active = active;
        if (active) {
            if (_listing is { } listing && _isCurrent(listing.Epoch)) {
                Run(listing.Items, listing.Path, listing.Epoch);
            }

            return;
        }

        Cancel();
        if (_shown.Count == 0) {
            return;
        }

        _shown = new Dictionary<string, (FileStamp, double)>(StringComparer.OrdinalIgnoreCase);
        if (_listing is { } shown && _isCurrent(shown.Epoch)) {
            _publish(shown.Epoch, SharpListing.WithScores(_rows(), _ => null));
        }
    }


    /// <summary>Drops the pass in flight: the folder was left, or search results took the list.</summary>
    public void Cancel() {
        _pass?.Cancel();
        _pass = null;
    }


    /// <summary>
    /// The rows with the scores the last pass found for them - by path, and
    /// only while the file is the one that was scored. The same list when
    /// there is nothing to add.
    /// </summary>
    public IReadOnlyList<FileSystemEntry> Decorate(IReadOnlyList<FileSystemEntry> items) {
        return _shown.Count == 0 ? items : SharpListing.WithScores(items, Shown);
    }


    private void Run(IReadOnlyList<FileSystemEntry> items, string path, int epoch) {
        var shots = items
            .Where(e => e.Kind == EntryKind.File && !e.IsFolderLike && ImageFormats.IsImage(e.Name))
            .ToArray();
        if (_probe is null || shots.Length == 0) {
            return;
        }

        _pass = new CancellationTokenSource();
        _ = RunPassAsync(shots, path, epoch, _pass.Token);
    }


    private async Task RunPassAsync(FileSystemEntry[] shots, string path, int epoch, CancellationToken token) {
        // Built here, on the UI thread, so the reports come back to it.
        var progress = new Progress<int>(done => {
            if (!token.IsCancellationRequested) {
                StatusReported?.Invoke(this, string.Format(Strings.StatusSharpness, done, shots.Length));
            }
        });

        var scored = new Dictionary<string, (FileStamp Stamp, double Value)>(StringComparer.OrdinalIgnoreCase);
        int done = 0;
        try {
            await Parallel.ForEachAsync(
                shots,
                new ParallelOptions { MaxDegreeOfParallelism = Parallelism, CancellationToken = token },
                (shot, _) => {
                    if (ScoreOf(shot) is { } score) {
                        lock (scored) {
                            scored[shot.FullPath] = (FileStamp.Of(shot.ModifiedUtc, shot.Size), score);
                        }
                    }
                    int count = Interlocked.Increment(ref done);
                    if (count % ReportEvery == 0) {
                        ((IProgress<int>)progress).Report(count);
                    }

                    return ValueTask.CompletedTask;
                });
        } catch (OperationCanceledException) {
            return;
        } catch (Exception ex) {
            _log.Warn($"Sharpness pass failed: {path} ({ex.Message})");

            return;
        }

        if (token.IsCancellationRequested || !_isCurrent(epoch)) {
            return;
        }

        _shown = scored;
        _publish(epoch, SharpListing.WithScores(_rows(), Shown));
        StatusReported?.Invoke(this, string.Format(Strings.StatusSharpness, shots.Length, shots.Length));
    }


    /// <summary>The raw score of one file: from the cache while the file is unchanged, else measured. Any thread.</summary>
    private double? ScoreOf(FileSystemEntry shot) {
        var stamp = FileStamp.Of(shot.ModifiedUtc, shot.Size);
        lock (_cacheLock) {
            if (_cache.TryGetValue(shot.FullPath, out var cached) && cached.Stamp == stamp) {
                return cached.Score;
            }
        }

        double? score;
        using (PerfLog.Measure("bg.sharpness")) {
            score = _probe!.Score(shot.FullPath);
        }
        if (score is { } measured) {
            lock (_cacheLock) {
                if (_cache.Count >= CacheLimit) {
                    _cache.Clear();
                }
                _cache[shot.FullPath] = (stamp, measured);
            }
        }

        return score;
    }


    private double? Shown(FileSystemEntry entry) {
        return _shown.TryGetValue(entry.FullPath, out var shown)
            && shown.Stamp == FileStamp.Of(entry.ModifiedUtc, entry.Size)
                ? shown.Value
                : null;
    }
}
