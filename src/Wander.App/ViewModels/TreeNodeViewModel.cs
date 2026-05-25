using System.Collections.ObjectModel;
using System.IO;
using Wander.Core.FileSystem;

namespace Wander.App.ViewModels;

public sealed class TreeNodeViewModel : ObservableObject {
    private static readonly TreeNodeViewModel _placeholder = new("__placeholder__", "", EntryKind.Directory, null, hasChildren: false);

    private readonly IFileSystem? _fs;
    private bool _isExpanded;
    private bool _isSelected;
    private bool _loaded;


    public TreeNodeViewModel(string name, string fullPath, EntryKind kind, IFileSystem? fs, bool hasChildren) {
        Name = name;
        FullPath = fullPath;
        Kind = kind;
        _fs = fs;
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
    /// Recursively expands nodes along the way to <paramref name="targetPath"/> and (optionally)
    /// selects the deepest matching node. Returns true if the target was reached.
    /// </summary>
    public bool TryExpandToPath(string targetPath, bool select = true) {
        if (string.IsNullOrEmpty(FullPath) || string.IsNullOrEmpty(targetPath)) {
            return false;
        }

        if (!IsUnderOrEqual(targetPath, FullPath)) {
            return false;
        }

        if (PathsEqual(targetPath, FullPath)) {
            if (select) {
                IsSelected = true;
            }
            return true;
        }

        IsExpanded = true;

        foreach (var child in Children) {
            if (child.TryExpandToPath(targetPath, select)) {
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

                bool childHasChildren = _fs.HasSubdirectories(entry.FullPath);
                Children.Add(new TreeNodeViewModel(entry.Name, entry.FullPath, EntryKind.Directory, _fs, childHasChildren));
            }
        } catch {
            // access denied / unavailable — silently skip; UI will show empty
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
