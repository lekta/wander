using System.Collections.ObjectModel;
using System.IO;
using Wander.Core;
using Wander.Core.Diagnostics;
using Wander.Core.FileSystem;
using Wander.Core.Logging;
using Wander.Core.Navigation;
using Wander.Core.Shell;

namespace Wander.App.ViewModels;

public sealed class TreeNodeViewModel : ObservableObject {
    private static readonly TreeNodeViewModel _placeholder = new("__placeholder__", "", EntryKind.Directory, null, hasChildren: false);

    private readonly IFileSystem? _fs;
    private readonly SettingsViewModel? _settings;
    private bool _isExpanded;
    private bool _isSelected;
    private bool _loaded;


    public TreeNodeViewModel(string name, string fullPath, EntryKind kind, IFileSystem? fs, bool hasChildren, SettingsViewModel? settings = null, bool isHidden = false) {
        Name = name;
        FullPath = fullPath;
        Kind = kind;
        _fs = fs;
        _settings = settings;
        IsHidden = isHidden;
        Children = new ObservableCollection<TreeNodeViewModel>();

        // Placeholder so WPF draws the expander chevron before we lazy-load.
        // No placeholder for leaf folders (HasSubdirectories=false) — no chevron drawn.
        if (hasChildren) {
            Children.Add(_placeholder);
        }
    }


    public string Name { get; }
    public string FullPath { get; }
    public EntryKind Kind { get; }
    public bool IsHidden { get; }
    public ObservableCollection<TreeNodeViewModel> Children { get; }

    /// <summary>
    /// Whether this node stands on a real filesystem folder — what makes it
    /// worth including in a <see cref="ProbeForChevrons"/> pass. Shell
    /// namespaces and missing bookmarks are built without one.
    /// </summary>
    internal bool HasFileSystem => _fs is not null;

    /// <summary>
    /// Bound to TreeViewItem.IsExpanded TwoWay. Wander never auto-collapses the tree
    /// without an explicit user gesture — programmatic <c>false</c> only happens
    /// when the user Alt-clicks the chevron (recursive collapse).
    /// </summary>
    public bool IsExpanded {
        get => _isExpanded;
        set {
            if (!SetField(ref _isExpanded, value)) {
                return;
            }

            if (_isExpanded) {
                EnsureLoaded();
            }
        }
    }

    /// <summary>Bound to TreeViewItem.IsSelected TwoWay so we can move selection programmatically.</summary>
    public bool IsSelected {
        get => _isSelected;
        set => SetField(ref _isSelected, value);
    }

    /// <summary>
    /// True for nodes that represent a user-defined bookmark (vs. a special
    /// folder like "This PC" or "Downloads" that's toggled via settings).
    /// Controls visibility of the "Remove from bookmarks" context menu.
    /// </summary>
    public bool IsRemovableBookmark { get; init; }

    /// <summary>
    /// The first user bookmark under the special folders — the row that
    /// draws the thin divider between the two halves of the panel.
    ///
    /// <para>
    /// A flag on the row rather than a separator item in the list: a fake
    /// node would have to be stepped over by every walk of the tree
    /// (expansion, keyboard, save and restore) and by the drop pipeline.
    /// </para>
    /// </summary>
    public bool StartsUserSection { get; init; }

    /// <summary>
    /// The bookmark points at a folder that is no longer there. The row
    /// stays in the panel — a bookmark that silently vanishes reads as
    /// "Wander lost my bookmark" — but greyed out and without a chevron,
    /// and clicking it explains itself in the file area.
    /// </summary>
    public bool IsMissing { get; init; }


