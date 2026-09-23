using System.Collections.Immutable;
using Wander.Core.Layout;
using Wander.Core.Navigation;
using Wander.Core.Panels;

namespace Wander.Core.Workspace;

/// <summary>
/// Module 2 of the reducer (REDESIGN 4.5): the rows of both panels, what is
/// open in them, where the open folder is in them and where each cursor
/// stands. Levels are read off the UI thread (<see cref="ReadBranch"/>) and
/// come back as <see cref="BranchRead"/> under the epoch they were asked
/// under; an answer nobody is waiting for any more is dropped here. A panel
/// never closes a branch by itself, and a level read again keeps what was
/// open and where the cursor was - both are kept by path, not by row.
/// </summary>
public static class PanelRules {
    /// <summary>
    /// How long the panels wait between two re-reads for the window's
    /// activation (P-24, decision B23): switching back and forth between two
    /// windows must not re-read the disk every time.
    /// </summary>
    public const int ActivationRefreshPauseMs = 5000;


    public static WorkspaceState Apply(WorkspaceState state, WorkspaceEvent e, ICollection<WorkspaceEffect> effects) {
        state = e switch {
            WorkspaceStarted started => OnStarted(state, started, effects),
            Navigated navigated => OnNavigated(state, navigated, effects),
            RowClicked clicked => state.WithPanel(clicked.Pane, state.Panel(clicked.Pane) with { Caret = clicked.Path }),
            CaretMoved moved => Show(state.WithPanel(moved.Pane, state.Panel(moved.Pane) with { Caret = moved.Path }), moved.Pane, moved.Path, effects),
            ChevronToggled toggled => Toggle(state, toggled.Pane, toggled.Path, toggled.Open, toggled.All, effects),
            CaretMoveRequested move => OnKey(state, move, effects),
            ZoneEntered entered => OnZoneEntered(state, entered, effects),
            BranchRead read => OnBranchRead(state, read, effects),
            ChevronsProbed probed => OnProbed(state, probed),
            BookmarksChanged bookmarks => OnBookmarks(state, bookmarks, effects),
            Relocated relocated => OnRelocated(state, relocated),
            Removed removed => OnRemoved(state, removed),
            FolderChanged changed => OnFolderChanged(state, changed, effects),
            PanelsRefreshRequested => Reread(state, expandedOnly: false, effects),
            WindowActivated activated => OnActivated(state, activated, effects),
            OptionsChanged options => OnOptions(state, options, effects),
            EditRequested edit => state.WithPanel(edit.Pane, state.Panel(edit.Pane).Find(edit.Path) is { IsRenamable: true }
                ? state.Panel(edit.Pane) with { Editing = edit.Path }
                : state.Panel(edit.Pane)),
            EditEnded ended => state.WithPanel(ended.Pane, state.Panel(ended.Pane) with { Editing = null }),
            _ => state,
        };

        // An open row has its level read: a branch restored open, a branch
        // opened by a reveal, a branch whose level was dropped while it was
        // open - whatever opened it, the rows under it are asked for.
        state = ReadOpenLevels(state, Pane.Bookmarks, effects);

        return ReadOpenLevels(state, Pane.Drives, effects);
    }


    // --- Events ------------------------------------------------------------

    /// <summary>The panels come up: the branches saved open are open again, and the drives are asked for.</summary>
    private static WorkspaceState OnStarted(WorkspaceState state, WorkspaceStarted started, ICollection<WorkspaceEffect> effects) {
        foreach (var stop in started.Expanded) {
            var pane = stop.Source == NavigationSource.Bookmark ? Pane.Bookmarks : Pane.Drives;
            var panel = state.Panel(pane);
            // The saved row and every folder above it: a branch saved open
            // was reached through open rows.
            for (string? step = stop.Path; step is not null; step = PanelPaths.Parent(step)) {
                panel = panel.WithExpanded(step, true);
            }
            state = state.WithPanel(pane, panel);
        }

        return Read(state, Pane.Drives, PanelState.TopKey, effects);
    }

