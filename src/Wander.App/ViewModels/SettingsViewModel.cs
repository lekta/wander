using System.Collections.ObjectModel;
using Wander.Core.FileSystem;
using Wander.Core.Layout;
using Wander.Core.Menu;
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

    private bool _hideSystemRootFolders;
    public bool HideSystemRootFolders {
        get => _hideSystemRootFolders;
        set => SetField(ref _hideSystemRootFolders, value);
    }

    /// <summary>
    /// The three visibility switches as one value, for handing to a
    /// background enumeration — reading them one by one off the worker
    /// thread would race the settings dialog.
    /// </summary>
    public EntryVisibility Visibility => new(ShowHidden, ShowSystem, HideSystemRootFolders);


    // --- File operations ----------------------------------------------
    private bool _confirmRecycle;
    public bool ConfirmRecycle {
        get => _confirmRecycle;
        set => SetField(ref _confirmRecycle, value);
    }


    // --- Companions ----------------------------------------------------
    private bool _integrateCompanions;
    public bool IntegrateCompanions {
        get => _integrateCompanions;
        set => SetField(ref _integrateCompanions, value);
    }


    // --- Sort ----------------------------------------------------------
    private SortKey _sortKey;
    public SortKey SortKey {
        get => _sortKey;
        set => SetField(ref _sortKey, value);
    }

    private bool _sortAscending;
    public bool SortAscending {
        get => _sortAscending;
        set => SetField(ref _sortAscending, value);
    }

    private bool _groupFoldersFirst;
    public bool GroupFoldersFirst {
        get => _groupFoldersFirst;
        set => SetField(ref _groupFoldersFirst, value);
    }


    // --- Layout (tile views) -------------------------------------------
    private int _largeIconCellWidth;
    public int LargeIconCellWidth {
        get => _largeIconCellWidth;
        set {
            if (SetField(ref _largeIconCellWidth, ClampInt(value, 60, 320))) {
                Raise(nameof(IconsMetrics));
            }
        }
    }

    private int _largeIconImageSize;
    public int LargeIconImageSize {
        get => _largeIconImageSize;
        set {
            if (SetField(ref _largeIconImageSize, ClampInt(value, 24, 256))) {
                Raise(nameof(IconsMetrics));
            }
        }
    }

    private int _largeIconMargin;
    public int LargeIconMargin {
        get => _largeIconMargin;
        set {
            if (SetField(ref _largeIconMargin, ClampInt(value, 0, 32))) {
                Raise(nameof(IconsMetrics));
            }
        }
    }

    private int _largeIconLabelFontSize;
    public int LargeIconLabelFontSize {
        get => _largeIconLabelFontSize;
        set {
            if (SetField(ref _largeIconLabelFontSize, ClampInt(value, 8, 24))) {
                Raise(nameof(IconsMetrics));
            }
        }
    }

    /// <summary>
    /// Cell geometry of the LargeIcons grid. Both the panel and the item
    /// template bind to this one value, so the grid and the tile drawn in it
    /// are the same arithmetic — see <see cref="TileMetrics"/> for why that
    /// matters. Recomputed on read and re-raised whenever one of the four
    /// knobs above moves, which is what makes the dialog resize tiles live.
    /// </summary>
    public TileMetrics IconsMetrics => TileMetrics.ForLargeIcons(
        LargeIconCellWidth, LargeIconImageSize, LargeIconMargin, LargeIconLabelFontSize);

    /// <summary>
    /// The same for Tiles, whose geometry is not user-tunable yet (PLAN A1)
    /// — a constant, but one the panel and the template still read from a
    /// single place.
    /// </summary>
    public TileMetrics TilesMetrics => TileMetrics.ForTiles();


    // --- Thumbnail cache ------------------------------------------------
    private bool _thumbnailDiskCacheEnabled;
    public bool ThumbnailDiskCacheEnabled {
        get => _thumbnailDiskCacheEnabled;
        set => SetField(ref _thumbnailDiskCacheEnabled, value);
    }

    private int _thumbnailDiskCacheMb;
    public int ThumbnailDiskCacheMb {
        get => _thumbnailDiskCacheMb;
        set => SetField(ref _thumbnailDiskCacheMb, ClampInt(value, 16, 8192));
    }

    private int _thumbnailMemoryEntries;
    public int ThumbnailMemoryEntries {
        get => _thumbnailMemoryEntries;
        set => SetField(ref _thumbnailMemoryEntries, ClampInt(value, 64, 8192));
    }

    /// <summary>
    /// Where the cache lives and how big it is right now, refreshed when the
    /// dialog opens and after a clear. Plain text rather than two properties
    /// because it is one sentence on screen.
    /// </summary>
    private string _thumbnailCacheStatus = "";
    public string ThumbnailCacheStatus {
        get => _thumbnailCacheStatus;
        set => SetField(ref _thumbnailCacheStatus, value);
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

    private bool _showBookmarkRecycleBin;
    public bool ShowBookmarkRecycleBin {
        get => _showBookmarkRecycleBin;
        set => SetField(ref _showBookmarkRecycleBin, value);
    }


    // --- Context menu ---------------------------------------------------
    private bool _shellExtensionsEnabled;
    public bool ShellExtensionsEnabled {
        get => _shellExtensionsEnabled;
        set => SetField(ref _shellExtensionsEnabled, value);
    }

    /// <summary>
    /// One checkbox per third-party entry Wander has met so far. Populated
    /// by <see cref="NoteShellExtensions"/> as menus are opened — there is
    /// no way to enumerate installed handlers up front without walking the
    /// registry ourselves, and the shell's own answer is the accurate one.
    /// </summary>
    public ObservableCollection<MenuToggleViewModel> ShellExtensionToggles { get; } = new();

    /// <summary>One checkbox per hideable built-in entry, in catalog order.</summary>
    public ObservableCollection<MenuToggleViewModel> MenuItemToggles { get; } = new();


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
            new FileOperationsSettingsCategory(this),
            new CompanionsSettingsCategory(this),
            new LayoutSettingsCategory(this),
            new ThumbnailsSettingsCategory(this),
            new BookmarksSettingsCategory(this),
            new ContextMenuSettingsCategory(this),
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
        HideSystemRootFolders = s.HideSystemRootFolders;
        ConfirmRecycle = s.ConfirmRecycle;
        IntegrateCompanions = s.IntegrateCompanions;
        SortKey = s.SortKey;
        SortAscending = s.SortAscending;
        GroupFoldersFirst = s.GroupFoldersFirst;
        LargeIconCellWidth = s.LargeIconCellWidth;
        LargeIconImageSize = s.LargeIconImageSize;
        LargeIconMargin = s.LargeIconMargin;
        LargeIconLabelFontSize = s.LargeIconLabelFontSize;
        ThumbnailDiskCacheEnabled = s.ThumbnailDiskCacheEnabled;
        ThumbnailDiskCacheMb = s.ThumbnailDiskCacheMb;
        ThumbnailMemoryEntries = s.ThumbnailMemoryEntries;
        ShowBookmarkDownloads = s.ShowBookmarkDownloads;
        ShowBookmarkDocuments = s.ShowBookmarkDocuments;
        ShowBookmarkPictures = s.ShowBookmarkPictures;
        ShowBookmarkRecycleBin = s.ShowBookmarkRecycleBin;
        ShellExtensionsEnabled = s.ShellExtensionsEnabled;
        RebuildMenuToggles(s.HiddenContextMenuItems);
        RebuildShellToggles(s.KnownShellExtensions, s.BlockedShellExtensions);
        ShowDebugMenu = s.ShowDebugMenu;
    }


    /// <summary>
    /// Records third-party entry names the shell just reported, so the
    /// settings dialog can offer them as checkboxes. Names already known
    /// keep their current state — discovering "7-Zip" again must not
    /// re-enable a 7-Zip the user switched off.
    /// </summary>
    public void NoteShellExtensions(IEnumerable<string> names) {
        bool added = false;
        foreach (string raw in names) {
            string name = ContextMenuSettings.NormalizeName(raw);
            if (name.Length == 0 || FindShellToggle(name) is not null) {
                continue;
            }
            ShellExtensionToggles.Add(new MenuToggleViewModel(name, name, true, OnMenuToggleChanged));
            added = true;
        }

        if (added) {
            OnMenuToggleChanged();
        }
    }

    public AppSettings ToRecord() {
        return new AppSettings {
            RestoreLastFolder = RestoreLastFolder,
            ShowHidden = ShowHidden,
            ShowSystem = ShowSystem,
            HideSystemRootFolders = HideSystemRootFolders,
            ConfirmRecycle = ConfirmRecycle,
            IntegrateCompanions = IntegrateCompanions,
            SortKey = SortKey,
            SortAscending = SortAscending,
            GroupFoldersFirst = GroupFoldersFirst,
            LargeIconCellWidth = LargeIconCellWidth,
            LargeIconImageSize = LargeIconImageSize,
            LargeIconMargin = LargeIconMargin,
            LargeIconLabelFontSize = LargeIconLabelFontSize,
            ThumbnailDiskCacheEnabled = ThumbnailDiskCacheEnabled,
            ThumbnailDiskCacheMb = ThumbnailDiskCacheMb,
            ThumbnailMemoryEntries = ThumbnailMemoryEntries,
            ShowBookmarkDownloads = ShowBookmarkDownloads,
            ShowBookmarkDocuments = ShowBookmarkDocuments,
            ShowBookmarkPictures = ShowBookmarkPictures,
            ShowBookmarkRecycleBin = ShowBookmarkRecycleBin,
            ShellExtensionsEnabled = ShellExtensionsEnabled,
            // Persisted as "what is off", so a future Wander release that
            // adds menu entries shows them by default instead of inheriting
            // an implicit "not in the saved list = hidden".
            HiddenContextMenuItems = MenuItemToggles.Where(t => !t.IsEnabled).Select(t => t.Key).ToArray(),
            BlockedShellExtensions = ShellExtensionToggles.Where(t => !t.IsEnabled).Select(t => t.Key).ToArray(),
            KnownShellExtensions = ContextMenuSettings.TrimKnownExtensions(
                ShellExtensionToggles.Select(t => t.Key),
                ShellExtensionToggles.Where(t => !t.IsEnabled).Select(t => t.Key)),
            ShowDebugMenu = ShowDebugMenu,
        };
    }


    private void RebuildMenuToggles(IReadOnlyList<string> hidden) {
        var off = new HashSet<string>(hidden, StringComparer.OrdinalIgnoreCase);
        MenuItemToggles.Clear();
        foreach (var id in ContextMenuCatalog.Hideable) {
            string key = id.ToString();
            MenuItemToggles.Add(new MenuToggleViewModel(
                key, ContextMenuCatalog.Title(id), !off.Contains(key), OnMenuToggleChanged));
        }
    }

    private void RebuildShellToggles(IReadOnlyList<string> known, IReadOnlyList<string> blocked) {
        var off = new HashSet<string>(
            blocked.Select(ContextMenuSettings.NormalizeName), StringComparer.OrdinalIgnoreCase);

        ShellExtensionToggles.Clear();
        // A blocked name the "known" cache lost still deserves its checkbox,
        // otherwise the user could never turn it back on.
        foreach (string name in known.Concat(blocked).Select(ContextMenuSettings.NormalizeName).Distinct(StringComparer.OrdinalIgnoreCase)) {
            if (name.Length == 0) {
                continue;
            }
            ShellExtensionToggles.Add(new MenuToggleViewModel(name, name, !off.Contains(name), OnMenuToggleChanged));
        }
    }

    private MenuToggleViewModel? FindShellToggle(string name) {
        return ShellExtensionToggles.FirstOrDefault(
            t => string.Equals(t.Key, name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Toggle lists are collections, not properties, so the owner's
    /// "settings changed → persist" hook needs an explicit nudge.
    /// </summary>
    private void OnMenuToggleChanged() {
        Raise(nameof(MenuItemToggles));
    }


    private static int ClampInt(int value, int min, int max) {
        return Math.Max(min, Math.Min(max, value));
    }
}