    /// <summary>
    /// Recursively expands nodes along the way to <paramref name="targetPath"/> and (optionally)
    /// selects the deepest matching node. Returns true if the target was reached.
    /// </summary>
    /// <param name="select">
    /// Set <see cref="IsSelected"/> on the target node — used for "navigate to this folder"
    /// so the row is highlighted in the tree.
    /// </param>
    /// <param name="expandTarget">
    /// Set <see cref="IsExpanded"/> on the target node itself — used for restoring saved
    /// expansion state, where "this path was expanded" literally means its children should
    /// be visible. Default <c>false</c>: walking to a node usually means "show that row",
    /// not "show its children".
    /// </param>
    public bool TryExpandToPath(string targetPath, bool select = true, bool expandTarget = false) {
        if (string.IsNullOrEmpty(FullPath) || string.IsNullOrEmpty(targetPath)) {
            return false;
        }

        if (!IsUnderOrEqual(targetPath, FullPath)) {
            return false;
        }

        if (PathsEqual(targetPath, FullPath)) {
            if (expandTarget) {
                IsExpanded = true;
            }
            if (select) {
                IsSelected = true;
            }
            return true;
        }

        IsExpanded = true;

        foreach (var child in Children) {
            if (child.TryExpandToPath(targetPath, select, expandTarget)) {
                return true;
            }
        }

        return false;
    }