    /// <summary>
    /// The open folder moved: its place is in the panel it was opened from -
    /// the drives when it cannot be reached from the bookmarks (P-10) - and
    /// that panel is opened down to it, the cursor on it. A folder opened
    /// from the bookmarks leaves the drives' cursor where it was (P-20, P-22);
    /// one opened from anywhere else takes the bookmarks' cursor away (P-21).
    /// </summary>
    private static WorkspaceState OnNavigated(WorkspaceState state, Navigated navigated, ICollection<WorkspaceEffect> effects) {
        string path = navigated.Path;
        var pane = navigated.Source == NavigationSource.Bookmark && state.Bookmarks.TopHolding(path) is not null
            ? Pane.Bookmarks
            : Pane.Drives;

        state = state.WithPanel(pane, state.Panel(pane) with { Location = path, Caret = path });
        state = Reveal(state, pane, path, effects);

        return pane == Pane.Drives
            ? state with { Bookmarks = state.Bookmarks with { Location = null, Caret = null, Revealing = null } }
            : state with { Drives = state.Drives with { Location = null, Revealing = null } };
    }

    /// <summary>A key in a panel: the cursor moves, or the row under it opens or closes (decision B26).</summary>
    private static WorkspaceState OnKey(WorkspaceState state, CaretMoveRequested move, ICollection<WorkspaceEffect> effects) {
        var panel = state.Panel(move.Pane);
        var result = PanelKeyNavigation.Press(PanelView.Rows(panel), panel.Caret, move.Key, move.PageSize);

        return result.Outcome switch {
            PanelKeyOutcome.MoveCaret => state.WithPanel(move.Pane, panel with { Caret = result.Path }),
            PanelKeyOutcome.Expand => Toggle(state, move.Pane, result.Path!, open: true, all: false, effects),
            PanelKeyOutcome.Collapse => Toggle(state, move.Pane, result.Path!, open: false, all: false, effects),
            _ => state,
        };
    }

    /// <summary>
    /// The keyboard came into a panel. Ctrl+Shift+E opens the panel down to
    /// the open folder; Ctrl+1 does the same, except into the drives while
    /// the open folder came from the bookmarks - the drives' cursor stays
    /// where it was left (P-22). Tab lands on the panel's cursor, else on
    /// the open folder's place, else on the first row, opening the branch
    /// it is hidden in (P-7, P-8, decision B5) and opening no folder. Coming
    /// back from a menu, a dialog or another window moves nothing; nor does
    /// the application putting it there - it has said where the cursor is,
    /// if it meant to - and nor does a reason nobody gave: it opens no
    /// branch and puts the cursor nowhere.
    /// </summary>
    private static WorkspaceState OnZoneEntered(WorkspaceState state, ZoneEntered entered, ICollection<WorkspaceEffect> effects) {
        if (entered.Zone is not (WindowZone.Bookmarks or WindowZone.Drives)) {
            return state;
        }

        var pane = entered.Zone == WindowZone.Bookmarks ? Pane.Bookmarks : Pane.Drives;
        var panel = state.Panel(pane);
        switch (entered.Reason) {
            case ZoneReason.RevealKey:
                return RevealOpenFolder(state, pane, effects);

            case ZoneReason.PanelKey:
                return pane == Pane.Drives && state.Folder.Source == NavigationSource.Bookmark && panel.Caret is { } held
                    ? Show(state, pane, held, effects)
                    : RevealOpenFolder(state, pane, effects);

            case ZoneReason.Tab:
                string? caret = panel.Caret ?? panel.Location ?? FirstRow(panel);
                if (caret is null) {
                    return state;
                }

                return Show(state.WithPanel(pane, panel with { Caret = caret }), pane, caret, effects);

            default:
                return state;
        }
    }

    /// <summary>
    /// A level came back from the disk. Dropped when it is not the answer
    /// the level waits for (P-18). Rows still there keep what is known of
    /// them; a row gone takes the cursor to its neighbour; the row above
    /// learns whether it has anything under it; the rows never asked about
    /// get their chevrons asked for; an opening down to a path goes on.
    /// </summary>
    private static WorkspaceState OnBranchRead(WorkspaceState state, BranchRead read, ICollection<WorkspaceEffect> effects) {
        var panel = state.Panel(read.Pane);
        var level = panel.LevelOf(read.Path);
        if (level.State != LevelState.Reading || level.Epoch != read.Epoch) {
            return state;
        }

        var old = level.Rows;
        var rows = Merge(panel, old, read.Rows);
        panel = panel.WithLevel(read.Path, new PanelLevel(LevelState.Loaded, rows, read.Epoch));
        if (read.Path.Length > 0) {
            panel = SetChildren(panel, read.Path, rows.Length > 0 ? ChildrenKnown.Yes : ChildrenKnown.No);
        }
        panel = SettleGone(panel, read.Path, old, rows);
        state = state.WithPanel(read.Pane, panel);

        Probe(state, read.Pane, rows, effects);
        if (panel.OpenChildrenWhenRead.Contains(PanelPaths.Key(read.Path))) {
            state = OpenChildren(state, read.Pane, read.Path, effects);
        }

        return ContinueReveal(state, read.Pane, effects);
    }

