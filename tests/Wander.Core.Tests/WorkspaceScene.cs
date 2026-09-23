using Wander.Core.Layout;
using Wander.Core.Listing;
using Wander.Core.Navigation;
using Wander.Core.Panels;
using Wander.Core.Workspace;

namespace Wander.Core.Tests;

/// <summary>
/// A workspace model driven by events, with a folder tree standing in for
/// the disk: every read the model asks for can be answered from it, the way
/// the application's executor answers them. What the tests of the model
/// share - they differ in what they post.
/// </summary>
internal sealed class WorkspaceScene {
    private readonly HashSet<string> _folders = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _hidden = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _drives = new();
    private long _clock = 10_000;


    public WorkspaceScene(params string[] folders) {
        foreach (string folder in folders) {
            Add(folder);
        }
    }


    public WorkspaceState State { get; private set; } = WorkspaceState.Initial;

    /// <summary>What the last <see cref="Post"/> asked for.</summary>
    public IReadOnlyList<WorkspaceEffect> Effects { get; private set; } = Array.Empty<WorkspaceEffect>();

    /// <summary>Every effect since the scene began.</summary>
    public List<WorkspaceEffect> AllEffects { get; } = new();

    /// <summary>The clock the events carry, in milliseconds.</summary>
    public long Now => _clock;

    public PanelState Drives => State.Drives;

    public PanelState Bookmarks => State.Bookmarks;


    /// <summary>A folder and everything above it; a drive for a root.</summary>
    public WorkspaceScene Add(string folder, bool hidden = false) {
        for (string? step = folder; step is not null; step = PanelPaths.Parent(step)) {
            if (PanelPaths.Parent(step) is null) {
                string drive = PanelPaths.Key(step) + Path.DirectorySeparatorChar;
                if (!_drives.Contains(drive, StringComparer.OrdinalIgnoreCase)) {
                    _drives.Add(drive);
                }
            } else {
                _folders.Add(PanelPaths.Key(step));
            }
        }
        if (hidden) {
            _hidden.Add(PanelPaths.Key(folder));
        }

        return this;
    }

    public WorkspaceScene Delete(string folder) {
        _folders.RemoveWhere(f => PanelPaths.IsUnderOrSelf(f, folder));

        return this;
    }

    public WorkspaceScene Advance(long ms) {
        _clock += ms;

        return this;
    }

    public WorkspaceScene Post(WorkspaceEvent e) {
        var result = WorkspaceReducer.Apply(State, e);
        State = result.State;
        Effects = result.Effects;
        AllEffects.AddRange(result.Effects);

        return this;
    }

    /// <summary>
    /// Answers every read and probe the model has asked for, and whatever
    /// those answers ask for in turn, until it asks for nothing more.
    /// <see cref="Effects"/> is then everything asked for on the way,
    /// starting with what the last post asked.
    /// </summary>
    public WorkspaceScene Settle() {
        var pending = new Queue<WorkspaceEffect>(Effects);
        var seen = new List<WorkspaceEffect>(Effects);
        int answered = 0;
        while (pending.Count > 0) {
            if (++answered > 500) {
                throw new InvalidOperationException("the model kept asking");
            }

            WorkspaceEvent? answer = pending.Dequeue() switch {
                ReadBranch read => new BranchRead(read.Pane, read.Path, Level(read.Path), read.Epoch),
                ProbeChevrons probe => new ChevronsProbed(
                    probe.Pane, probe.Paths.ToDictionary(p => p, HasChildren, StringComparer.OrdinalIgnoreCase)),
                _ => null,
            };
            if (answer is null) {
                continue;
            }

            var result = WorkspaceReducer.Apply(State, answer);
            State = result.State;
            AllEffects.AddRange(result.Effects);
            foreach (var effect in result.Effects) {
                pending.Enqueue(effect);
                seen.Add(effect);
            }
        }
        Effects = seen;

        return this;
    }

    /// <summary>The window comes up with the drives read.</summary>
    public WorkspaceScene Start(params NavigationStop[] expanded) {
        return Post(new WorkspaceStarted(expanded)).Settle();
    }

    public WorkspaceScene Navigate(string path, NavigationSource source = NavigationSource.Drives, NavigationKind how = NavigationKind.Go) {
        return Post(new Navigated(path, source, how)).Settle();
    }

    public WorkspaceScene Enter(WindowZone? zone, ZoneReason reason = ZoneReason.Tab) {
        return Post(new ZoneEntered(zone, reason)).Settle();
    }

    public WorkspaceScene Key(Pane pane, PanelKey key, int pageSize = 10) {
        return Post(new CaretMoveRequested(pane, key, pageSize, _clock)).Settle();
    }

    public WorkspaceScene Click(Pane pane, string path) {
        return Post(new RowClicked(pane, path, _clock)).Settle();
    }

    public WorkspaceScene Chevron(Pane pane, string path, bool open, bool all = false) {
        return Post(new ChevronToggled(pane, path, open, all)).Settle();
    }

    public WorkspaceScene Options(bool arrowsOpen = false, bool showHidden = false) {
        return Post(new OptionsChanged(new WorkspaceOptions(arrowsOpen, showHidden))).Settle();
    }

    public WorkspaceScene SetBookmarks(params PanelRow[] rows) {
        return Post(new BookmarksChanged(rows)).Settle();
    }

    /// <summary>The user selected these rows of the list, the first the main one and the last under the keyboard.</summary>
    public WorkspaceScene Select(params string[] rows) {
        return Post(new ListSelectionChanged(rows, rows.FirstOrDefault(), rows.LastOrDefault()));
    }

    /// <summary>The list's rows landed.</summary>
    public WorkspaceScene Land(
        IReadOnlyList<string> before, IReadOnlyList<string> after,
        ListingReason reason = ListingReason.Relist, ArrivalDecision? intent = null,
        params (string From, string To)[] renames) {
        return Post(new ListingLanded(before, after, reason, intent ?? ArrivalDecision.None, renames));
    }

    /// <summary>The lines of a panel as drawn, by path.</summary>
    public IReadOnlyList<string> Lines(Pane pane) {
        return PanelView.Rows(State.Panel(pane)).Select(l => l.Path).ToList();
    }

    public bool Shows(Pane pane, string path) {
        return PanelView.IndexOf(PanelView.Rows(State.Panel(pane)), path) >= 0;
    }

    /// <summary>The target as the model holds its facts, the list having nothing selected.</summary>
    public Target Target => TargetRules.Of(TargetRules.FactsOf(State));

    public IEnumerable<T> Asked<T>() where T : WorkspaceEffect {
        return Effects.OfType<T>();
    }

    public static PanelRow Bookmark(string path, PanelRowRole role = PanelRowRole.OwnBookmark) {
        return new PanelRow(path, Path.GetFileName(PanelPaths.Key(path)), PanelRowKind.Folder) { Role = role };
    }


    private IReadOnlyList<PanelRow> Level(string path) {
        if (path.Length == 0) {
            return _drives.Select(d => new PanelRow(d, d, PanelRowKind.Drive)).ToList();
        }

        return _folders
            .Where(f => PanelPaths.Same(PanelPaths.Parent(f), path))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .Select(f => new PanelRow(f, Path.GetFileName(f), PanelRowKind.Folder) { IsHidden = _hidden.Contains(f) })
            .ToList();
    }

    private bool HasChildren(string path) {
        return _folders.Any(f => PanelPaths.Same(PanelPaths.Parent(f), path));
    }
}
