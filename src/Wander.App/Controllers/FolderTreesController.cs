using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Wander.App.ViewModels;
using Wander.Core.FileSystem;
using Wander.Core.Navigation;

namespace Wander.App.Controllers;

/// <summary>
/// The two folder panels as one thing: the drives tree it owns outright, and
/// the machinery both panels share — node bookkeeping, the single highlight
/// that moves between them, and expanding either one down to a path.
///
/// <para>
/// The bookmarks panel keeps its own rows (<see cref="BookmarksController"/>
/// builds them from a list only it knows), so this class is handed a way to
/// look at them rather than a copy of them. Everything that has to consider
/// both panels — refreshing, revealing, moving the highlight — lives here
/// exactly because it has to consider both: split between the two owners it
/// would be written twice and agree only by accident.
/// </para>
/// </summary>
public sealed class FolderTreesController {
    private readonly IFileSystem _fs;
    private readonly SettingsViewModel _settings;
    private readonly Func<IEnumerable<TreeNodeViewModel>> _bookmarkRows;
    private TreeNodeViewModel? _selected;


    public FolderTreesController(
        IFileSystem fs, SettingsViewModel settings, Func<IEnumerable<TreeNodeViewModel>> bookmarkRows) {
        _fs = fs;
        _settings = settings;
        _bookmarkRows = bookmarkRows;
    }


    /// <summary>A row was opened or closed, so the session state is stale.</summary>
    public event EventHandler? ExpansionChanged;


    /// <summary>The drives tree. Bound by the lower half of the left panel.</summary>
    public ObservableCollection<TreeNodeViewModel> Roots { get; } = new();

    /// <summary>
    /// True while <see cref="ExpandTo"/> or <see cref="RevealIn"/> is moving
    /// the highlight itself. The panels' selection handlers treat a change
    /// arriving under this flag as an echo of a navigation, not as a click.
    /// It used to be harmless either way, because the row picked was the
    /// current folder and the view drops those; inside an archive the row
    /// is the folder the archive lives in, and taken for a click that echo
    /// navigated straight back out of the archive (2026-09-02).
    /// </summary>
    public bool IsSyncingSelection { get; private set; }


    /// <summary>Fills the drives tree. Called once, at startup.</summary>
    public void LoadRoots() {
        Roots.Clear();
        foreach (var root in _fs.GetRoots()) {
            // Chevron first, question later: asking a drive whether it has
            // subfolders spins it up, and at startup that wait would sit
            // between the user and the first frame. ProbeForChevrons
            // removes the chevron from an empty drive once it has answered.
            var node = new TreeNodeViewModel(
                root.Name, root.FullPath, EntryKind.Drive, _fs, hasChildren: true, _settings);
            Roots.Add(node);
            Wire(node);
        }
        TreeNodeViewModel.ProbeForChevrons(_fs, Roots.ToList());
    }


    /// <summary>
    /// Puts one node and everything under it under this controller's
    /// bookkeeping: expansions get remembered, and children that appear
    /// later — a branch is enumerated the first time it opens — get the same
    /// treatment without anyone having to remember to ask.
    ///
    /// <para>
    /// Public because the bookmarks panel builds nodes of its own and they
    /// have to behave the same way.
    /// </para>
    /// </summary>
    public void Wire(TreeNodeViewModel node) {
        node.PropertyChanged += OnNodePropertyChanged;
        node.Children.CollectionChanged += OnChildrenChanged;
        foreach (var child in node.Children) {
            Wire(child);
        }
    }


    /// <summary>
    /// Re-reads every expanded branch of both panels. What is expanded stays
    /// expanded - <see cref="TreeNodeViewModel.RefreshChildrenAsync"/>
    /// reconciles rather than rebuilds - so this is safe to hang off F5.
    /// </summary>
    public void RefreshAll() {
        foreach (var node in BothPanels()) {
            _ = node.RefreshChildrenAsync();
        }
    }


    /// <summary>
    /// The narrow version: one folder gained or lost a subfolder, so only
    /// the rows standing on that folder are re-read. Both panels can be
    /// showing the same path, and a path can appear twice within one of
    /// them, so this does not stop at the first hit.
    /// </summary>
    public void RefreshFor(string path) {
        foreach (var node in BothPanels()) {
            node.RefreshBranch(path);
        }
    }


