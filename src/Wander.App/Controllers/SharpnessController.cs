using System.Windows.Threading;
using Wander.Core.Diagnostics;
using Wander.Core.FileSystem;
using Wander.Core.Icons;
using Wander.Core.Listing;
using Wander.Core.Logging;

namespace Wander.App.Controllers;

/// <summary>
/// How sharp each photograph of the gallery is (RAWHELPERS, step 6): the
/// number in the corner of a cell, the same one the preview pane shows for
/// the picture it is on.
///
/// <para>
/// Scored on demand, a cell at a time: the cells ask as they come on screen
/// (<c>ReviewThumb</c>), so a folder of three hundred RAW files costs the
/// screenful being looked at rather than all of it. The probe remembers what
/// it measured, so scrolling back costs a lookup.
/// </para>
///
/// <para>
/// They reach the rows in bursts rather than one by one - a row replaced is
/// a row rebuilt - and every listing published afterwards is put through
/// <see cref="Decorate"/>: the ratings pass and the folder's own re-listing
/// publish rows made before the scores existed, and would otherwise wash
/// them out.
/// </para>
/// </summary>
public sealed class SharpnessController {
    /// <summary>How many files are measured at once. The disk is shared with the thumbnails.</summary>
    private const int Parallelism = 2;

    private readonly ISharpnessProbe? _probe;
    private readonly ILogger _log;
    private readonly Func<int, bool> _isCurrent;
    private readonly Func<IReadOnlyList<FileSystemEntry>> _rows;
    private readonly Action<int, IReadOnlyList<FileSystemEntry>> _publish;
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly SemaphoreSlim _gate = new(Parallelism);

    // What the rows on screen are to say, and what has already been asked
    // about, both for the folder listed now. UI thread only.
    private readonly HashSet<string> _asked = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, (FileStamp Stamp, double Value)> _shown = new(StringComparer.OrdinalIgnoreCase);

    private (string Path, int Epoch)? _listing;
    private CancellationTokenSource? _pass;
    private bool _active;
    private int _publishPending;


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


    /// <summary>A folder was listed: what was scored for the one before is not about these rows.</summary>
    public void Listed(string path, int epoch) {
        bool sameFolder = _listing is { } previous
            && string.Equals(previous.Path, path, StringComparison.OrdinalIgnoreCase);
        Cancel();
        _listing = (path, epoch);
        if (!sameFolder) {
            _shown = new Dictionary<string, (FileStamp, double)>(StringComparer.OrdinalIgnoreCase);
        }
    }


    /// <summary>
    /// Whether the score is wanted at all - the sharpness helper. Switched
    /// off it takes the numbers off the rows; switched on, the cells on
    /// screen ask for themselves.
    /// </summary>
    public void SetActive(bool active) {
        if (_active == active) {
            return;
        }

        _active = active;
        if (active) {
            return;
        }

        Cancel();
        if (_shown.Count == 0) {
            return;
        }

        _shown = new Dictionary<string, (FileStamp, double)>(StringComparer.OrdinalIgnoreCase);
        if (_listing is { } listing && _isCurrent(listing.Epoch)) {
            _publish(listing.Epoch, SharpListing.WithScores(_rows(), _ => null));
        }
    }


    /// <summary>A cell on screen wants its frame's score. Cheap to call again for one already asked about.</summary>
    public void Want(FileSystemEntry entry) {
        if (_probe is null || !_active || _listing is not { } listing) {
            return;
        }
        if (_shown.ContainsKey(entry.FullPath) || !_asked.Add(entry.FullPath)) {
            return;
        }

        _pass ??= new CancellationTokenSource();
        _ = ScoreAsync(entry, listing.Epoch, _pass.Token);
    }


    /// <summary>Drops what is in flight: the folder was left, or search results took the list.</summary>
    public void Cancel() {
        _pass?.Cancel();
        _pass = null;
        _asked.Clear();
    }


    /// <summary>
    /// The rows with the scores already found for them - by path, and only
    /// while the file is the one that was scored. The same list when there
    /// is nothing to add.
    /// </summary>
    public IReadOnlyList<FileSystemEntry> Decorate(IReadOnlyList<FileSystemEntry> items) {
        return _shown.Count == 0 ? items : SharpListing.WithScores(items, Shown);
    }


    private async Task ScoreAsync(FileSystemEntry entry, int epoch, CancellationToken ct) {
        double? score;
        try {
            await _gate.WaitAsync(ct);
            try {
                score = await Task.Run(() => Measure(entry), ct);
            } finally {
                _gate.Release();
            }
        } catch (OperationCanceledException) {
            return;
        } catch (Exception ex) {
            _log.Warn($"Sharpness failed: {entry.FullPath} ({ex.Message})");

            return;
        }

        if (ct.IsCancellationRequested || !_isCurrent(epoch) || score is not { } value) {
            return;
        }

        _shown[entry.FullPath] = (FileStamp.Of(entry.ModifiedUtc, entry.Size), value);
        SchedulePublish(epoch);
    }


    /// <summary>
    /// The rows are handed over once per quiet moment, not once per answer:
    /// a screenful arriving one at a time would rebuild a row and re-run the
    /// filter thirty times over.
    /// </summary>
    private void SchedulePublish(int epoch) {
        if (Interlocked.Exchange(ref _publishPending, 1) != 0) {
            return;
        }

        _dispatcher.BeginInvoke(DispatcherPriority.Background, () => {
            Interlocked.Exchange(ref _publishPending, 0);
            if (_isCurrent(epoch)) {
                _publish(epoch, SharpListing.WithScores(_rows(), Shown));
            }
        });
    }


    /// <summary>The score of one file. The probe remembers what it measured, so a file is measured once a session. Any thread.</summary>
    private double? Measure(FileSystemEntry entry) {
        using var pass = PerfLog.Measure("bg.sharpness");

        return _probe!.Score(entry.FullPath);
    }


    private double? Shown(FileSystemEntry entry) {
        return _shown.TryGetValue(entry.FullPath, out var shown)
            && shown.Stamp == FileStamp.Of(entry.ModifiedUtc, entry.Size)
                ? shown.Value
                : null;
    }
}