    /// <summary>The disk said which rows have subfolders; a row whose level has been read knows better.</summary>
    private static WorkspaceState OnProbed(WorkspaceState state, ChevronsProbed probed) {
        var panel = state.Panel(probed.Pane);
        foreach (var (path, has) in probed.HasChildren) {
            if (panel.LevelOf(path).State == LevelState.Unread) {
                panel = SetChildren(panel, path, has ? ChildrenKnown.Yes : ChildrenKnown.No);
            }
        }

        return state.WithPanel(probed.Pane, panel);
    }

    /// <summary>
    /// The bookmarks' rows were built again. What was open, the cursor and
    /// the open folder's place stay by path (P-15, N7); a row that is no
    /// longer there takes the cursor to its neighbour.
    /// </summary>
    private static WorkspaceState OnBookmarks(WorkspaceState state, BookmarksChanged changed, ICollection<WorkspaceEffect> effects) {
        var panel = state.Bookmarks;
        var old = panel.Top;
        var rows = Merge(panel, old, changed.Rows);
        panel = panel.WithLevel(PanelState.TopKey, new PanelLevel(LevelState.Loaded, rows, 0));
        panel = SettleGone(panel, PanelState.TopKey, old, rows);
        state = state with { Bookmarks = panel };
        Probe(state, Pane.Bookmarks, rows, effects);

        return state;
    }

    /// <summary>
    /// A folder Wander moved or renamed: every row on it or under it, in
    /// both panels, takes the new path in place - open if it was open, the
    /// cursor still on it (P-12, P-17). The level it moved out of and the
    /// one it moved into are read again by whoever moved it
    /// (<see cref="FolderChanged"/>).
    /// </summary>
    private static WorkspaceState OnRelocated(WorkspaceState state, Relocated relocated) {
        return state with {
            Bookmarks = Follow(state.Bookmarks, relocated.From, relocated.To),
            Drives = Follow(state.Drives, relocated.From, relocated.To),
        };
    }

    /// <summary>
    /// Folders gone: their rows leave both panels, and a cursor on one of
    /// them - or inside one - goes to the next row of its level, else the
    /// one before, else the row above (P-14). Where the open folder goes is
    /// the navigation's to say (P-13).
    /// </summary>
    private static WorkspaceState OnRemoved(WorkspaceState state, Removed removed) {
        return state with {
            Bookmarks = Remove(state.Bookmarks, removed.Paths),
            Drives = Remove(state.Drives, removed.Paths),
        };
    }

    /// <summary>
    /// A folder gained or lost subfolders: an open level on it is read again;
    /// a closed one is dropped - it is read when it opens - and the chevron
    /// of a row on it is asked for again (P-18): an empty folder that got a
    /// subfolder gets its chevron back.
    /// </summary>
    private static WorkspaceState OnFolderChanged(WorkspaceState state, FolderChanged changed, ICollection<WorkspaceEffect> effects) {
        foreach (var pane in new[] { Pane.Bookmarks, Pane.Drives }) {
            var panel = state.Panel(pane);
            var level = panel.LevelOf(changed.Path);
            if (level.State != LevelState.Unread && panel.IsExpanded(changed.Path)) {
                state = Read(state, pane, changed.Path, effects);
            } else {
                if (level.State != LevelState.Unread) {
                    state = state.WithPanel(pane, panel.WithLevel(changed.Path, PanelLevel.Unread));
                }
                if (panel.Find(changed.Path) is { IsProbed: true } row) {
                    effects.Add(new ProbeChevrons(pane, new[] { row.Path }));
                }
            }
        }

        return state;
    }

