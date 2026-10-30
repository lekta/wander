using System.Diagnostics.CodeAnalysis;
using Wander.Core.Diagnostics;
using Wander.Core.FileSystem;
using Wander.Core.Logging;
using Wander.Core.Panels;

namespace Wander.Core.Listing;

/// <summary>
/// How much each folder of the table holds: walked on demand, as its cell
/// comes on screen, so a folder of five thousand subfolders costs the
/// screenful being looked at. The answers go to the cells, not into the rows:
/// a row replaced is a row rebuilt, and a list with its numbers coming in
/// must not move (CLAUDE.md, "Список не дёргается").
///
/// <para>
/// What was found is kept for the session and shown at once on the way back;
/// a folder is walked again once per visit, and the number changes in place
/// if it was out of date. Everything but the walk itself runs on the thread
/// <c>post</c> delivers to - the UI thread.
/// </para>
/// </summary>
[SuppressMessage("Design", "CA1001",
    Justification = "A cancellation source, managed only; replaced on every visit, and the owner lives as long as the application.")]
public sealed class FolderSizes {
    /// <summary>How many folders are walked at once. The disk is shared with the thumbnails.</summary>
    private const int Parallelism = 2;

    /// <summary>How many answers are kept before they start over. A number is a few dozen bytes; this is a ceiling, not a budget.</summary>
    private const int KnownCapacity = 16384;

    private readonly IFileSystem _fs;
    private readonly Func<string, bool> _walkable;
    private readonly Action<Action> _post;
    private readonly Action<Action> _run;
    private readonly ILogger _log;

    private readonly Dictionary<string, long> _known = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _asked = new(StringComparer.OrdinalIgnoreCase);
    private readonly Stack<string> _waiting = new();

    private CancellationTokenSource _visit = new();
    private int _running;
    private bool _working;
    private bool _isOn = true;


    /// <param name="walkable">Whether a folder is worth walking at all - not on a share, not inside an archive. Asked off the UI thread.</param>
    /// <param name="post">Runs an answer on the UI thread.</param>
    /// <param name="run">Runs a walk off it.</param>
    public FolderSizes(IFileSystem fs, Func<string, bool> walkable, Action<Action> post, Action<Action> run, ILogger log) {
        _fs = fs;
        _walkable = walkable;
        _post = post;
        _run = run;
        _log = log;
    }


    /// <summary>A folder was weighed: its path and what it holds.</summary>
    public event Action<string, long>? Measured;

    /// <summary>What was known may be out of date, or the switch moved: whoever shows a number looks again.</summary>
    public event Action? Invalidated;

    /// <summary>Nothing is being walked and nothing waits: the last of what was asked for has been answered.</summary>
    public event Action? Drained;


    /// <summary>The setting: off, nothing is walked and nothing is known.</summary>
    public bool IsOn {
        get => _isOn;
        set {
            if (_isOn == value) {
                return;
            }

            _isOn = value;
            if (!value) {
                Leave();
            }
            Invalidated?.Invoke();
        }
    }


    /// <summary>What the folder held when last weighed; null when it has not been.</summary>
    public long? Known(string path) {
        return _isOn && _known.TryGetValue(path, out long size) ? size : null;
    }


    /// <summary>A folder wants weighing. Cheap to call again: once per visit.</summary>
    public void Want(string path) {
        if (!_isOn || !_asked.Add(path)) {
            return;
        }

        _waiting.Push(path);
        _working = true;
        Pump();
    }


    /// <summary>The folder on screen was left: what is being walked stops, and what the next one asks for is walked anew.</summary>
    public void Leave() {
        _visit.Cancel();
        _visit.Dispose();
        _visit = new CancellationTokenSource();
        _asked.Clear();
        _waiting.Clear();
        _working = false;
    }


    /// <summary>F5: everything on screen is weighed again; the numbers stay until the new ones come.</summary>
    public void AskAgain() {
        _asked.Clear();
        Invalidated?.Invoke();
    }


    /// <summary>
    /// An operation went through these paths: every folder on one of them or
    /// holding one is weighed again - the folders it touched and those above.
    /// </summary>
    public void AskAgainFor(IReadOnlyCollection<string> touched) {
        int before = _asked.Count;
        _asked.RemoveWhere(asked => touched.Any(path => PanelPaths.IsUnderOrSelf(path, asked) || PanelPaths.IsUnderOrSelf(asked, path)));
        if (_asked.Count != before) {
            Invalidated?.Invoke();
        }
    }


    private void Pump() {
        while (_running < Parallelism && _waiting.TryPop(out string? path)) {
            _running++;
            var token = _visit.Token;
            _run(() => {
                long? size = Measure(path, token);
                _post(() => Answered(path, size, token));
            });
        }

        if (_working && _running == 0 && _waiting.Count == 0) {
            _working = false;
            Drained?.Invoke();
        }
    }

    /// <summary>Any thread. Null for no answer: not worth walking, unreadable, the visit over.</summary>
    private long? Measure(string path, CancellationToken token) {
        try {
            if (token.IsCancellationRequested || !_walkable(path)) {
                return null;
            }

            using var pass = PerfLog.Measure("bg.foldersize");
            var stats = FolderStatistics.Collect(_fs, path, maxTypes: 0, maxSubfolders: 0, ct: token);

            return stats.IsUnreadable ? null : stats.TotalSize;
        } catch (OperationCanceledException) {
            return null;
        } catch (Exception ex) when (ex is not OutOfMemoryException) {
            _log.Warn($"Folder not weighed: {path} ({ex.Message})");

            return null;
        }
    }

    private void Answered(string path, long? size, CancellationToken token) {
        _running--;
        if (!token.IsCancellationRequested && size is { } value) {
            if (_known.Count >= KnownCapacity) {
                _known.Clear();
            }
            _known[path] = value;
            Measured?.Invoke(path, value);
        }
        Pump();
    }
}
