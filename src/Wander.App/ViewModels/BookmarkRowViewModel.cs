using Wander.App.Resources;

namespace Wander.App.ViewModels;

/// <summary>
/// One bookmark on the bookmarks' page: every one there is, the hidden and
/// the absent too. <c>BookmarksController</c> fills it; the tick is the one
/// thing written back through it.
/// </summary>
public sealed class BookmarkRowViewModel : ObservableObject {
    private readonly Action<bool> _setShown;
    private string _name = "";
    private string _iconPath = "";
    private string _place = "";
    private bool _isShown;
    private bool _isDeviceAway;
    private bool _isGone;
    private bool _startsSection;
    private bool _canMoveUp;
    private bool _canMoveDown;
    private bool _isEditing;


    public BookmarkRowViewModel(string path, bool isBuiltIn, Action<bool> setShown) {
        Path = path;
        IsBuiltIn = isBuiltIn;
        _setShown = setShown;
    }


    public string Path { get; }

    /// <summary>A built-in row: switched off, never removed - nothing here to remove it from.</summary>
    public bool IsBuiltIn { get; }

    /// <summary>The name in the panel: the bookmark's own, or the default.</summary>
    public string Name {
        get => _name;
        private set => SetField(ref _name, value);
    }

    public string IconPath {
        get => _iconPath;
        private set => SetField(ref _iconPath, value);
    }

    /// <summary>Where it points, as read: empty for a place with no path to show.</summary>
    public string Place {
        get => _place;
        private set => SetField(ref _place, value);
    }

    /// <summary>The tick: has a row in the panel, whether or not the folder is here now.</summary>
    public bool IsShown {
        get => _isShown;
        set {
            if (SetField(ref _isShown, value)) {
                Raise(nameof(IsDim));
                _setShown(value);
            }
        }
    }

    /// <summary>On a phone or a camera not plugged in.</summary>
    public bool IsDeviceAway {
        get => _isDeviceAway;
        private set {
            if (SetField(ref _isDeviceAway, value)) {
                RaiseState();
            }
        }
    }

    /// <summary>The folder is not on disk: moved, or on a drive not plugged in.</summary>
    public bool IsGone {
        get => _isGone;
        set {
            if (SetField(ref _isGone, value)) {
                RaiseState();
            }
        }
    }

    /// <summary>A probe of the disk is out for it - one at a time, a sleeping share answers late.</summary>
    public bool IsProbing { get; set; }

    /// <summary>Why it is absent; empty while it is here.</summary>
    public string State => IsDeviceAway ? Strings.BookmarksStateDeviceAway : IsGone ? Strings.BookmarksStateGone : "";

    /// <summary>Drawn faded: not in the panel, or nothing there to open.</summary>
    public bool IsDim => !IsShown || IsDeviceAway || IsGone;

    /// <summary>The rule between the built-in rows and the user's own is drawn above it.</summary>
    public bool StartsSection {
        get => _startsSection;
        private set => SetField(ref _startsSection, value);
    }

    public bool CanMoveUp {
        get => _canMoveUp;
        private set => SetField(ref _canMoveUp, value);
    }

    public bool CanMoveDown {
        get => _canMoveDown;
        private set => SetField(ref _canMoveDown, value);
    }

    /// <summary>The name is being edited in place.</summary>
    public bool IsEditing {
        get => _isEditing;
        set => SetField(ref _isEditing, value);
    }


    /// <summary>Brings the row in step with the list. The tick goes in without coming back out as a change.</summary>
    public void Update(string name, string iconPath, string place, bool isShown, bool isDeviceAway, bool startsSection, bool canMoveUp, bool canMoveDown) {
        Name = name;
        IconPath = iconPath;
        Place = place;
        if (_isShown != isShown) {
            _isShown = isShown;
            Raise(nameof(IsShown));
            Raise(nameof(IsDim));
        }
        IsDeviceAway = isDeviceAway;
        StartsSection = startsSection;
        CanMoveUp = canMoveUp;
        CanMoveDown = canMoveDown;
    }


    private void RaiseState() {
        Raise(nameof(State));
        Raise(nameof(IsDim));
    }
}