    /// <summary>
    /// The window came back: the open levels of both panels are read again
    /// in the background (P-24, decision B23) - not more than once in
    /// <see cref="ActivationRefreshPauseMs"/>.
    /// </summary>
    private static WorkspaceState OnActivated(WorkspaceState state, WindowActivated activated, ICollection<WorkspaceEffect> effects) {
        if (state.LastActivationRefreshMs is { } last && activated.NowMs - last < ActivationRefreshPauseMs) {
            return state;
        }

        return Reread(state with { LastActivationRefreshMs = activated.NowMs }, expandedOnly: true, effects);
    }

    /// <summary>
    /// Hidden folders switched off: a cursor on one goes to the row above it
    /// (P-19). Either way every level is read again - the rows it holds are
    /// what the setting says now.
    /// </summary>
    private static WorkspaceState OnOptions(WorkspaceState state, OptionsChanged changed, ICollection<WorkspaceEffect> effects) {
        if (changed.Options.ShowHidden == state.Options.ShowHidden) {
            return state;
        }

        if (!changed.Options.ShowHidden) {
            foreach (var pane in new[] { Pane.Bookmarks, Pane.Drives }) {
                var panel = state.Panel(pane);
                if (panel.Find(panel.Caret) is { IsHidden: true } hidden) {
                    string? above = PanelPaths.Parent(hidden.Path);
                    state = state.WithPanel(pane, panel with { Caret = panel.Find(above) is not null ? above : null });
                }
            }
        }

        return Reread(state, expandedOnly: false, effects);
    }


    // --- Opening and closing -------------------------------------------------

    /// <summary>
    /// A row opens or closes; nothing else moves - not the cursor, not the
    /// open folder's place, not the list (P-6). Opened, its level is read if
    /// it has not been; Alt opens its children too (P-9). Closed while its
    /// level is still being read, the answer is dropped (P-18); Alt closes
    /// everything below as well.
    /// </summary>
    private static WorkspaceState Toggle(WorkspaceState state, Pane pane, string path, bool open, bool all, ICollection<WorkspaceEffect> effects) {
        var panel = state.Panel(pane);
        string key = PanelPaths.Key(path);
        if (open) {
            panel = panel.WithExpanded(path, true) with {
                OpenChildrenWhenRead = all ? panel.OpenChildrenWhenRead.Add(key) : panel.OpenChildrenWhenRead.Remove(key),
            };
            state = state.WithPanel(pane, panel);
            var level = panel.LevelOf(path);
            if (level.State == LevelState.Unread) {
                return Read(state, pane, path, effects);
            }

            return all && level.State == LevelState.Loaded ? OpenChildren(state, pane, path, effects) : state;
        }

        panel = panel.WithExpanded(path, false) with { OpenChildrenWhenRead = panel.OpenChildrenWhenRead.Remove(key) };
        if (panel.LevelOf(path).State == LevelState.Reading) {
            panel = panel.WithLevel(path, PanelLevel.Unread);
        }
        if (all) {
            panel = panel with { Expanded = panel.Expanded.Except(panel.Expanded.Where(k => PanelPaths.IsInside(k, path))) };
        }

        return state.WithPanel(pane, panel);
    }

    /// <summary>Alt with the chevron: the children of a read row open, one level and no further.</summary>
    private static WorkspaceState OpenChildren(WorkspaceState state, Pane pane, string path, ICollection<WorkspaceEffect> effects) {
        var panel = state.Panel(pane);
        panel = panel with { OpenChildrenWhenRead = panel.OpenChildrenWhenRead.Remove(PanelPaths.Key(path)) };
        foreach (var child in panel.LevelOf(path).Rows) {
            if (child.HasChevron) {
                panel = panel.WithExpanded(child.Path, true);
            }
        }

        return state.WithPanel(pane, panel);
    }

    /// <summary>Puts <paramref name="path"/> on screen: shown already, or opened down to (P-7).</summary>
    private static WorkspaceState Show(WorkspaceState state, Pane pane, string path, ICollection<WorkspaceEffect> effects) {
        return PanelView.IndexOf(PanelView.Rows(state.Panel(pane)), path) >= 0
            ? state
            : Reveal(state, pane, path, effects);
    }

