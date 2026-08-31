using System.Collections.ObjectModel;
using System.IO;
using Wander.Core.FileSystem;

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


    private void EnsureLoaded() {
        if (_loaded || _fs is null) {
            return;
        }

        _loaded = true;
        Children.Clear();

        try {
            foreach (var entry in _fs.Enumerate(FullPath)) {
                if (entry.Kind != EntryKind.Directory) {
                    continue;
                }
                if (!IsAllowedByFilters(entry)) {
                    continue;
                }

                bool childHasChildren = _fs.HasSubdirectories(entry.FullPath);
                Children.Add(new TreeNodeViewModel(
                    entry.Name, entry.FullPath, EntryKind.Directory, _fs, childHasChildren,
                    _settings, entry.IsHidden));
            }
        } catch {
            // access denied / unavailable — silently skip; UI will show empty
        }
    }

    private bool IsAllowedByFilters(FileSystemEntry entry) {
        return _settings is null || _settings.Visibility.Allows(entry);
    }


    /// <summary>
    /// Drop the cached children and re-enumerate using the current settings
    /// filters. Recurses through already-loaded subtrees so a single toggle of
    /// ShowHidden/ShowSystem refreshes the whole expanded portion of the tree.
    /// Unloaded branches stay lazy — they'll re-evaluate filters whenever the
    /// user expands them next.
    /// </summary>
    public void RefreshChildren() {
        if (!_loaded) {
            return;
        }

        _loaded = false;
        Children.Clear();

        if (_isExpanded) {
            EnsureLoaded();
            foreach (var child in Children) {
                child.RefreshChildren();
            }
        } else if (_fs is not null && _fs.HasSubdirectories(FullPath)) {
            // Collapsed: restore the placeholder so the chevron still renders.
            Children.Add(_placeholder);
        }
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