    /// <summary>
    /// Opens whichever panel the navigation came from down to
    /// <paramref name="path"/> and highlights the row.
    ///
    /// <para>
    /// Source-aware: a navigation that originated in the bookmarks panel
    /// (including replayed history) re-expands only the bookmarks tree,
    /// never the drives tree. Falls back to drives when the path is no
    /// longer reachable through any bookmark — typically because the user
    /// removed the bookmark since the history entry was recorded.
    /// </para>
    /// </summary>
    public void ExpandTo(string path, NavigationSource source) {
        IsSyncingSelection = true;
        try {
            // Clear the previously selected row first. IsSelected is two-way
            // bound, so leaving it set keeps the prior bookmark/drive row
            // visually highlighted when navigation jumps between panels.
            if (_selected is not null) {
                _selected.IsSelected = false;
                _selected = null;
            }

            bool ok = source == NavigationSource.Bookmark && TryExpandAndSelect(_bookmarkRows(), path);
            if (!ok) {
                TryExpandAndSelect(Roots, path);
            }
        } finally {
            IsSyncingSelection = false;
        }
    }


    /// <summary>
    /// Expands one named panel down to <paramref name="path"/> and selects
    /// its row — what <c>Ctrl+2</c> and <c>Ctrl+Shift+E</c> point the
    /// keyboard at. False when the folder is not reachable in that panel (a
    /// path outside every bookmark, typically); the highlight is then put
    /// back where it was rather than leaving both panels blank.
    /// </summary>
    public bool RevealIn(NavigationSource panel, string path) {
        IsSyncingSelection = true;
        try {
            var previous = _selected;
            if (previous is not null) {
                // Cleared before the search: FindSelected looks for a selected
                // row and would otherwise find this one.
                previous.IsSelected = false;
                _selected = null;
            }

            if (TryExpandAndSelect(panel == NavigationSource.Bookmark ? _bookmarkRows() : Roots, path)) {
                return true;
            }

            if (previous is not null) {
                previous.IsSelected = true;
                _selected = previous;
            }

            return false;
        } finally {
            IsSyncingSelection = false;
        }
    }


    /// <summary>
    /// Moves the highlight onto one row. <c>IsSelected</c> is two-way bound,
    /// so whatever had it has to be told to let go — otherwise two rows stay
    /// highlighted once the panel is rebuilt underneath them.
    /// </summary>
    public void Select(TreeNodeViewModel node) {
        if (_selected is not null) {
            _selected.IsSelected = false;
        }
        node.IsSelected = true;
        _selected = node;
    }


    /// <summary>
    /// Every expanded path in both panels, tagged with its panel.
    ///
    /// <para>
    /// Deduped on (path, panel). The same path can legitimately appear in
    /// both — a user favourite that is also reachable through drives — and
    /// those are two separate expansion states, so both are kept.
    /// </para>
    /// </summary>
    public List<NavigationStop> CollectExpanded() {
        var result = new List<NavigationStop>();
        foreach (var root in Roots) {
            root.CollectExpanded(result, NavigationSource.Drives);
        }
        foreach (var bookmark in _bookmarkRows()) {
            bookmark.CollectExpanded(result, NavigationSource.Bookmark);
        }

        return result.Distinct().ToList();
    }


    private IEnumerable<TreeNodeViewModel> BothPanels() {
        return Roots.Concat(_bookmarkRows());
    }


    private bool TryExpandAndSelect(IEnumerable<TreeNodeViewModel> nodes, string path) {
        foreach (var node in nodes) {
            if (node.TryExpandToPath(path, select: true)) {
                _selected = node.FindSelected();

                return true;
            }
        }

        return false;
    }


    private void OnChildrenChanged(object? sender, NotifyCollectionChangedEventArgs e) {
        if (e.NewItems is null) {
            return;
        }

        foreach (TreeNodeViewModel added in e.NewItems) {
            Wire(added);
        }
    }


    private void OnNodePropertyChanged(object? sender, PropertyChangedEventArgs e) {
        if (e.PropertyName == nameof(TreeNodeViewModel.IsExpanded)) {
            ExpansionChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
