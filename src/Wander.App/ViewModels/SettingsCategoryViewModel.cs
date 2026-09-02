using Wander.App.Resources;

namespace Wander.App.ViewModels;

/// <summary>
/// One section in the settings dialog. The dialog selects the right-pane
/// DataTemplate by the concrete subclass type, so adding a new category
/// is just:
///   1. Create a new <c>FooSettingsCategory : SettingsCategoryViewModel</c>.
///   2. Add a <c>DataTemplate DataType="{x:Type vm:FooSettingsCategory}"</c>
///      in SettingsWindow.xaml.
///   3. Add an instance to <see cref="SettingsViewModel.Categories"/>.
/// No central switch / registry / reflection needed.
/// </summary>
public abstract class SettingsCategoryViewModel : ObservableObject {
    public string Title { get; }

    /// <summary>
    /// Back-pointer to the live settings VM. XAML reads/writes setting
    /// values via <c>{Binding Owner.PropName}</c> so the per-category
    /// VMs stay thin and don't have to forward every property by hand.
    /// </summary>
    public SettingsViewModel Owner { get; }

    protected SettingsCategoryViewModel(string title, SettingsViewModel owner) {
        Title = title;
        Owner = owner;
    }
}


public sealed class GeneralSettingsCategory : SettingsCategoryViewModel {
    public GeneralSettingsCategory(SettingsViewModel owner)
        : base(Strings.SettingsCategoryGeneral, owner) { }
}


public sealed class VisibilitySettingsCategory : SettingsCategoryViewModel {
    public VisibilitySettingsCategory(SettingsViewModel owner)
        : base(Strings.SettingsCategoryVisibility, owner) { }
}


public sealed class LayoutSettingsCategory : SettingsCategoryViewModel {
    public LayoutSettingsCategory(SettingsViewModel owner)
        : base(Strings.SettingsCategoryLayout, owner) { }
}


public sealed class GallerySettingsCategory : SettingsCategoryViewModel {
    public GallerySettingsCategory(SettingsViewModel owner)
        : base(Strings.SettingsCategoryGallery, owner) { }
}


public sealed class ThumbnailsSettingsCategory : SettingsCategoryViewModel {
    public ThumbnailsSettingsCategory(SettingsViewModel owner)
        : base(Strings.SettingsCategoryThumbnails, owner) { }
}


public sealed class BookmarksSettingsCategory : SettingsCategoryViewModel {
    public BookmarksSettingsCategory(SettingsViewModel owner)
        : base(Strings.SettingsCategoryBookmarks, owner) { }
}


public sealed class ContextMenuSettingsCategory : SettingsCategoryViewModel {
    public ContextMenuSettingsCategory(SettingsViewModel owner)
        : base(Strings.SettingsCategoryContextMenu, owner) { }
}


/// <summary>
/// The keyboard reference. Reads rather than edits — see
/// <see cref="HotkeyCatalog"/> for why the two are different tasks.
/// </summary>
public sealed class HotkeysSettingsCategory : SettingsCategoryViewModel {
    public HotkeysSettingsCategory(SettingsViewModel owner)
        : base(Strings.SettingsCategoryHotkeys, owner) { }


    private string _query = string.Empty;
    /// <summary>
    /// What the search field holds. Not persisted: it is a way of looking
    /// through a list, not a preference, and a dialog that reopens still
    /// filtered is a dialog that looks half-empty for no visible reason.
    /// </summary>
    public string Query {
        get => _query;
        set {
            if (SetField(ref _query, value)) {
                Raise(nameof(Groups));
            }
        }
    }

    public IReadOnlyList<HotkeyGroup> Groups => HotkeyCatalog.Filter(_query);
}


public sealed class DebugSettingsCategory : SettingsCategoryViewModel {
    public DebugSettingsCategory(SettingsViewModel owner)
        : base(Strings.SettingsCategoryDebug, owner) { }
}