    /// <summary>
    /// Ctrl+1 and Ctrl+Shift+E: the panel opened down to the open folder,
    /// the cursor on it. A folder the panel cannot reach - outside every
    /// bookmark - leaves the cursor where it was.
    /// </summary>
    private static WorkspaceState RevealOpenFolder(WorkspaceState state, Pane pane, ICollection<WorkspaceEffect> effects) {
        var panel = state.Panel(pane);
        if (state.Folder.Path is not { } open || panel.TopHolding(open) is null) {
            string? caret = panel.Caret ?? FirstRow(panel);

            return caret is null ? state : Show(state.WithPanel(pane, panel with { Caret = caret }), pane, caret, effects);
        }

        state = state.WithPanel(pane, panel with { Location = open, Caret = open });

        return Reveal(state, pane, open, effects);
    }

    /// <summary>Starts opening <paramref name="pane"/> down to <paramref name="path"/>, level by level.</summary>
    private static WorkspaceState Reveal(WorkspaceState state, Pane pane, string path, ICollection<WorkspaceEffect> effects) {
        state = state.WithPanel(pane, state.Panel(pane) with { Revealing = path });

        return ContinueReveal(state, pane, effects);
    }

    /// <summary>
    /// Opens the rows on the way to the path being revealed, as far as their
    /// levels are read; the first one not read is asked for, and the rest
    /// waits for its answer. A level that turns out not to hold the next step
    /// (a hidden folder, one gone) ends the reveal where it got to.
    /// </summary>
    private static WorkspaceState ContinueReveal(WorkspaceState state, Pane pane, ICollection<WorkspaceEffect> effects) {
        var panel = state.Panel(pane);
        if (panel.Revealing is not { } target) {
            return state;
        }

        if (panel.TopHolding(target) is not { } top) {
            return state.WithPanel(pane, panel with { Revealing = null });
        }

        var chain = PanelPaths.Chain(top.Path, target);
        for (int i = 0; i < chain.Count - 1; i++) {
            panel = panel.WithExpanded(chain[i], true);
            var level = panel.LevelOf(chain[i]);
            if (level.State == LevelState.Unread) {
                return Read(state.WithPanel(pane, panel), pane, chain[i], effects);
            }
            if (!level.Rows.Any(r => PanelPaths.Same(r.Path, chain[i + 1]))) {
                return level.State == LevelState.Reading
                    ? state.WithPanel(pane, panel)
                    : state.WithPanel(pane, panel with { Revealing = null });
            }
        }

        return state.WithPanel(pane, panel with { Revealing = null });
    }


    // --- Reading --------------------------------------------------------------

    /// <summary>Asks for the level under <paramref name="path"/> under a new epoch; the rows known so far stay on screen meanwhile.</summary>
    private static WorkspaceState Read(WorkspaceState state, Pane pane, string path, ICollection<WorkspaceEffect> effects) {
        int epoch = state.Epoch + 1;
        var panel = state.Panel(pane);
        var level = panel.LevelOf(path);
        panel = panel.WithLevel(path, new PanelLevel(LevelState.Reading, level.Rows, epoch));
        effects.Add(new ReadBranch(pane, path, epoch));

        return state.WithPanel(pane, panel) with { Epoch = epoch };
    }

    /// <summary>Every open row on screen whose level has not been read is asked for.</summary>
    private static WorkspaceState ReadOpenLevels(WorkspaceState state, Pane pane, ICollection<WorkspaceEffect> effects) {
        foreach (var line in PanelView.Rows(state.Panel(pane))) {
            if (line.IsExpanded && state.Panel(pane).LevelOf(line.Path).State == LevelState.Unread) {
                state = Read(state, pane, line.Path, effects);
            }
        }

        return state;
    }

