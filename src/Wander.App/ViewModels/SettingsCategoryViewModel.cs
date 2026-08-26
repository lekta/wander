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


public sealed class SafetySettingsCategory : SettingsCategoryViewModel {
    public SafetySettingsCategory(SettingsViewModel owner)
        : base(Strings.SettingsCategorySafety, owner) { }
}


public sealed class FileOperationsSettingsCategory : SettingsCategoryViewModel {
    public FileOperationsSettingsCategory(SettingsViewModel owner)
        : base(Strings.SettingsCategoryFileOps, owner) { }
}


public sealed class CompanionsSettingsCategory : SettingsCategoryViewModel {
    public CompanionsSettingsCategory(SettingsViewModel owner)
        : base(Strings.SettingsCategoryCompanions, owner) { }
}


public sealed class LayoutSettingsCategory : SettingsCategoryViewModel {
    public LayoutSettingsCategory(SettingsViewModel owner)
        : base(Strings.SettingsCategoryLayout, owner) { }
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


public sealed class DebugSettingsCategory : SettingsCategoryViewModel {
    public DebugSettingsCategory(SettingsViewModel owner)
        : base(Strings.SettingsCategoryDebug, owner) { }
}
