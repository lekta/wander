using System.Windows.Threading;
using Wander.Core.Diagnostics;
using Wander.Core.FileSystem;
using Wander.Core.Icons;
using Wander.Core.Imaging;
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
/// it measured, so scrolling back costs a lookup. Of the cells waiting, the
/// host's rank decides who goes first.
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
    private readonly RankedGate _gate;

    // What the rows on screen are to say, and what has already been asked
    // about - the file as it was when asked - both for the folder listed
    // now. UI thread only.
    private readonly Dictionary<string, FileStamp> _asked = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, (FileStamp Stamp, double Value)> _shown = new(StringComparer.OrdinalIgnoreCase);

    private (string Path, int Epoch)? _listing;
    private CancellationTokenSource? _pass;
    private bool _active;
    private int _publishPending;


    /// <param name="isCurrent">Whether an epoch is still the listing on screen.</param>
    /// <param name="rows">The rows on screen now - what the answers are put into.</param>
    /// <param name="publish">Hands a set of rows to the list, epoch and all.</param>
    /// <param name="rank">Which files are measured first, lowest rank first.</param>
    public SharpnessController(
        ISharpnessProbe? probe,
        ILogger log,
        Func<int, bool> isCurrent,
        Func<IReadOnlyList<FileSystemEntry>> rows,
        Action<int, IReadOnlyList<FileSystemEntry>> publish,
        Func<string, int> rank) {
        _gate = new RankedGate(Parallelism, rank);
        _probe = probe;
        _log = log;
        _isCurrent = isCurrent;
        _rows = rows;
        _publish = publish;
    }


    /// <summary>
    /// A folder was listed. Another one: what was scored and asked about is
    /// not about these rows. The same one again - a re-read after a change
    /// in it, F5 - keeps all of it, and what is being measured goes on: an
    /// answer is about a file and its stamp, not about a listing, and a cell
    /// whose row came back unchanged does not ask again. Cancelled here, its
    /// number never came (2026-10-08).
    /// </summary>
    public void Listed(string path, int epoch) {
        bool sameFolder = _listing is { } previous
            && string.Equals(previous.Path, path, StringComparison.OrdinalIgnoreCase);
        _listing = (path, epoch);
        if (sameFolder) {
            return;
        }

        Cancel();
        _shown = new Dictionary<string, (FileStamp, double)>(StringComparer.OrdinalIgnoreCase);
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
        if (_probe is null || !_active || _listing is null || Shown(entry) is not null) {
            return;
        }

        // Asked about this file as it is now: in flight, or measured with
        // nothing to say. A file rewritten since is asked about again.
        var stamp = FileStamp.Of(entry.ModifiedUtc, entry.Size);
        if (_asked.TryGetValue(entry.FullPath, out var asked) && asked == stamp) {
            return;
        }

        _asked[entry.FullPath] = stamp;
        _pass ??= new CancellationTokenSource();
        _ = ScoreAsync(entry, _pass.Token);
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


    private async Task ScoreAsync(FileSystemEntry entry, CancellationToken ct) {
        double? score;
        try {
            await _gate.EnterAsync(entry.FullPath, ct);
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

        // Not checked against the listing it was asked under: the folder
        // listed again since is the same files, and Shown holds each answer
        // to the stamp of its row. Another folder cancelled the pass.
        if (ct.IsCancellationRequested || score is not { } value) {
            return;
        }

        _shown[entry.FullPath] = (FileStamp.Of(entry.ModifiedUtc, entry.Size), value);
        SchedulePublish();
    }


    /// <summary>
    /// The rows are handed over once per quiet moment, not once per answer:
    /// a screenful arriving one at a time would rebuild a row and re-run the
    /// filter thirty times over. To the listing on screen then; while one is
    /// still being read, the rows it lands with take the answers
    /// (<see cref="Decorate"/>).
    /// </summary>
    private void SchedulePublish() {
        if (Interlocked.Exchange(ref _publishPending, 1) != 0) {
            return;
        }

        _dispatcher.BeginInvoke(DispatcherPriority.Background, () => {
            Interlocked.Exchange(ref _publishPending, 0);
            if (_listing is { } listing && _isCurrent(listing.Epoch)) {
                _publish(listing.Epoch, SharpListing.WithScores(_rows(), Shown));
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
