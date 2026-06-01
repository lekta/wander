using System.Collections.ObjectModel;
using Wander.Core.Persistence;

namespace Wander.App.ViewModels;

/// <summary>
/// Live, mutable mirror of <see cref="AppSettings"/>. The settings dialog
/// edits this directly via WPF bindings; <see cref="MainViewModel"/> hosts
/// a single instance and persists it through <see cref="AppState"/> on
/// every change.
///
/// Layout decisions:
///  - Properties live here as plain mutable fields with INotifyPropertyChanged
///    so XAML can two-way bind without a converter or proxy.
///  - Categories are exposed as a collection of typed VMs; the dialog binds
///    its left-pane ListBox to <see cref="Categories"/> and the right pane
///    selects a DataTemplate by category VM type.
///  - Each category VM holds a reference to *this* owner so it can read /
///    write settings via <c>{Binding Owner.X}</c> in XAML, avoiding
///    repetitive delegated properties.
/// </summary>
public sealed class SettingsViewModel : ObservableObject {

    // --- General -------------------------------------------------------
    private bool _restoreLastFolder;
    public bool RestoreLastFolder {
        get => _restoreLastFolder;
        set => SetField(ref _restoreLastFolder, value);
    }


    // --- Safety --------------------------------------------------------
    private bool _showHidden;
    public bool ShowHidden {
        get => _showHidden;
        set => SetField(ref _showHidden, value);
    }

    private bool _showSystem;
    public bool ShowSystem {
        get => _showSystem;
        set => SetField(ref _showSystem, value);
    }


    // --- Layout (LargeIcons) ------------------------------------------
    private int _largeIconCellWidth;
    public int LargeIconCellWidth {
        get => _largeIconCellWidth;
        set => SetField(ref _largeIconCellWidth, ClampInt(value, 60, 320));
    }

    private int _largeIconImageSize;
    public int LargeIconImageSize {
        get => _largeIconImageSize;
        set => SetField(ref _largeIconImageSize, ClampInt(value, 24, 256));
    }

    private int _largeIconMargin;
    public int LargeIconMargin {
        get => _largeIconMargin;
        set => SetField(ref _largeIconMargin, ClampInt(value, 0, 32));
    }

    private int _largeIconLabelFontSize;
    public int LargeIconLabelFontSize {
        get => _largeIconLabelFontSize;
        set => SetField(ref _largeIconLabelFontSize, ClampInt(value, 8, 24));
    }


    // --- Bookmarks -----------------------------------------------------
    private bool _showBookmarkDownloads;
    public bool ShowBookmarkDownloads {
        get => _showBookmarkDownloads;
        set => SetField(ref _showBookmarkDownloads, value);
    }

    private bool _showBookmarkDocuments;
    public bool ShowBookmarkDocuments {
        get => _showBookmarkDocuments;
        set => SetField(ref _showBookmarkDocuments, value);
    }

    private bool _showBookmarkPictures;
    public bool ShowBookmarkPictures {
        get => _showBookmarkPictures;
        set => SetField(ref _showBookmarkPictures, value);
    }


    // --- Debug ---------------------------------------------------------
    private bool _showDebugMenu;
    public bool ShowDebugMenu {
        get => _showDebugMenu;
        set => SetField(ref _showDebugMenu, value);
    }


    // --- Category list (used by the dialog) ---------------------------
    public ObservableCollection<SettingsCategoryViewModel> Categories { get; }

    private SettingsCategoryViewModel? _selectedCategory;
    public SettingsCategoryViewModel? SelectedCategory {
        get => _selectedCategory;
        set => SetField(ref _selectedCategory, value);
    }


    public SettingsViewModel() {
        // Initialise from the default AppSettings record so the field
        // defaults stay in one place (the record).
        ApplyFrom(new AppSettings());

        Categories = new ObservableCollection<SettingsCategoryViewModel> {
            new GeneralSettingsCategory(this),
            new SafetySettingsCategory(this),
            new LayoutSettingsCategory(this),
            new BookmarksSettingsCategory(this),
            new DebugSettingsCategory(this),
        };
        _selectedCategory = Categories[0];
    }


    public void ApplyFrom(AppSettings s) {
        // Bulk update without raising for unchanged values; bindings only
        // refresh when something actually shifted.
        RestoreLastFolder = s.RestoreLastFolder;
        ShowHidden = s.ShowHidden;
        ShowSystem = s.ShowSystem;
        LargeIconCellWidth = s.LargeIconCellWidth;
        LargeIconImageSize = s.LargeIconImageSize;
        LargeIconMargin = s.LargeIconMargin;
        LargeIconLabelFontSize = s.LargeIconLabelFontSize;
        ShowBookmarkDownloads = s.ShowBookmarkDownloads;
        ShowBookmarkDocuments = s.ShowBookmarkDocuments;
        ShowBookmarkPictures = s.ShowBookmarkPictures;
        ShowDebugMenu = s.ShowDebugMenu;
    }

    public AppSettings ToRecord() {
        return new AppSettings {
            RestoreLastFolder = RestoreLastFolder,
            ShowHidden = ShowHidden,
            ShowSystem = ShowSystem,
            LargeIconCellWidth = LargeIconCellWidth,
            LargeIconImageSize = LargeIconImageSize,
            LargeIconMargin = LargeIconMargin,
            LargeIconLabelFontSize = LargeIconLabelFontSize,
            ShowBookmarkDownloads = ShowBookmarkDownloads,
            ShowBookmarkDocuments = ShowBookmarkDocuments,
            ShowBookmarkPictures = ShowBookmarkPictures,
            ShowDebugMenu = ShowDebugMenu,
        };
    }


    private static int ClampInt(int value, int min, int max) {
        return Math.Max(min, Math.Min(max, value));
    }
}
