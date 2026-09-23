using Wander.Core.Panels;
using Wander.Core.Workspace;

namespace Wander.App.ViewModels;

/// <summary>
/// One line of a folder panel as drawn (REDESIGN 4.7, decision P3): the
/// model's row (<see cref="PanelRow"/>) and what the panel's state says of
/// it - open, under the cursor, the open folder's place, what a menu is
/// open for. A projection and nothing more: it reads no disk and decides
/// nothing, <c>FolderTreesController</c> fills it from the model, and WPF
/// only reads it - every binding on it is one-way.
/// </summary>
public sealed class TreeNodeViewModel : ObservableObject {
    private PanelRow _row;
    private int _depth;
    private bool _isExpanded;
    private bool _isCaret;
    private bool _isActive;
    private bool _isLocation;
    private bool _isMenuSubject;


    public TreeNodeViewModel(string key, PanelRow row) {
        Key = key;
        _row = row;
    }


    /// <summary>The line's identity from one drawing to the next (<see cref="VisibleRow.Key"/>).</summary>
    public string Key { get; }

    /// <summary>The row, as the model has it now.</summary>
    public PanelRow Row => _row;

    public string FullPath => _row.Path;

    /// <summary>The label: the folder's name, or a bookmark's own title.</summary>
    public string Name => _row.Name;

    public PanelRowKind Kind => _row.Kind;

    /// <summary>A hidden folder: drawn faded.</summary>
    public bool IsHidden => _row.IsHidden;

    /// <summary>A bookmark whose folder is gone: greyed and italic, no chevron.</summary>
    public bool IsMissing => _row.IsMissing;

    /// <summary>One of the user's own bookmarks: the row's "..." menu, and Ctrl+Up/Down move it.</summary>
    public bool IsRemovableBookmark => _row.Role == PanelRowRole.OwnBookmark;

    /// <summary>A special folder the settings switch on: Delete switches it off.</summary>
    public bool IsBuiltInBookmark => _row.Role == PanelRowRole.BuiltInBookmark;

    /// <summary>The rule between the special folders and the user's own bookmarks is drawn above it.</summary>
    public bool StartsUserSection => _row.StartsSection;

    /// <summary>A chevron is drawn.</summary>
    public bool HasChevron => _row.HasChevron;

    /// <summary>How deep the line sits: the indent.</summary>
    public int Depth {
        get => _depth;
        private set => SetField(ref _depth, value);
    }

    /// <summary>Open: its level is drawn under it.</summary>
    public bool IsExpanded {
        get => _isExpanded;
        private set => SetField(ref _isExpanded, value);
    }

    /// <summary>Under the panel's cursor: the lit row.</summary>
    public bool IsCaret {
        get => _isCaret;
        private set => SetField(ref _isCaret, value);
    }

    /// <summary>Lit as the active row - the keyboard is in this panel; otherwise the light is the inactive one.</summary>
    public bool IsActive {
        get => _isActive;
        private set => SetField(ref _isActive, value);
    }

    /// <summary>The open folder's place: its name is drawn bold (decision B24).</summary>
    public bool IsLocation {
        get => _isLocation;
        private set => SetField(ref _isLocation, value);
    }

    /// <summary>A context menu is open for it: framed with the caret's brush, the light staying where it was (decision B4).</summary>
    public bool IsMenuSubject {
        get => _isMenuSubject;
        private set => SetField(ref _isMenuSubject, value);
    }


    /// <summary>
    /// Brings the line in step with the model: the row it shows now, and what
    /// the panel's state says of it. Only what changed is raised.
    /// </summary>
    public void Update(VisibleRow line, PanelHighlight highlight, string? location, string? menuSubject) {
        if (!ReferenceEquals(_row, line.Row)) {
            var before = _row;
            _row = line.Row;
            RaiseRowChanges(before, line.Row);
        }

        Depth = line.Depth;
        IsExpanded = line.IsExpanded;
        IsCaret = PanelPaths.Same(highlight.Row, line.Path);
        IsActive = IsCaret && highlight.Active;
        IsLocation = PanelPaths.Same(location, line.Path);
        IsMenuSubject = PanelPaths.Same(menuSubject, line.Path);
    }


    private void RaiseRowChanges(PanelRow before, PanelRow after) {
        if (before.Path != after.Path) {
            Raise(nameof(FullPath));
        }
        if (before.Name != after.Name) {
            Raise(nameof(Name));
        }
        if (before.Kind != after.Kind) {
            Raise(nameof(Kind));
        }
        if (before.IsHidden != after.IsHidden) {
            Raise(nameof(IsHidden));
        }
        if (before.IsMissing != after.IsMissing) {
            Raise(nameof(IsMissing));
        }
        if (before.Role != after.Role) {
            Raise(nameof(IsRemovableBookmark));
            Raise(nameof(IsBuiltInBookmark));
        }
        if (before.StartsSection != after.StartsSection) {
            Raise(nameof(StartsUserSection));
        }
        if (before.HasChevron != after.HasChevron) {
            Raise(nameof(HasChevron));
        }
        Raise(nameof(Row));
    }
}
