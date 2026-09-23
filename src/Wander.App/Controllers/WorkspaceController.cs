using System.Windows.Threading;
using Wander.Core.Diagnostics;
using Wander.Core.FileSystem;
using Wander.Core.Logging;
using Wander.Core.Navigation;
using Wander.Core.Panels;
using Wander.Core.Shell;
using Wander.Core.Workspace;

namespace Wander.App.Controllers;

/// <summary>
/// The one executor of the window's model (REDESIGN 4.6). Events come in
/// from the views, the view model and the pool; each goes through
/// <see cref="WorkspaceReducer"/>, the new state goes out
/// (<see cref="StateChanged"/>) and the effects it asks for are carried out
/// here - a navigation, a level read off the UI thread, a timer - or, for
/// what only the controls can do - move the keyboard, put a selection on the
/// list, open an editor - handed to the window (<see cref="ViewEffectRequested"/>),
/// after the state is drawn: a line the keyboard is sent to is on screen by
/// then. What an effect brings back comes back as an event with the epoch
/// it was asked under.
///
/// <para>
/// Events raised while one is being applied - a navigation run as an
/// effect reports itself, a line focused as the state is drawn reports the
/// keyboard - wait in a queue and are applied after it, in order: nothing
/// is applied inside another application, and the order of the trace is
/// the order of the model.
/// </para>
/// </summary>
public sealed class WorkspaceController {
    private readonly IFileSystem _fs;
    private readonly IShellNamespace? _shell;
    private readonly Func<EntryVisibility> _visibility;
    private readonly Action<string, NavigationSource> _navigate;
    private readonly Dispatcher _dispatcher;
    private readonly ILogger _log;
    private readonly Queue<WorkspaceEvent> _queue = new();
    private bool _applying;
    private DispatcherTimer? _throttle;


    /// <param name="visibility">What the panels show - read on the UI thread when a level is asked for.</param>
    /// <param name="navigate">Opens a folder the way a click on a panel row does - the folder itself selected.</param>
    public WorkspaceController(
        IFileSystem fs, IShellNamespace? shell, Func<EntryVisibility> visibility,
        Action<string, NavigationSource> navigate, Dispatcher dispatcher, ILogger log) {
        _fs = fs;
        _shell = shell;
        _visibility = visibility;
        _navigate = navigate;
        _dispatcher = dispatcher;
        _log = log;
    }


    /// <summary>The state before and after an event, before its effects are set going.</summary>
    public event Action<WorkspaceState, WorkspaceState>? StateChanged;

    /// <summary>
    /// An effect only the controls can carry out - <see cref="FocusZone"/>,
    /// <see cref="FocusRow"/>, <see cref="ApplyListSelection"/>,
    /// <see cref="OpenEditor"/>: the window's, as the controls are its.
    /// </summary>
    public event Action<WorkspaceEffect>? ViewEffectRequested;


    public WorkspaceState State { get; private set; } = WorkspaceState.Initial;

    /// <summary>The clock the events carry, in milliseconds.</summary>
    public static long Now => Environment.TickCount64;


    /// <summary>
    /// Applies <paramref name="e"/> - after the ones already waiting, when
    /// this is called from inside the application of another.
    /// </summary>
    public void Post(WorkspaceEvent e) {
        _queue.Enqueue(e);
        if (_applying) {
            return;
        }

        _applying = true;
        try {
            while (_queue.Count > 0) {
                var next = _queue.Dequeue();
                var before = State;
                var result = WorkspaceReducer.Apply(before, next);
                State = result.State;
                Trace(next, result.Effects);
                if (!ReferenceEquals(before, State)) {
                    StateChanged?.Invoke(before, State);
                }
                foreach (var effect in result.Effects) {
                    Execute(effect);
                }
            }
        } finally {
            _applying = false;
        }
    }


    // --- Effects -------------------------------------------------------------

    private void Execute(WorkspaceEffect effect) {
        switch (effect) {
            case Navigate navigate:
                _navigate(navigate.Path, navigate.Source);
                break;
            case ReadBranch read:
                _ = ReadAsync(read);
                break;
            case ProbeChevrons probe:
                _ = ProbeAsync(probe);
                break;
            case ScheduleThrottle schedule:
                Schedule(schedule.AtMs);
                break;
            case FocusZone or FocusRow or ApplyListSelection or OpenEditor:
                ViewEffectRequested?.Invoke(effect);
                break;
        }
    }

    /// <summary>
    /// A level read on the pool and handed back under its epoch. Watched: a
    /// share on a sleeping disk or a RAR the shell has to open answers in
    /// seconds, and a spinner that spins for seconds is otherwise nowhere in
    /// the log. A level that cannot be read is an empty one - access denied,
    /// a drive pulled out, a broken archive are not errors to report here.
    /// </summary>
    private async Task ReadAsync(ReadBranch read) {
        var visibility = _visibility();
        IReadOnlyList<PanelRow> rows;
        try {
            rows = await LongWait.WatchAsync(
                Task.Run(() => ReadLevel(read.Path, visibility)), _log, $"tree: listing {read.Path}");
        } catch (Exception ex) {
            _log.Warn($"Panel level not readable: {read.Path} ({ex.Message})");
            rows = Array.Empty<PanelRow>();
        }

        Post(new BranchRead(read.Pane, read.Path, rows, read.Epoch));
    }

