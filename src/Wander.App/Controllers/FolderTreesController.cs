using Wander.App.ViewModels;
using Wander.Core.Navigation;
using Wander.Core.Panels;
using Wander.Core.Workspace;

namespace Wander.App.Controllers;

/// <summary>
/// The two folder panels as drawn: the lines of each, kept in step with the
/// model (<see cref="WorkspaceController"/>). A projection - every decision
/// about what is open, lit or where the open folder is was made by the
/// model's rules; this only turns the state into lines, reusing a line
/// whose key survived so its container, and the keyboard on it, stay.
/// </summary>
public sealed class FolderTreesController {
    /// <summary>
    /// Past this many edits the panel is refilled in one go: a branch of
    /// thousands of folders opened one insert at a time would lay the panel
    /// out thousands of times.
    /// </summary>
    private const int MaxIncrementalEdits = 256;

    private readonly WorkspaceController _workspace;


    public FolderTreesController(WorkspaceController workspace) {
        _workspace = workspace;
        _workspace.StateChanged += (_, state) => Project(state);
    }


    /// <summary>The lines of both panels were brought in step with the model.</summary>
    public event EventHandler? Projected;


    /// <summary>The bookmarks panel's lines. Bound by the upper half of the left panel.</summary>
    public BulkObservableCollection<TreeNodeViewModel> BookmarkLines { get; } = new();

    /// <summary>The drives panel's lines. Bound by the lower half.</summary>
    public BulkObservableCollection<TreeNodeViewModel> DriveLines { get; } = new();


    /// <summary>The lines of one panel.</summary>
    public IReadOnlyList<TreeNodeViewModel> Lines(Pane pane) {
        return pane == Pane.Bookmarks ? BookmarkLines : DriveLines;
    }

    /// <summary>The line under a panel's cursor, when it is drawn.</summary>
    public TreeNodeViewModel? CaretLine(Pane pane) {
        return Lines(pane).FirstOrDefault(l => l.IsCaret);
    }

    /// <summary>The line of <paramref name="pane"/> on <paramref name="path"/>: the cursor's when it stands there, else the first drawn.</summary>
    public TreeNodeViewModel? LineAt(Pane pane, string path) {
        return CaretLine(pane) is { } caret && PanelPaths.Same(caret.FullPath, path)
            ? caret
            : Lines(pane).FirstOrDefault(l => PanelPaths.Same(l.FullPath, path));
    }

    /// <summary>
    /// Every open line of both panels, tagged with its panel - what the
    /// session keeps. Only lines on screen: a row closed with an open branch
    /// under it keeps that branch open for the session, but saving it would
    /// open the closed row on the next start.
    /// </summary>
    public List<NavigationStop> CollectExpanded() {
        var result = new List<NavigationStop>();
        foreach (var (pane, source) in new[] { (Pane.Drives, NavigationSource.Drives), (Pane.Bookmarks, NavigationSource.Bookmark) }) {
            foreach (var line in PanelView.Rows(_workspace.State.Panel(pane))) {
                if (line.IsExpanded) {
                    result.Add(new NavigationStop(line.Path, source));
                }
            }
        }

        return result.Distinct().ToList();
    }


    private void Project(WorkspaceState state) {
        Project(BookmarkLines, state, Pane.Bookmarks);
        Project(DriveLines, state, Pane.Drives);
        Projected?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// One panel's lines brought in step: inserted, moved and removed around
    /// the lines that stay (the rule is BranchReconcile's, over the lines'
    /// keys), then every line told what the state says of it.
    /// </summary>
    private static void Project(BulkObservableCollection<TreeNodeViewModel> lines, WorkspaceState state, Pane pane) {
        var fresh = PanelView.Rows(state.Panel(pane));
        var kept = new Dictionary<string, TreeNodeViewModel>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines) {
            kept.TryAdd(line.Key, line);
        }

        var edits = BranchReconcile.Plan(lines.Select(l => l.Key).ToList(), fresh.Select(f => f.Key).ToList());
        if (edits.Count > MaxIncrementalEdits) {
            lines.ReplaceAll(fresh.Select(f => kept.TryGetValue(f.Key, out var line) ? line : new TreeNodeViewModel(f.Key, f.Row)).ToList());
        } else {
            foreach (var edit in edits) {
                switch (edit.Kind) {
                    case BranchEditKind.Insert:
                        lines.Insert(edit.Index, new TreeNodeViewModel(fresh[edit.Index].Key, fresh[edit.Index].Row));
                        break;
                    case BranchEditKind.Move:
                        lines.Move(edit.From, edit.Index);
                        break;
                    case BranchEditKind.Remove:
                        lines.RemoveAt(edit.Index);
                        break;
                }
            }
        }

        var highlight = state.Highlight(pane);
        string? location = state.Panel(pane).Location;
        string? menuSubject = state.Menu?.Subject is { Kind: TargetKind.PanelRow } subject && subject.Pane == pane
            ? subject.Folder
            : null;
        for (int i = 0; i < fresh.Length; i++) {
            lines[i].Update(fresh[i], highlight, location, menuSubject);
        }
    }
}