    /// <summary>
    /// Every level read so far is asked for again - F5, the hidden folders
    /// switched on or off; <paramref name="expandedOnly"/> for the window's
    /// activation, which leaves closed levels alone. A closed level is
    /// otherwise dropped: it is read when it opens, and the chevron of its
    /// row is asked for meanwhile. A level already being read is left to it.
    /// </summary>
    private static WorkspaceState Reread(WorkspaceState state, bool expandedOnly, ICollection<WorkspaceEffect> effects) {
        foreach (var pane in new[] { Pane.Bookmarks, Pane.Drives }) {
            var probe = new List<string>();
            foreach (var (key, level) in state.Panel(pane).Levels) {
                if (key.Length == 0 || level.State != LevelState.Loaded) {
                    continue;
                }

                var panel = state.Panel(pane);
                if (panel.IsExpanded(key)) {
                    state = Read(state, pane, PanelPaths.FromKey(key), effects);
                } else if (!expandedOnly) {
                    state = state.WithPanel(pane, panel.WithLevel(key, PanelLevel.Unread));
                    if (panel.Find(key) is { IsProbed: true } row) {
                        probe.Add(row.Path);
                    }
                }
            }
            if (probe.Count > 0) {
                effects.Add(new ProbeChevrons(pane, probe));
            }
        }

        return state;
    }

    /// <summary>The rows whose level has not been read get their chevrons asked for.</summary>
    private static void Probe(WorkspaceState state, Pane pane, ImmutableArray<PanelRow> rows, ICollection<WorkspaceEffect> effects) {
        var panel = state.Panel(pane);
        var paths = rows
            .Where(r => r.IsProbed && panel.LevelOf(r.Path).State == LevelState.Unread)
            .Select(r => r.Path)
            .ToArray();
        if (paths.Length > 0) {
            effects.Add(new ProbeChevrons(pane, paths));
        }
    }


    // --- Rows ---------------------------------------------------------------

    /// <summary>
    /// The rows of a fresh answer, with what is known of each kept: a row
    /// whose own level has been read has a chevron by what it holds, and one
    /// asked about before keeps the answer until it is asked again.
    /// </summary>
    private static ImmutableArray<PanelRow> Merge(PanelState panel, ImmutableArray<PanelRow> old, IReadOnlyList<PanelRow> fresh) {
        var rows = ImmutableArray.CreateBuilder<PanelRow>(fresh.Count);
        foreach (var row in fresh) {
            var own = panel.LevelOf(row.Path);
            var known = own.State == LevelState.Loaded
                ? own.Rows.Length > 0 ? ChildrenKnown.Yes : ChildrenKnown.No
                : old.FirstOrDefault(o => PanelPaths.Same(o.Path, row.Path))?.Children ?? ChildrenKnown.Unknown;
            rows.Add(row.Children == ChildrenKnown.Unknown ? row with { Children = known } : row);
        }

        return rows.MoveToImmutable();
    }

    /// <summary>Every row on <paramref name="path"/>, in every level, learns what is under it.</summary>
    private static PanelState SetChildren(PanelState panel, string path, ChildrenKnown known) {
        foreach (var (key, level) in panel.Levels) {
            int at = IndexIn(level.Rows, path);
            if (at >= 0 && level.Rows[at].Children != known) {
                panel = panel.WithLevel(key, level with { Rows = level.Rows.SetItem(at, level.Rows[at] with { Children = known }) });
            }
        }

        return panel;
    }

    /// <summary>
    /// A level went from <paramref name="old"/> to <paramref name="fresh"/>:
    /// the cursor on a row that is gone, or inside one, goes to the next row
    /// that stayed, else the one before, else the row above; what was open
    /// or being edited or revealed under a gone row is let go of.
    /// </summary>
    private static PanelState SettleGone(PanelState panel, string levelKey, ImmutableArray<PanelRow> old, ImmutableArray<PanelRow> fresh) {
        var gone = old.Where(o => IndexIn(fresh, o.Path) < 0).ToList();
        if (gone.Count == 0) {
            return panel;
        }

        foreach (var row in gone) {
            if (panel.Caret is { } caret && PanelPaths.IsUnderOrSelf(caret, row.Path)) {
                panel = panel with { Caret = Successor(old, fresh, row, levelKey) };
            }
            if (panel.Editing is { } editing && PanelPaths.IsUnderOrSelf(editing, row.Path)) {
                panel = panel with { Editing = null };
            }
            if (panel.Revealing is { } revealing && PanelPaths.IsUnderOrSelf(revealing, row.Path)) {
                panel = panel with { Revealing = null };
            }
            panel = panel with { Expanded = panel.Expanded.Except(panel.Expanded.Where(k => PanelPaths.IsUnderOrSelf(k, row.Path))) };
        }

        return panel;
    }