    /// <summary>
    /// Finds which of <paramref name="nodes"/> have no subfolders and takes
    /// their chevrons away — off the UI thread, because the question is one
    /// disk enumeration per node and the nodes were just drawn assuming
    /// "yes". The optimistic default is Explorer's own behaviour: a chevron
    /// that vanishes a moment later is barely visible, a window that stops
    /// answering while a drive spins up is not.
    /// </summary>
    internal static void ProbeForChevrons(IFileSystem fs, IReadOnlyList<TreeNodeViewModel> nodes) {
        if (nodes.Count == 0) {
            return;
        }

        _ = Task.Run(() => {
            var leaves = new List<TreeNodeViewModel>();
            foreach (var node in nodes) {
                // An archive keeps its chevron unasked. Answering would mean
                // opening every archive in the folder through the shell, in
                // the background, on every expansion; an empty one loses
                // the chevron the moment it is expanded instead.
                if (Archives.Contains(node.FullPath)) {
                    continue;
                }
                if (!fs.HasSubdirectories(node.FullPath)) {
                    leaves.Add(node);
                }
            }

            if (leaves.Count == 0) {
                return;
            }

            System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => {
                foreach (var leaf in leaves) {
                    leaf.SetLeaf();
                }
            });
        });
    }


    /// <summary>
    /// Re-reads this node's subfolders and recurses through the already-loaded
    /// subtree, so one call brings the whole expanded portion of the panel back
    /// in line with the disk. Unloaded branches stay lazy — they enumerate
    /// whenever the user expands them next.
    ///
    /// <para>
    /// Reconciles rather than rebuilds, and that is the whole point: rows that
    /// are still there keep their view models, so what was expanded stays
    /// expanded and what was selected stays selected. Clearing and re-filling
    /// would collapse every branch below this one, which is exactly the Win11
    /// Explorer behaviour Wander exists to not have.
    /// </para>
    ///
    /// <para>
    /// The level is read on the pool. F5 walks every expanded branch of
    /// both panels, and on a share or a drive that has gone to sleep each
    /// one was a wait the window sat through; the reconciliation itself
    /// stays on the UI thread, where the collection is bound.
    /// </para>
    /// </summary>
    public async Task RefreshChildrenAsync() {
        if (!_loaded) {
            return;
        }

        if (!_isExpanded) {
            // Collapsed: nothing is on screen to keep, so drop the cache and
            // leave the placeholder that draws the chevron. Whether the
            // chevron is still deserved is answered off the UI thread, same
            // as everywhere else.
            _loaded = false;
            Children.Clear();
            if (_fs is not null) {
                Children.Add(_placeholder);
                ProbeForChevrons(_fs, new[] { this });
            }

            return;
        }

        // Watched: a share on a sleeping disk or a RAR the shell has to
        // open answer in seconds, and a spinner that spins for seconds is
        // otherwise nowhere in the log.
        var fresh = await LongWait.WatchAsync(
            Task.Run(ReadChildFolders),
            ServiceLocator.TryGet<ILogger>() ?? NullLogger.Instance,
            $"tree: listing {FullPath}");
        if (!_loaded) {
            // Collapsed and dropped while the disk was answering: the
            // placeholder is what belongs there now, not these rows.
            return;
        }

        List<TreeNodeViewModel>? added = null;
        for (int i = 0; i < fresh.Count; i++) {
            int existing = IndexOfChild(fresh[i].FullPath, i);
            if (existing < 0) {
                Children.Insert(i, fresh[i]);
                (added ??= new List<TreeNodeViewModel>()).Add(fresh[i]);
            } else if (existing != i) {
                Children.Move(existing, i);
            }
        }
        while (Children.Count > fresh.Count) {
            Children.RemoveAt(Children.Count - 1);
        }

        if (added is not null && _fs is not null) {
            ProbeForChevrons(_fs, added);
        }

        foreach (var child in Children) {
            _ = child.RefreshChildrenAsync();
        }
    }


    private void EnsureLoaded() {
        if (_loaded || _fs is null) {
            return;
        }

        _loaded = true;
        Children.Clear();
        var children = ReadChildFolders();
        foreach (var child in children) {
            Children.Add(child);
        }
        ProbeForChevrons(_fs, children);
    }


    /// <summary>
    /// The subfolders this node should be showing right now, as fresh view
    /// models - and the archives the shell opens as folders, standing among
    /// them by name the way Explorer's navigation pane has them. Enumeration
    /// failure (access denied, drive pulled out, a broken archive) is not
    /// an error to report here — the node simply has no children to draw.
    /// </summary>
    private List<TreeNodeViewModel> ReadChildFolders() {
        var folders = new List<TreeNodeViewModel>();
        if (_fs is null) {
            return folders;
        }

        try {
            if (Archives.Contains(FullPath)) {
                ReadArchiveFolders(folders);

                return folders;
            }

            var archives = new List<TreeNodeViewModel>();
            foreach (var entry in _fs.Enumerate(FullPath)) {
                bool isFolder = entry.Kind == EntryKind.Directory;
                bool isArchive = !isFolder
                    && entry.Kind == EntryKind.File
                    && Archives.Of(entry.FullPath) is { IsRoot: true };
                if (!isFolder && !isArchive) {
                    continue;
                }
                if (!IsAllowedByFilters(entry)) {
                    continue;
                }

                (isFolder ? folders : archives).Add(Child(entry));
            }

            // The listing comes folders first and files after; a row for an
            // archive belongs among the folders, where the eye looks for it.
            // Inserted rather than re-sorted, so the folders keep whatever
            // order the filesystem gave them.
            foreach (var archive in archives) {
                int at = folders.FindIndex(f => string.Compare(archive.Name, f.Name, StringComparison.OrdinalIgnoreCase) < 0);
                folders.Insert(at < 0 ? folders.Count : at, archive);
            }
        } catch {
            // access denied / unavailable — silently skip; UI will show empty
        }

        return folders;
    }

    /// <summary>
    /// The folders inside an archive, or inside a folder of one: what the
    /// shell lists, nothing else - there is no filesystem to ask. Files are
    /// left out as everywhere in the tree.
    /// </summary>
    private void ReadArchiveFolders(List<TreeNodeViewModel> into) {
        if (ServiceLocator.TryGet<IShellNamespace>() is not { } shell) {
            return;
        }

        foreach (var entry in shell.Enumerate(FullPath)) {
            if (entry.Kind == EntryKind.Directory) {
                into.Add(Child(entry));
            }
        }
    }

    /// <summary>
    /// One child row. Every child starts with a chevron; ProbeForChevrons
    /// takes it off the leaves a beat later. Asking the disk here — one
    /// enumeration per child, on the UI thread — is what made expanding a
    /// branch on a slow drive freeze the window.
    /// </summary>
    private TreeNodeViewModel Child(FileSystemEntry entry) {
        return new TreeNodeViewModel(
            entry.Name, entry.FullPath, EntryKind.Directory, _fs, hasChildren: true,
            _settings, entry.IsHidden);
    }


    /// <summary>
    /// The probe's verdict landing: no subfolders, so no chevron. A node
    /// that loaded for real in the meantime knows better and is left alone.
    /// </summary>
    private void SetLeaf() {
        if (_loaded) {
            return;
        }

        if (Children.Count == 1 && ReferenceEquals(Children[0], _placeholder)) {
            Children.Clear();
        }
    }

    private bool IsAllowedByFilters(FileSystemEntry entry) {
        return _settings is null || _settings.Visibility.Allows(entry);
    }


    // --- Walks over the branch -------------------------------------------
    //
    // Five recursions used to live in two other files — three of them in
    // MainWindow's code-behind, where a tree walk is not a window's business.
    // They are here because the shape being walked is this one: a node knows
    // what its children are, and nobody else should have to.


    /// <summary>
    /// Adds every expanded path in this branch to <paramref name="result"/>,
    /// tagged with the panel it belongs to.
    ///
    /// <para>
    /// Stops at the first collapsed row rather than walking past it.
    /// Collapsing a row does not clear the flags inside it — reopening it in
    /// the same session is supposed to show what was open before. Saving
    /// those hidden descendants, though, made restore expand its way down to
    /// each of them, and expanding a descendant expands its parents: the
    /// branch the user had just closed came back open on the next start.
    /// </para>
    /// </summary>
    public void CollectExpanded(List<NavigationStop> result, NavigationSource source) {
        if (!IsExpanded) {
            return;
        }

        if (!string.IsNullOrEmpty(FullPath)) {
            result.Add(new NavigationStop(FullPath, source));
        }
        foreach (var child in Children) {
            child.CollectExpanded(result, source);
        }
    }


    /// <summary>The selected row inside this branch, or null.</summary>
    public TreeNodeViewModel? FindSelected() {
        if (IsSelected) {
            return this;
        }

        foreach (var child in Children) {
            if (child.FindSelected() is { } found) {
                return found;
            }
        }

        return null;
    }


    /// <summary>
    /// Re-reads the row standing on <paramref name="path"/> anywhere in this
    /// branch. A path can appear more than once, so this does not stop at
    /// the first hit at any one level.
    /// </summary>
    public void RefreshBranch(string path) {
        if (PathsEqual(FullPath, path)) {
            _ = RefreshChildrenAsync();

            return;
        }

        // Snapshot: a match further down rebuilds its own Children, never
        // this level's, but the enumerator is cheap enough not to argue with.
        foreach (var child in Children.ToArray()) {
            child.RefreshBranch(path);
        }
    }


    /// <summary>Opens the immediate children, one level and no further.</summary>
    public void ExpandChildren() {
        foreach (var child in Children) {
            if (string.IsNullOrEmpty(child.FullPath)) {
                continue;
            }
            child.IsExpanded = true;
        }
    }


    /// <summary>
    /// Closes everything below this row, deepest first, leaving the row
    /// itself as it is.
    /// </summary>
    public void CollapseDescendants() {
        foreach (var child in Children) {
            if (child.IsExpanded) {
                child.CollapseDescendants();
                child.IsExpanded = false;
            }
        }
    }


    private int IndexOfChild(string path, int from) {
        for (int i = from; i < Children.Count; i++) {
            if (PathsEqual(Children[i].FullPath, path)) {
                return i;
            }
        }

        return -1;
    }


    private static bool PathsEqual(string a, string b) {
        return string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsUnderOrEqual(string candidate, string anchor) {
        string c = Normalize(candidate);
        string a = Normalize(anchor);

        if (string.Equals(c, a, StringComparison.OrdinalIgnoreCase)) {
            return true;
        }

        string prefix = a.EndsWith(Path.DirectorySeparatorChar) ? a : a + Path.DirectorySeparatorChar;
        return c.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalize(string path) {
        return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}
