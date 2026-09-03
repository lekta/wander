using System.IO;
using System.Threading.Tasks;
using Wander.App.Resources;
using Wander.App.ViewModels;
using Wander.Core.FileSystem;
using Wander.Core.Logging;

namespace Wander.App.Conflict;

/// <summary>
/// The conflict window's state: one row per collision - nested under a
/// folder being merged - the two things that answer for whole groups of
/// them at once, and the background work that feeds the rows facts: the
/// reader that works out "are these two the same file?" and the walk that
/// finds what collides inside a merged folder.
///
/// <para>
/// The two group answers are deliberately different shapes. "Skip
/// identical" is a standing policy - a tick that keeps answering as
/// comparisons land, and lets go of its own answers when it is cleared.
/// "Apply to the rest" is a choice among four, taken once, over whatever is
/// open at that moment; it never overwrites an answer already given.
/// </para>
///
/// <para>
/// Every rule lives in <see cref="ConflictBatch"/>, which Core tests reach;
/// this class is the part that needs a dispatcher - it runs the reads off
/// the UI thread and pushes what they found back into the rows.
/// </para>
/// </summary>
public sealed class ConflictWindowViewModel : ObservableObject {
    private readonly IFileSystem _fs;
    private readonly ILogger _log;
    private readonly CancellationTokenSource _cts = new();
    private readonly Dictionary<ConflictPair, ConflictRowViewModel> _rowsByPair = new();

    private ConflictBulkAction _bulkAction = ConflictBulkAction.None;
    private bool _comparing;


    public ConflictWindowViewModel(ConflictBatch batch, IFileSystem fs, ILogger log) {
        Batch = batch;
        _fs = fs;
        _log = log;

        Rows = new BulkObservableCollection<ConflictRowViewModel>();
        RebuildRows();
    }


    public ConflictBatch Batch { get; }

    /// <summary>
    /// The pairs on screen, in tree order: what the batch asked about, and
    /// under every merged folder what was found inside it.
    /// </summary>
    public BulkObservableCollection<ConflictRowViewModel> Rows { get; }

    /// <summary>
    /// The verb, how much the batch carries and how much of it collided, in
    /// the title bar - the way the system's own copy-conflict dialog puts
    /// them. The two folders are not in here: they head the two columns
    /// instead, where each one sits over the side it belongs to.
    /// </summary>
    public string Title =>
        string.Format(Batch.IsMove ? Strings.ConflictTitleMove : Strings.ConflictTitleCopy, Batch.ItemCount, Batch.Count);

    /// <summary>
    /// Where the left column comes from. One folder in the usual case; a
    /// selection gathered from search results can span several, and then
    /// there is no one path to name.
    /// </summary>
    public string FromText {
        get {
            var folders = Batch.Roots
                .Select(p => Path.GetDirectoryName(p.Conflict.Source.FullPath) ?? "")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(2)
                .ToList();

            return folders.Count == 1
                ? string.Format(Strings.ConflictFrom, folders[0])
                : Strings.ConflictFromSeveral;
        }
    }

    /// <summary>Where the right column is - one folder for the whole batch.</summary>
    public string ToText =>
        string.Format(Strings.ConflictTo, Path.GetDirectoryName(Batch.Roots[0].Conflict.ExistingTarget.FullPath) ?? "");

    public string DecidedText => string.Format(Strings.ConflictDecided, Batch.DecidedCount, Batch.Effective().Count);

    /// <summary>OK is for a list with no open questions left in it.</summary>
    public bool CanAccept => Batch.AllDecided;

    /// <summary>
    /// The standing "they are the same file, keep what is there" policy.
    /// Starts from the user's setting and can be changed for this batch
    /// alone; the setting itself is not written back from here.
    /// </summary>
    public bool SkipIdentical {
        get => Batch.SkipIdentical;
        set {
            if (Batch.SkipIdentical == value) {
                return;
            }

            Refresh(Batch.SetSkipIdentical(value));
            Raise();
        }
    }

    /// <summary>
    /// One answer for everything still open, taken once. It stays selected
    /// as a record of what was applied; choosing another applies that one to
    /// whatever is open by then. A folder it set to merge is read like one
    /// the user ticked by hand.
    /// </summary>
    public ConflictBulkAction BulkAction {
        get => _bulkAction;
        set {
            _bulkAction = value;
            var changed = Batch.Apply(value);
            Refresh(changed);
            foreach (var pair in changed) {
                if (pair.IsMerging && pair.Scan == MergeScanState.NotScanned) {
                    StartScan(pair);
                }
            }
            RebuildRows();
            Raise();
        }
    }


    /// <summary>The window is on screen: start reading.</summary>
    public void Start() {
        EnsureComparing();
    }