    /// <summary>Where a cursor goes when <paramref name="row"/> leaves its level: the next row that stayed, else the one before, else the row above.</summary>
    private static string? Successor(ImmutableArray<PanelRow> old, ImmutableArray<PanelRow> fresh, PanelRow row, string levelKey) {
        int at = IndexIn(old, row.Path);
        for (int i = at + 1; i < old.Length; i++) {
            if (IndexIn(fresh, old[i].Path) >= 0) {
                return old[i].Path;
            }
        }
        for (int i = at - 1; i >= 0; i--) {
            if (IndexIn(fresh, old[i].Path) >= 0) {
                return old[i].Path;
            }
        }

        return levelKey.Length > 0 ? levelKey : null;
    }

    /// <summary>One panel after a folder moved: levels, rows, what is open and every path it holds follow.</summary>
    private static PanelState Follow(PanelState panel, string from, string to) {
        var levels = ImmutableDictionary.CreateBuilder<string, PanelLevel>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, level) in panel.Levels) {
            var rows = level.Rows.Select(r => FollowRow(r, from, to)).ToImmutableArray();
            string moved = key.Length == 0 ? key : PanelPaths.Key(PanelPaths.Follow(key, from, to));
            levels[moved] = level with { Rows = rows };
        }

        return panel with {
            Levels = levels.ToImmutable(),
            Expanded = FollowKeys(panel.Expanded, from, to),
            OpenChildrenWhenRead = FollowKeys(panel.OpenChildrenWhenRead, from, to),
            Location = FollowPath(panel.Location, from, to),
            Caret = FollowPath(panel.Caret, from, to),
            Editing = FollowPath(panel.Editing, from, to),
            Revealing = FollowPath(panel.Revealing, from, to),
        };
    }

    /// <summary>
    /// A row after a folder moved. Only the moved folder's own row changes
    /// its label, and not a built-in bookmark's - that label is Windows's
    /// name for the folder, not the folder's.
    /// </summary>
    private static PanelRow FollowRow(PanelRow row, string from, string to) {
        if (!PanelPaths.IsUnderOrSelf(row.Path, from)) {
            return row;
        }

        string path = PanelPaths.Follow(row.Path, from, to);
        bool renamed = PanelPaths.Same(row.Path, from) && row.Role != PanelRowRole.BuiltInBookmark;

        return row with { Path = path, Name = renamed ? Path.GetFileName(path) : row.Name };
    }

    private static ImmutableHashSet<string> FollowKeys(ImmutableHashSet<string> keys, string from, string to) {
        return keys.Any(k => PanelPaths.IsUnderOrSelf(k, from))
            ? keys.Select(k => PanelPaths.Key(PanelPaths.Follow(k, from, to))).ToImmutableHashSet(StringComparer.OrdinalIgnoreCase)
            : keys;
    }

    private static string? FollowPath(string? path, string from, string to) {
        return path is null ? null : PanelPaths.Follow(path, from, to);
    }

    /// <summary>One panel after folders went: their rows, their levels, what was open under them.</summary>
    private static PanelState Remove(PanelState panel, IReadOnlyList<string> removed) {
        bool Gone(string path) => removed.Any(r => PanelPaths.IsUnderOrSelf(path, r));

        foreach (var (key, level) in panel.Levels) {
            if (key.Length > 0 && Gone(key)) {
                panel = panel with { Levels = panel.Levels.Remove(key) };
                continue;
            }

            var kept = level.Rows.Where(r => !Gone(r.Path)).ToImmutableArray();
            if (kept.Length != level.Rows.Length) {
                panel = SettleGone(panel.WithLevel(key, level with { Rows = kept }), key, level.Rows, kept);
            }
        }

        return panel with {
            Expanded = panel.Expanded.Except(panel.Expanded.Where(Gone)),
            Location = panel.Location is { } location && Gone(location) ? null : panel.Location,
        };
    }

    private static int IndexIn(ImmutableArray<PanelRow> rows, string path) {
        for (int i = 0; i < rows.Length; i++) {
            if (PanelPaths.Same(rows[i].Path, path)) {
                return i;
            }
        }

        return -1;
    }

    private static string? FirstRow(PanelState panel) {
        return panel.Top.IsEmpty ? null : panel.Top[0].Path;
    }
}