    /// <summary>
    /// The rows under <paramref name="path"/>: the drives for the top of the
    /// drives panel; the folders of an archive; otherwise the folders on disk
    /// the settings show, and the archives the shell opens as folders among
    /// them by name, the way Explorer's navigation pane has them.
    /// </summary>
    private IReadOnlyList<PanelRow> ReadLevel(string path, EntryVisibility visibility) {
        if (path.Length == 0) {
            return _fs.GetRoots().Select(root => new PanelRow(root.FullPath, root.Name, PanelRowKind.Drive)).ToList();
        }

        var folders = new List<PanelRow>();
        try {
            if (Archives.Contains(path)) {
                if (_shell is not null) {
                    foreach (var entry in _shell.Enumerate(path)) {
                        if (entry.Kind == EntryKind.Directory) {
                            folders.Add(new PanelRow(entry.FullPath, entry.Name, PanelRowKind.ArchiveFolder));
                        }
                    }
                }

                return folders;
            }

            var archives = new List<PanelRow>();
            foreach (var entry in _fs.Enumerate(path)) {
                bool isFolder = entry.Kind == EntryKind.Directory;
                bool isArchive = !isFolder && entry.Kind == EntryKind.File && Archives.Of(entry.FullPath) is { IsRoot: true };
                if ((!isFolder && !isArchive) || !visibility.Allows(entry)) {
                    continue;
                }

                var row = new PanelRow(entry.FullPath, entry.Name, isFolder ? PanelRowKind.Folder : PanelRowKind.Archive) {
                    IsHidden = entry.IsHidden,
                };
                (isFolder ? folders : archives).Add(row);
            }

            // The listing comes folders first and files after; an archive
            // belongs among the folders, where the eye looks for it -
            // inserted rather than re-sorted, so the folders keep the order
            // the filesystem gave them.
            foreach (var archive in archives) {
                int at = folders.FindIndex(f => string.Compare(archive.Name, f.Name, StringComparison.OrdinalIgnoreCase) < 0);
                folders.Insert(at < 0 ? folders.Count : at, archive);
            }
        } catch (Exception ex) when (ex is not OutOfMemoryException) {
            // Access denied, unavailable: the row simply has nothing under it.
        }

        return folders;
    }

    /// <summary>Whether each row has subfolders, asked on the pool - a question per row, one disk each.</summary>
    private async Task ProbeAsync(ProbeChevrons probe) {
        var answers = await Task.Run(() => {
            var found = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            foreach (string path in probe.Paths) {
                try {
                    found[path] = _fs.HasSubdirectories(path);
                } catch (Exception ex) when (ex is not OutOfMemoryException) {
                    found[path] = false;
                }
            }

            return found;
        });

        Post(new ChevronsProbed(probe.Pane, answers));
    }

    /// <summary>One timer for the throttle of the panels' arrow keys: a new time replaces the old one.</summary>
    private void Schedule(long atMs) {
        if (_throttle is null) {
            _throttle = new DispatcherTimer(DispatcherPriority.Input, _dispatcher);
            _throttle.Tick += (_, _) => {
                _throttle!.Stop();
                Post(new ThrottleElapsed(Now));
            };
        }

        _throttle.Stop();
        _throttle.Interval = TimeSpan.FromMilliseconds(Math.Max(1, atMs - Now));
        _throttle.Start();
    }


    // --- Trace --------------------------------------------------------------------

    /// <summary>
    /// A line per event in the action trace (AppSettings.LogActions): what
    /// happened and what it asked for (REDESIGN 4.9). Paths are masked like
    /// every other line of the log.
    /// </summary>
    private static void Trace(WorkspaceEvent e, IReadOnlyList<WorkspaceEffect> effects) {
        if (!Log.Details) {
            return;
        }

        string asked = effects.Count == 0 ? "" : "; effects: " + string.Join(", ", effects.Select(Describe));
        Log.Detail($"WS {Describe(e)}{asked}");
    }

    private static string Describe(WorkspaceEvent e) {
        return e switch {
            BranchRead read => $"BranchRead {read.Pane} {Level(read.Path)} ({read.Rows.Count} rows, epoch {read.Epoch})",
            ChevronsProbed probed => $"ChevronsProbed {probed.Pane} ({probed.HasChildren.Count})",
            BookmarksChanged changed => $"BookmarksChanged ({changed.Rows.Count} rows)",
            WorkspaceStarted started => $"WorkspaceStarted ({started.Expanded.Count} open)",
            Removed removed => $"Removed {string.Join(", ", removed.Paths)}",
            MenuOpened opened => $"MenuOpened {opened.Context.Subject.Describe()}",
            MenuClosed closed => $"MenuClosed {closed.Context.Subject.Describe()}",
            ListingLanded landed => $"ListingLanded {landed.Reason} ({landed.Before.Count} -> {landed.After.Count} rows, " +
                $"intent {landed.Intent.Outcome}, {landed.Renames.Count} renamed)",
            ListSelectionChanged changed => $"ListSelectionChanged ({Selected(changed.Selection)}, caret {changed.Caret})",
            _ => e.ToString(),
        };
    }

    private static string Describe(WorkspaceEffect effect) {
        return effect switch {
            ReadBranch read => $"ReadBranch {read.Pane} {Level(read.Path)} (epoch {read.Epoch})",
            ProbeChevrons probe => $"ProbeChevrons {probe.Pane} ({probe.Paths.Count})",
            ApplyListSelection apply => $"ApplyListSelection ({Selected(apply.List.Selection)}, caret {apply.List.Caret}{(apply.Scroll ? ", scroll" : "")})",
            _ => effect.ToString(),
        };
    }

    /// <summary>A selection in a trace line: how many, and the first - five thousand paths are not a line.</summary>
    private static string Selected(IReadOnlyList<string> selection) {
        return selection.Count == 0 ? "none" : $"{selection.Count}, first {selection[0]}";
    }

    private static string Level(string path) {
        return path.Length == 0 ? "(top)" : path;
    }
}