    /// <summary>
    /// A row answered, or took its answer back. A folder switched to or
    /// from merge shows or hides what is inside it, and a merge nobody has
    /// read yet starts being read.
    /// </summary>
    public void OnChoiceChanged(ConflictRowViewModel row) {
        var pair = row.Pair;
        if (pair.IsFolderPair) {
            if (pair.IsMerging && pair.Scan == MergeScanState.NotScanned) {
                StartScan(pair);
            }
            RebuildRows();
        }

        OnAnswersChanged();
        EnsureComparing();
    }

    /// <summary>An answer was given or taken back; the footer and OK follow.</summary>
    public void OnAnswersChanged() {
        Raise(nameof(DecidedText));
        Raise(nameof(CanAccept));
    }

    /// <summary>The window is going away - stop reading files nobody waits for.</summary>
    public void Stop() {
        _cts.Cancel();
    }


    private void Refresh(IReadOnlyList<ConflictPair> changed) {
        foreach (var pair in changed) {
            RowOf(pair)?.RefreshChoice();
        }

        OnAnswersChanged();
    }

    private ConflictRowViewModel? RowOf(ConflictPair pair) {
        return _rowsByPair.TryGetValue(pair, out var row) ? row : null;
    }

    /// <summary>
    /// The rows are the effective pairs in tree order. Cached per pair, so a
    /// folder folded and unfolded again keeps the rows it had.
    /// </summary>
    private void RebuildRows() {
        var rows = new List<ConflictRowViewModel>();
        foreach (var pair in Batch.Effective()) {
            if (!_rowsByPair.TryGetValue(pair, out var row)) {
                row = new ConflictRowViewModel(this, pair);
                _rowsByPair[pair] = row;
            }
            rows.Add(row);
        }

        Rows.ReplaceAll(rows);
    }

    /// <summary>
    /// Reads whatever is worth reading, one pair at a time, until nothing
    /// is left - and picks up again whenever a merge brings new pairs in.
    /// Sequential on purpose: the pairs are on the same two volumes, and
    /// four parallel readers of 60 MB files make each other slower rather
    /// than the list faster. Small files go first (see
    /// <see cref="ConflictBatch.NextToCompare"/>).
    /// </summary>
    private async void EnsureComparing() {
        if (_comparing) {
            return;
        }

        _comparing = true;
        try {
            while (!_cts.IsCancellationRequested
                && Batch.NextToCompare(FileContentComparer.AutoCompareLimit) is { } pair) {
                await CompareAsync(pair).ConfigureAwait(true);
            }
        } catch (OperationCanceledException) {
            // The window closed while a read was in flight - routine.
        } catch (Exception ex) {
            // An async void that throws takes the process with it, and a
            // comparison is decoration: the window works without it.
            _log.Error("Conflict window: background comparison failed", ex);
        } finally {
            _comparing = false;
        }
    }

    private async Task CompareAsync(ConflictPair pair) {
        var conflict = pair.Conflict;
        bool? same = await Task.Run(() => Read(conflict), _cts.Token).ConfigureAwait(true);
        if (_cts.IsCancellationRequested) {
            return;
        }

        bool decided = Batch.SetCompared(pair, same);
        var row = RowOf(pair);
        row?.RefreshVerdict();
        if (decided) {
            row?.RefreshChoice();
            OnAnswersChanged();
        }
    }

    /// <summary>
    /// Null means "cannot say": one of the two could not be read (locked by
    /// whoever is writing it, or gone). The pair then stays open on content
    /// and is not tried again.
    /// </summary>
    private bool? Read(FileConflictInfo conflict) {
        try {
            return FileContentComparer.AreIdentical(
                _fs, conflict.Source.FullPath, conflict.ExistingTarget.FullPath, _cts.Token);
        } catch (OperationCanceledException) {
            return null;
        } catch (Exception ex) {
            _log.Info($"Conflict window: cannot compare {conflict.Source.FullPath} with {conflict.ExistingTarget.FullPath} - {ex.Message}");

            return null;
        }
    }

    /// <summary>
    /// Finds what collides inside a folder the user chose to merge, off the
    /// UI thread, and hangs the result under the row. The row says it is
    /// reading in the meantime; a folder that cannot be read keeps its
    /// answer and says so.
    /// </summary>
    private async void StartScan(ConflictPair pair) {
        Batch.MarkScanning(pair);
        RowOf(pair)?.RefreshChoice();

        var conflict = pair.Conflict;
        try {
            var scan = await Task.Run(
                () => MergeScanner.Scan(_fs, conflict.Source.FullPath, conflict.ExistingTarget.FullPath, conflict.IsMove, _cts.Token),
                _cts.Token).ConfigureAwait(true);
            if (_cts.IsCancellationRequested) {
                return;
            }
            Batch.AttachScan(pair, scan);
        } catch (OperationCanceledException) {
            return;
        } catch (Exception ex) {
            _log.Error($"Conflict window: cannot read {conflict.Source.FullPath} for a merge", ex);
            Batch.MarkScanFailed(pair);
        }

        RowOf(pair)?.RefreshChoice();
        RebuildRows();
        OnAnswersChanged();
        EnsureComparing();
    }
}
