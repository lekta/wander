using System.Collections.ObjectModel;
using Wander.Core.FileSystem;

namespace Wander.App.ViewModels;

public sealed class TreeNodeViewModel : ObservableObject {
    private static readonly TreeNodeViewModel _placeholder = new("__placeholder__", "", EntryKind.Directory, null);

    private readonly IFileSystem? _fs;
    private bool _isExpanded;
    private bool _loaded;


    public TreeNodeViewModel(string name, string fullPath, EntryKind kind, IFileSystem? fs) {
        Name = name;
        FullPath = fullPath;
        Kind = kind;
        _fs = fs;
        Children = new ObservableCollection<TreeNodeViewModel> { _placeholder };
    }


    public string Name { get; }
    public string FullPath { get; }
    public EntryKind Kind { get; }
    public ObservableCollection<TreeNodeViewModel> Children { get; }

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


    private void EnsureLoaded() {
        if (_loaded || _fs is null) {
            return;
        }

        _loaded = true;
        Children.Clear();

        try {
            foreach (var entry in _fs.Enumerate(FullPath)) {
                if (entry.Kind == EntryKind.Directory) {
                    Children.Add(new TreeNodeViewModel(entry.Name, entry.FullPath, EntryKind.Directory, _fs));
                }
            }
        } catch {
            // access denied / unavailable — silently skip; UI will show empty
        }
    }
}
