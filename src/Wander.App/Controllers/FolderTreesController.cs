using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Wander.App.ViewModels;
using Wander.Core.FileSystem;
using Wander.Core.Navigation;

namespace Wander.App.Controllers;

/// <summary>
/// The two folder panels as one thing: the drives tree it owns outright, and
/// the machinery both panels share — node bookkeeping, where each panel's
/// highlight goes, and expanding either one down to a path.
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
        _ = RefreshForAsync(path);
    }


    /// <summary>
    /// <see cref="RefreshFor"/> that can be waited for: a caller about to
    /// highlight a row the re-read is only now bringing in - a folder just
    /// moved into an open branch - needs the row there first.
    /// </summary>
    public Task RefreshForAsync(string path) {
        return Task.WhenAll(BothPanels().Select(node => node.RefreshBranch(path)));
    }


    /// <summary>
    /// A folder was renamed: every row on it or under it, in either panel,
    /// takes the new path and stays where it is
    /// (<see cref="TreeNodeViewModel.Follow"/>). Called ahead of the
    /// re-read of the level, which then finds the row already under its
    /// new name and keeps it instead of replacing it with a closed one.
    /// </summary>
    public void Follow(string oldRoot, string newRoot) {
        foreach (var node in BothPanels()) {
            node.Follow(oldRoot, newRoot);
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
    ///
    /// <para>
    /// A folder opened from the bookmarks leaves the drives tree's row lit
    /// where it was - dimmed, the tree has no keyboard - and <c>Ctrl+1</c>
    /// into the drives tree goes back to it (decided 2026-09-22). Anything
    /// else lands in the drives tree, and the bookmark row lit before goes
    /// out: the bookmarks never point at a folder that is not open.
    /// <c>IsSelected</c> is two-way bound, so a row left lit stays drawn
    /// lit - which is why the rows are let go of by hand.
    /// </para>
    /// </summary>
    public void ExpandTo(string path, NavigationSource source) {
        IsSyncingSelection = true;
        try {
            Unlight(_bookmarkRows());
            if (source == NavigationSource.Bookmark && TryExpandAndSelect(_bookmarkRows(), path)) {
                return;
            }

            Unlight(Roots);
            TryExpandAndSelect(Roots, path);
        } finally {
            IsSyncingSelection = false;
        }
    }


    /// <summary>
    /// Expands one named panel down to <paramref name="path"/> and selects
    /// its row — what <c>Ctrl+1</c> and <c>Ctrl+Shift+E</c> point the
    /// keyboard at. False when the folder is not reachable in that panel (a
    /// path outside every bookmark, typically); the panel's highlight is
    /// then put back where it was. The other panel keeps its own either way.
    /// </summary>
    public bool RevealIn(NavigationSource panel, string path) {
        IsSyncingSelection = true;
        try {
            var rows = panel == NavigationSource.Bookmark ? _bookmarkRows() : Roots;
            // Let go of before the search: a lit row left behind would be a
            // second highlight in the panel.
            var previous = Unlight(rows);
            if (TryExpandAndSelect(rows, path)) {
                return true;
            }

            if (previous is not null) {
                previous.IsSelected = true;
            }

            return false;
        } finally {
            IsSyncingSelection = false;
        }
    }


    /// <summary>
    /// Moves the highlight of the panel holding <paramref name="node"/> onto
    /// it - a bookmark just moved, which is a new row after the rebuild; a
    /// row a harness scenario points at. The other panel keeps its own.
    /// </summary>
    public void Select(TreeNodeViewModel node) {
        Unlight(Holds(_bookmarkRows(), node) ? _bookmarkRows() : Roots);
        node.IsSelected = true;
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


    private static bool TryExpandAndSelect(IEnumerable<TreeNodeViewModel> nodes, string path) {
        foreach (var node in nodes) {
            if (node.TryExpandToPath(path, select: true)) {
                return true;
            }
        }

        return false;
    }


    /// <summary>
    /// Takes the highlight off the one row of a panel that carries it and
    /// says which that was. Asked of the rows themselves rather than
    /// remembered: the arrow keys move a panel's highlight without asking
    /// anybody, and a remembered row went stale on the first one.
    /// </summary>
    private static TreeNodeViewModel? Unlight(IEnumerable<TreeNodeViewModel> rows) {
        foreach (var row in rows) {
            if (row.FindSelected() is { } lit) {
                lit.IsSelected = false;

                return lit;
            }
        }

        return null;
    }

    /// <summary>True when <paramref name="node"/> is one of <paramref name="rows"/> or inside an open one.</summary>
    private static bool Holds(IEnumerable<TreeNodeViewModel> rows, TreeNodeViewModel node) {
        foreach (var row in rows) {
            if (ReferenceEquals(row, node) || Holds(row.Children, node)) {
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
