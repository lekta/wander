using System.Collections.ObjectModel;
using System.Windows.Media;
using Wander.App.Resources;
using Wander.Core;
using Wander.Core.Actions;
using Wander.Core.Companions;
using Wander.Core.FileSystem;
using Wander.Core.Folders;
using Wander.Core.Imaging;
using Wander.Core.Layout;
using Wander.Core.Menu;
using Wander.Core.Persistence;
using Wander.Core.Shell;

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
    /// <summary>How much of the next picture the gallery preview shows - see <see cref="GalleryPreviewWidth"/>.</summary>
    private const int GalleryPreviewEdge = 16;


    public SettingsViewModel() {
        // Initialise from the default AppSettings record so the field
        // defaults stay in one place (the record).
        ApplyFrom(new AppSettings());

        Categories = new ObservableCollection<SettingsCategoryViewModel> {
            // Every page is a part of Wander as the user meets it, in three
            // runs: what is on screen, what is done with files, the
            // machinery (2026-09-25). No "Основное": a page named after
            // nothing collected whatever had no better place, and the
            // tree's arrows sat next to the delete question and the temp
            // folder. A page about a part of another is under it, indented
            // (2026-09-28): the view's sizes and the gallery under the view;
            // the menu, the actions with their programs and the ratings -
            // set in every view, written next to the photo - under file
            // operations. Rules - ARCHITECTURE, "Настройки".
            new FoldersSettingsCategory(this),
            new ListSettingsCategory(this),
            new ViewsSettingsCategory(this),
            new SizesSettingsCategory(this),
            new GallerySettingsCategory(this),
            new OperationsSettingsCategory(this),
            new ContextMenuSettingsCategory(this),
            new ActionsSettingsCategory(this),
            new ToolsSettingsCategory(this),
            new RatingsSettingsCategory(this),
            new CacheSettingsCategory(this),
            new HotkeysSettingsCategory(this),
            new DebugSettingsCategory(this),
        };
        _selectedCategory = Categories[0];
    }


    // --- General -------------------------------------------------------
    private bool _restoreLastFolder;
    /// <summary>A session starts in the last folder; otherwise in the working folder (2026-09-28; the first drive before).</summary>
    public bool RestoreLastFolder {
        get => _restoreLastFolder;
        set {
            if (SetField(ref _restoreLastFolder, value)) {
                Raise(nameof(StartsInWorkFolder));
            }
        }
    }

    /// <summary>The other of the page's two startup choices; stored as <see cref="RestoreLastFolder"/>.</summary>
    public bool StartsInWorkFolder {
        get => !_restoreLastFolder;
        set => RestoreLastFolder = !value;
    }

    private string _workFolder = "";
    /// <summary>The user's own choice of working folder; empty for the system Documents - see <see cref="AppSettings.WorkFolder"/>.</summary>
    public string WorkFolder {
        get => _workFolder;
        set {
            if (SetField(ref _workFolder, value ?? "")) {
                Raise(nameof(WorkFolderText));
                Raise(nameof(HasCustomWorkFolder));
            }
        }
    }

    public bool HasCustomWorkFolder => _workFolder.Length > 0;

    /// <summary>What the settings page shows: the chosen folder, or the system one it stands for.</summary>
    public string WorkFolderText => HasCustomWorkFolder
        ? _workFolder
        : string.Format(Strings.SettingsWorkFolderDefault, SystemDocuments() ?? "");

    /// <summary>
    /// The working folder as it stands now: the user's choice, or the
    /// system Documents when there is none. Whether it exists is the
    /// caller's question.
    /// </summary>
    public string? ResolveWorkFolder() {
        return HasCustomWorkFolder ? _workFolder : SystemDocuments();
    }


    // --- Behaviour ------------------------------------------------------
    private bool _autoRefresh;
    public bool AutoRefresh {
        get => _autoRefresh;
        set => SetField(ref _autoRefresh, value);
    }

    private bool _visibleFirstLoading;
    /// <summary>Icons for what is on screen load first - see <see cref="AppSettings.VisibleFirstLoading"/>.</summary>
    public bool VisibleFirstLoading {
        get => _visibleFirstLoading;
        set => SetField(ref _visibleFirstLoading, value);
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
        set {
            if (SetField(ref _hideSystemRootFolders, value)) {
                Raise(nameof(ShowSystemRootFolders));
            }
        }
    }

    /// <summary>
    /// The same switch the other way up, for the settings dialog.
    ///
    /// <para>
    /// The stored flag is "hide", because that is the behaviour being turned
    /// on and the default is true. The dialog lists it next to two "show"
    /// checkboxes, and a column where two boxes mean "show" and the third
    /// means "hide" is a column that gets misread. Inverting here rather
    /// than in <c>AppSettings</c> keeps the saved file saying what it means.
    /// </para>
    /// </summary>
    public bool ShowSystemRootFolders {
        get => !HideSystemRootFolders;
        set => HideSystemRootFolders = !value;
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

    private bool _confirmMove;
    /// <inheritdoc cref="AppSettings.ConfirmMove"/>
    public bool ConfirmMove {
        get => _confirmMove;
        set => SetField(ref _confirmMove, value);
    }

    private bool _skipIdenticalOnConflict;
    /// <inheritdoc cref="AppSettings.SkipIdenticalOnConflict"/>
    public bool SkipIdenticalOnConflict {
        get => _skipIdenticalOnConflict;
        set => SetField(ref _skipIdenticalOnConflict, value);
    }

    private bool _dragHoverEntersFolders;
    /// <inheritdoc cref="AppSettings.DragHoverEntersFolders"/>
    public bool DragHoverEntersFolders {
        get => _dragHoverEntersFolders;
        set => SetField(ref _dragHoverEntersFolders, value);
    }

    private bool _useSystemTemp;
    /// <summary>
    /// <inheritdoc cref="AppSettings.UseSystemTemp"/> The checkbox shows
    /// the effective value; once saved it is explicit, and stops following
    /// the mode.
    /// </summary>
    public bool UseSystemTemp {
        get => _useSystemTemp;
        set => SetField(ref _useSystemTemp, value);
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


    // --- Layout (Details) ----------------------------------------------
    //
    // Every view keeps its own sizes. The clamps are the whole validation
    // story for these: the fields are plain text boxes, and a row height of
    // 0 or 5000 typed by hand must not be able to break the list.

    private int _detailsRowHeight;
    public int DetailsRowHeight {
        get => _detailsRowHeight;
        set => SetField(ref _detailsRowHeight, ClampInt(value, 16, 96));
    }

    private int _detailsIconSize;
    public int DetailsIconSize {
        get => _detailsIconSize;
        set {
            if (SetField(ref _detailsIconSize, ClampInt(value, 12, 64))) {
                Raise(nameof(DetailsIconColumnWidth));
            }
        }
    }

    /// <summary>
    /// Width of the Details table's icon column — the icon plus the air
    /// around it. Derived rather than settable: a column narrower than its
    /// icon clips it, and nobody would want to tune the two separately.
    /// </summary>
    public double DetailsIconColumnWidth => DetailsIconSize + 8;


    // --- Layout (Tiles) -------------------------------------------------
    private int _tileCellWidth;
    public int TileCellWidth {
        get => _tileCellWidth;
        set {
            if (SetField(ref _tileCellWidth, ClampInt(value, 120, 480))) {
                Raise(nameof(TilesMetrics));
            }
        }
    }

    private int _tileIconSize;
    public int TileIconSize {
        get => _tileIconSize;
        set {
            if (SetField(ref _tileIconSize, ClampInt(value, 16, 96))) {
                Raise(nameof(TilesMetrics));
            }
        }
    }

    private int _tileLabelFontSize;
    public int TileLabelFontSize {
        get => _tileLabelFontSize;
        set {
            if (SetField(ref _tileLabelFontSize, ClampInt(value, 8, 24))) {
                Raise(nameof(TilesMetrics));
            }
        }
    }


    // --- Layout (LargeIcons) -------------------------------------------
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

    /// <summary>The same for Tiles, from that view's own three knobs.</summary>
    public TileMetrics TilesMetrics => TileMetrics.ForTiles(
        TileCellWidth, TileIconSize, TileLabelFontSize);


    // --- Layout (Gallery) ----------------------------------------------
    private int _galleryCellWidth;
    public int GalleryCellWidth {
        get => _galleryCellWidth;
        set {
            if (SetField(ref _galleryCellWidth, ClampInt(value, 80, 640))) {
                Raise(nameof(GalleryMetrics));
                Raise(nameof(GalleryPreviewWidth));
            }
        }
    }

    private int _galleryImageSize;
    public int GalleryImageSize {
        get => _galleryImageSize;
        set {
            if (SetField(ref _galleryImageSize, ClampInt(value, 64, 600))) {
                Raise(nameof(GalleryMetrics));
                Raise(nameof(GalleryPreviewWidth));
            }
        }
    }

    private int _galleryMargin;
    public int GalleryMargin {
        get => _galleryMargin;
        set {
            if (SetField(ref _galleryMargin, ClampInt(value, 0, 32))) {
                Raise(nameof(GalleryMetrics));
                Raise(nameof(GalleryPreviewWidth));
            }
        }
    }

    private int _galleryLabelFontSize;
    public int GalleryLabelFontSize {
        get => _galleryLabelFontSize;
        set {
            if (SetField(ref _galleryLabelFontSize, ClampInt(value, 8, 24))) {
                Raise(nameof(GalleryMetrics));
            }
        }
    }

    /// <summary>Cell geometry of the gallery grid — same contract as <see cref="IconsMetrics"/>.</summary>
    public TileMetrics GalleryMetrics => TileMetrics.ForGallery(
        GalleryCellWidth, GalleryImageSize, GalleryMargin, GalleryLabelFontSize);

    /// <summary>
    /// How wide the page's gallery preview is drawn (2026-09-28): one cell
    /// with the air round it, then the edge of the next picture - enough to
    /// show the gap between two. Two whole cells were wider than the room
    /// beside the fields and put the preview under them.
    /// </summary>
    public int GalleryPreviewWidth =>
        (3 * GalleryMargin) + GalleryCellWidth + Math.Max(0, (GalleryCellWidth - GalleryImageSize) / 2) + GalleryPreviewEdge;

    private GalleryBackground _galleryBackground;
    public GalleryBackground GalleryBackground {
        get => _galleryBackground;
        set {
            if (SetField(ref _galleryBackground, value)) {
                RaisePalette();
            }
        }
    }

    private int _galleryGreyLevel;
    public int GalleryGreyLevel {
        get => _galleryGreyLevel;
        set {
            if (SetField(ref _galleryGreyLevel, ClampInt(value, 0, 255))) {
                RaisePalette();
            }
        }
    }

    private int _galleryDarkLevel;
    public int GalleryDarkLevel {
        get => _galleryDarkLevel;
        set {
            if (SetField(ref _galleryDarkLevel, ClampInt(value, 0, 255))) {
                RaisePalette();
            }
        }
    }

    /// <summary>
    /// Every colour the gallery draws, from the chosen background and the
    /// two brightness knobs. One value the whole view binds to, for the
    /// same reason <see cref="GalleryMetrics"/> is one value: a background
    /// and a selection colour chosen independently is how you get a
    /// highlight nobody can read.
    /// </summary>
    public GalleryPalette GalleryPalette => new(GalleryBackground, GalleryGreyLevel, GalleryDarkLevel);

    /// <summary>The three background options as colours, for the strip's buttons.</summary>
    public Brush GalleryLightSwatch =>
        ViewModels.GalleryPalette.Swatch(Wander.Core.Persistence.GalleryBackground.Light, GalleryGreyLevel, GalleryDarkLevel);

    public Brush GalleryGreySwatch =>
        ViewModels.GalleryPalette.Swatch(Wander.Core.Persistence.GalleryBackground.Grey, GalleryGreyLevel, GalleryDarkLevel);

    public Brush GalleryDarkSwatch =>
        ViewModels.GalleryPalette.Swatch(Wander.Core.Persistence.GalleryBackground.Dark, GalleryGreyLevel, GalleryDarkLevel);

    private bool _autoGallery;
    public bool AutoGallery {
        get => _autoGallery;
        set => SetField(ref _autoGallery, value);
    }

    private int _autoGalleryPercent;
    public int AutoGalleryPercent {
        get => _autoGalleryPercent;
        set => SetField(ref _autoGalleryPercent, ClampInt(value, 1, 100));
    }

    private ViewMode _defaultViewMode;
    public ViewMode DefaultViewMode {
        get => _defaultViewMode;
        set => SetField(ref _defaultViewMode, value);
    }


    // --- Ratings --------------------------------------------------------
    private SidecarFormat _rawRatingFormat;
    public SidecarFormat RawRatingFormat {
        get => _rawRatingFormat;
        set => SetField(ref _rawRatingFormat, value);
    }

    private bool _confirmCreateSidecar;
    /// <inheritdoc cref="AppSettings.ConfirmCreateSidecar"/>
    public bool ConfirmCreateSidecar {
        get => _confirmCreateSidecar;
        set => SetField(ref _confirmCreateSidecar, value);
    }


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

    /// <summary>
    /// The ceiling on decoded pictures in memory, in megabytes; 0 leaves it
    /// to the machine - a sixteenth of its memory (<see cref="PictureMemory"/>).
    /// </summary>
    private int _pictureMemoryMb;
    public int PictureMemoryMb {
        get => _pictureMemoryMb;
        set => SetField(ref _pictureMemoryMb, value <= 0 ? 0 : ClampInt(value, 128, 65536));
    }

    /// <summary>What 0 means on this machine, in words under the field: "0 - a sixteenth of the memory, 1024 MB here".</summary>
    public string PictureMemoryHint => string.Format(
        Strings.SettingsPictureMemoryHint,
        PictureMemory.Megabytes(PictureMemory.Budget(0, GC.GetGCMemoryInfo().TotalAvailableMemoryBytes)));

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

    private bool _showBookmarkDesktop;
    public bool ShowBookmarkDesktop {
        get => _showBookmarkDesktop;
        set => SetField(ref _showBookmarkDesktop, value);
    }

    private bool _showBookmarkMusic;
    public bool ShowBookmarkMusic {
        get => _showBookmarkMusic;
        set => SetField(ref _showBookmarkMusic, value);
    }

    private bool _showBookmarkVideos;
    public bool ShowBookmarkVideos {
        get => _showBookmarkVideos;
        set => SetField(ref _showBookmarkVideos, value);
    }

    private bool _showBookmarkRecycleBin;
    public bool ShowBookmarkRecycleBin {
        get => _showBookmarkRecycleBin;
        set => SetField(ref _showBookmarkRecycleBin, value);
    }

    private bool _treeKeyboardNavigates;
    /// <inheritdoc cref="AppSettings.TreeKeyboardNavigates"/>
    public bool TreeKeyboardNavigates {
        get => _treeKeyboardNavigates;
        set => SetField(ref _treeKeyboardNavigates, value);
    }

    private bool _treeScrollsSideways;
    /// <inheritdoc cref="AppSettings.TreeScrollsSideways"/>
    public bool TreeScrollsSideways {
        get => _treeScrollsSideways;
        set => SetField(ref _treeScrollsSideways, value);
    }


    // --- Context menu ---------------------------------------------------
    /// <summary>
    /// Menu rows the shell has reported to us, in discovery order. Not a
    /// collection property because nothing binds to it — it is an input to
    /// <see cref="RebuildShellRows"/> and a field of the saved record.
    /// </summary>
    private readonly List<KnownShellEntry> _seenShellEntries = new();

    /// <summary>Scopes the user added through "Добавить", beyond the base ones.</summary>
    private readonly List<string> _trackedScopes = new();

    /// <summary>Most recently right-clicked file types, newest first.</summary>
    private IReadOnlyList<string> _recentScopes = Array.Empty<string>();

    private IReadOnlyList<ShellHandler> _handlers = Array.Empty<ShellHandler>();

    private HashSet<string> _blockedShellKeys = new(StringComparer.OrdinalIgnoreCase);


    private bool _shellExtensionsEnabled;
    public bool ShellExtensionsEnabled {
        get => _shellExtensionsEnabled;
        set => SetField(ref _shellExtensionsEnabled, value);
    }

    /// <summary>
    /// The context-menu table: one row per third-party entry, from the
    /// registry scan and from what Wander has actually met, merged by
    /// <see cref="ShellExtensionCatalog"/>.
    /// </summary>
    public ObservableCollection<ShellExtensionRowViewModel> ShellExtensionRows { get; } = new();

    public IReadOnlyList<string> RecentScopes => _recentScopes;

    /// <summary>Every scope the table is built from: the fixed set plus the user's.</summary>
    public IReadOnlyList<string> ScannedScopes => ShellScopes.Base.Concat(_trackedScopes).ToArray();

    /// <summary>One row per hideable built-in entry, in menu order and shape.</summary>
    public ObservableCollection<MenuItemRowViewModel> MenuItemRows { get; } = new();


    // --- Custom actions ------------------------------------------------
    /// <summary>Where the programs are; empty until the dialog has looked.</summary>
    private IReadOnlyDictionary<string, ToolLocation> _toolLocations = new Dictionary<string, ToolLocation>();

    /// <summary>Programs pointed at by hand on the "Программы" page.</summary>
    private IReadOnlyList<ToolPath> _toolPaths = Array.Empty<ToolPath>();

    /// <summary>The program list of the actions' form, kept while its lines stay the same - see <see cref="RefreshProgramChoices"/>.</summary>
    private IReadOnlyList<ProgramChoice> _programChoices = Array.Empty<ProgramChoice>();

    /// <summary>
    /// The catalog's debug-only rows. Kept out of the table -
    /// there is nothing to edit on them and the page is about what the user
    /// set up - and handed to the menus with the rest, which show them only
    /// while the debug menu is on. Never stored: with no row of their own
    /// they follow the code like any untouched preset.
    /// </summary>
    private readonly List<CustomAction> _debugActions = new();

    /// <summary>
    /// The actions table: presets merged with what the user stored, in
    /// catalog order (<see cref="ActionCatalog.Merge"/>).
    /// </summary>
    public ObservableCollection<ActionRowViewModel> ActionRows { get; } = new();

    /// <summary>The catalog as it stands, presets included - what the menus and the runner read.</summary>
    public IReadOnlyList<CustomAction> Actions => ActionRows.Select(r => r.Action).Concat(_debugActions).ToArray();

    /// <summary>The "Программы" page: one block per program the catalog needs.</summary>
    public ObservableCollection<ToolRowViewModel> ToolRows { get; } = new();

    public IReadOnlyList<ToolPath> ToolPaths => _toolPaths;


    // --- Debug ---------------------------------------------------------
    private bool _showDebugMenu;
    public bool ShowDebugMenu {
        get => _showDebugMenu;
        set => SetField(ref _showDebugMenu, value);
    }

    private bool _logActions;
    /// <inheritdoc cref="AppSettings.LogActions"/>
    public bool LogActions {
        get => _logActions;
        set => SetField(ref _logActions, value);
    }

    private bool _logPaths;
    /// <inheritdoc cref="AppSettings.LogPaths"/>
    public bool LogPaths {
        get => _logPaths;
        set => SetField(ref _logPaths, value);
    }


    // --- Category list (used by the dialog) ---------------------------
    public ObservableCollection<SettingsCategoryViewModel> Categories { get; }

    private SettingsCategoryViewModel? _selectedCategory;
    public SettingsCategoryViewModel? SelectedCategory {
        get => _selectedCategory;
        set => SetField(ref _selectedCategory, value);
    }


    public void ApplyFrom(AppSettings s) {
        // Bulk update without raising for unchanged values; bindings only
        // refresh when something actually shifted.
        RestoreLastFolder = s.RestoreLastFolder;
        WorkFolder = s.WorkFolder;
        AutoRefresh = s.AutoRefresh;
        VisibleFirstLoading = s.VisibleFirstLoading;
        ShowHidden = s.ShowHidden;
        ShowSystem = s.ShowSystem;
        HideSystemRootFolders = s.HideSystemRootFolders;
        ConfirmRecycle = s.ConfirmRecycle;
        ConfirmMove = s.ConfirmMove;
        SkipIdenticalOnConflict = s.SkipIdenticalOnConflict;
        DragHoverEntersFolders = s.DragHoverEntersFolders;
        UseSystemTemp = s.UseSystemTemp ?? AppPaths.IsPortable;
        IntegrateCompanions = s.IntegrateCompanions;
        SortKey = s.SortKey;
        SortAscending = s.SortAscending;
        GroupFoldersFirst = s.GroupFoldersFirst;
        DetailsRowHeight = s.DetailsRowHeight;
        DetailsIconSize = s.DetailsIconSize;
        TileCellWidth = s.TileCellWidth;
        TileIconSize = s.TileIconSize;
        TileLabelFontSize = s.TileLabelFontSize;
        LargeIconCellWidth = s.LargeIconCellWidth;
        LargeIconImageSize = s.LargeIconImageSize;
        LargeIconMargin = s.LargeIconMargin;
        LargeIconLabelFontSize = s.LargeIconLabelFontSize;
        GalleryCellWidth = s.GalleryCellWidth;
        GalleryImageSize = s.GalleryImageSize;
        GalleryMargin = s.GalleryMargin;
        GalleryLabelFontSize = s.GalleryLabelFontSize;
        GalleryBackground = s.GalleryBackground;
        GalleryGreyLevel = s.GalleryGreyLevel;
        GalleryDarkLevel = s.GalleryDarkLevel;
        AutoGallery = s.AutoGallery;
        AutoGalleryPercent = s.AutoGalleryPercent;
        DefaultViewMode = s.DefaultViewMode;
        RawRatingFormat = s.RawRatingFormat;
        ConfirmCreateSidecar = s.ConfirmCreateSidecar;
        ThumbnailDiskCacheEnabled = s.ThumbnailDiskCacheEnabled;
        ThumbnailDiskCacheMb = s.ThumbnailDiskCacheMb;
        PictureMemoryMb = s.PictureMemoryMb;
        ShowBookmarkDownloads = s.ShowBookmarkDownloads;
        ShowBookmarkDocuments = s.ShowBookmarkDocuments;
        ShowBookmarkPictures = s.ShowBookmarkPictures;
        ShowBookmarkDesktop = s.ShowBookmarkDesktop;
        ShowBookmarkMusic = s.ShowBookmarkMusic;
        ShowBookmarkVideos = s.ShowBookmarkVideos;
        ShowBookmarkRecycleBin = s.ShowBookmarkRecycleBin;
        TreeKeyboardNavigates = s.TreeKeyboardNavigates;
        TreeScrollsSideways = s.TreeScrollsSideways;
        ShellExtensionsEnabled = s.ShellExtensionsEnabled;
        RebuildMenuToggles(s.HiddenContextMenuItems);
        // ".lnk" as the type of a menu was the shortcut's target's rows
        // filed under the link (ShellScopes.MenuScopeOf, 2026-09-25): such a
        // row keeps no scope until it is met again, and the picker no longer
        // leads with it.
        _seenShellEntries.Clear();
        _seenShellEntries.AddRange(s.KnownShellEntries.Select(e => ShellScopes.IsShortcut(e.Scope) ? e with { Scope = "" } : e));
        _trackedScopes.Clear();
        _trackedScopes.AddRange(s.TrackedShellScopes);
        _recentScopes = s.RecentShellScopes.Where(scope => !ShellScopes.IsShortcut(scope)).ToArray();
        _blockedShellKeys = new HashSet<string>(s.BlockedShellExtensions, StringComparer.OrdinalIgnoreCase);
        RebuildShellRows();
        _toolPaths = s.ToolPaths;
        RebuildActionRows(s.CustomActions);
        ShowDebugMenu = s.ShowDebugMenu;
        LogActions = s.LogActions;
        LogPaths = s.LogPaths;
    }


    /// <summary>
    /// Records the rows the shell just drew, so the settings table knows
    /// which installed handlers actually show up — and what they call
    /// themselves, which the registry cannot say for a COM handler.
    ///
    /// <para>
    /// Rows already known are left alone: meeting "7-Zip" again must not
    /// re-enable a 7-Zip the user switched off. An entry whose description
    /// or type was empty last time is filled in if this menu has one.
    /// </para>
    /// </summary>
    public void NoteShellExtensions(IEnumerable<KnownShellEntry> entries) {
        bool changed = false;
        foreach (var entry in entries) {
            if (entry.Key.Trim().Length == 0) {
                continue;
            }

            int at = _seenShellEntries.FindIndex(e => Same(e.Key, entry.Key));
            if (at < 0) {
                _seenShellEntries.Add(entry);
                changed = true;
                continue;
            }

            var known = _seenShellEntries[at];
            var filled = known with {
                Help = known.Help.Length > 0 ? known.Help : entry.Help,
                Scope = known.Scope.Length > 0 ? known.Scope : entry.Scope,
            };
            if (filled != known) {
                _seenShellEntries[at] = filled;
                changed = true;
            }
        }

        if (changed) {
            RebuildShellRows();
            OnMenuToggleChanged();
        }
    }

    /// <summary>
    /// Remembers the file type a context menu was just opened on. Feeds the
    /// "Добавить" picker and nothing else — see <see cref="Core.Shell.RecentScopes"/>.
    /// </summary>
    public void NoteMenuScope(string? scope) {
        var updated = Core.Shell.RecentScopes.Add(_recentScopes, scope);
        if (!ReferenceEquals(updated, _recentScopes)) {
            _recentScopes = updated;
            OnMenuToggleChanged();
        }
    }

    /// <summary>
    /// Puts the context-menu page back to how it ships: nothing blocked,
    /// nothing tracked beyond the base scopes, nothing remembered from past
    /// menus, and Wander's own entries all visible.
    ///
    /// <para>
    /// The seen list goes too, on purpose. It is what puts rows in the table
    /// (ShellExtensionCatalog lists what menus have drawn), so leaving it
    /// would reset the switches and keep the clutter - and it costs nothing:
    /// the next right-click starts filling it in again.
    /// </para>
    /// </summary>
    public void ResetContextMenu() {
        _blockedShellKeys.Clear();
        _seenShellEntries.Clear();
        _trackedScopes.Clear();
        _recentScopes = Array.Empty<string>();
        ShellExtensionsEnabled = true;

        foreach (var row in MenuItemRows) {
            row.IsShown = true;
        }

        RebuildShellRows();
        OnMenuToggleChanged();
    }


    /// <summary>
    /// Adds scopes to the table — the "Добавить" button's whole effect. The
    /// rows themselves come from re-scanning: a scope is what we can store,
    /// a handler list is what the registry owns.
    /// </summary>
    public void TrackScopes(IEnumerable<string> scopes) {
        bool added = false;
        foreach (string scope in scopes) {
            string trimmed = scope.Trim();
            if (trimmed.Length == 0
                || ShellScopes.IsBase(trimmed)
                || _trackedScopes.Contains(trimmed, StringComparer.OrdinalIgnoreCase)) {
                continue;
            }
            _trackedScopes.Add(trimmed);
            added = true;
        }

        if (added) {
            RebuildShellRows();
            OnMenuToggleChanged();
        }
    }

    /// <summary>
    /// Hands the table the handlers a scan turned up. Kept out of the VM's
    /// own hands on purpose: the registry lives in the platform layer, and
    /// the dialog is the one that decides when a scan is worth its 50 ms.
    /// </summary>
    public void SetShellHandlers(IReadOnlyList<ShellHandler> handlers) {
        _handlers = handlers;
        RebuildShellRows();
    }

    /// <summary>
    /// Hands both pages where the programs are. Looked up by the dialog on
    /// the pool: <c>PATH</c> can hold a network folder.
    /// </summary>
    public void SetToolLocations(IReadOnlyDictionary<string, ToolLocation> tools) {
        _toolLocations = tools;
        foreach (var row in ActionRows) {
            row.SetTools(tools);
        }

        ToolRows.Clear();
        foreach (string tool in ActionCatalog.ToolNames(Actions)) {
            var row = new ToolRowViewModel(tool);
            if (tools.TryGetValue(tool, out var location)) {
                row.SetLocation(location);
            }
            ToolRows.Add(row);
        }
    }

    /// <summary>"Указать…" and "Сбросить": null goes back to looking for the program. The caller looks again.</summary>
    public void SetToolPath(string tool, string? path) {
        _toolPaths = ActionCatalog.WithToolPath(_toolPaths, tool, path);
        Raise(nameof(ToolPaths));
    }

    public ActionRowViewModel AddAction() {
        return AppendAction(ActionCatalog.NewAction(Strings.ActionsNewTitle));
    }

    /// <summary>A copy goes to the end of the table - where the user's rows are kept.</summary>
    public ActionRowViewModel CopyAction(ActionRowViewModel source) {
        return AppendAction(ActionCatalog.CopyOf(source.Action, string.Format(Strings.ActionsCopyTitle, source.Title)));
    }

    public void RemoveAction(ActionRowViewModel row) {
        if (row.IsPreset) {
            return;
        }

        ActionRows.Remove(row);
        OnActionsChanged();
    }

    public AppSettings ToRecord() {
        return new AppSettings {
            RestoreLastFolder = RestoreLastFolder,
            WorkFolder = WorkFolder,
            AutoRefresh = AutoRefresh,
            VisibleFirstLoading = VisibleFirstLoading,
            ShowHidden = ShowHidden,
            ShowSystem = ShowSystem,
            HideSystemRootFolders = HideSystemRootFolders,
            ConfirmRecycle = ConfirmRecycle,
            ConfirmMove = ConfirmMove,
            SkipIdenticalOnConflict = SkipIdenticalOnConflict,
            DragHoverEntersFolders = DragHoverEntersFolders,
            UseSystemTemp = UseSystemTemp,
            IntegrateCompanions = IntegrateCompanions,
            SortKey = SortKey,
            SortAscending = SortAscending,
            GroupFoldersFirst = GroupFoldersFirst,
            DetailsRowHeight = DetailsRowHeight,
            DetailsIconSize = DetailsIconSize,
            TileCellWidth = TileCellWidth,
            TileIconSize = TileIconSize,
            TileLabelFontSize = TileLabelFontSize,
            LargeIconCellWidth = LargeIconCellWidth,
            LargeIconImageSize = LargeIconImageSize,
            LargeIconMargin = LargeIconMargin,
            LargeIconLabelFontSize = LargeIconLabelFontSize,
            GalleryCellWidth = GalleryCellWidth,
            GalleryImageSize = GalleryImageSize,
            GalleryMargin = GalleryMargin,
            GalleryLabelFontSize = GalleryLabelFontSize,
            GalleryBackground = GalleryBackground,
            GalleryGreyLevel = GalleryGreyLevel,
            GalleryDarkLevel = GalleryDarkLevel,
            AutoGallery = AutoGallery,
            AutoGalleryPercent = AutoGalleryPercent,
            DefaultViewMode = DefaultViewMode,
            RawRatingFormat = RawRatingFormat,
            ConfirmCreateSidecar = ConfirmCreateSidecar,
            ThumbnailDiskCacheEnabled = ThumbnailDiskCacheEnabled,
            ThumbnailDiskCacheMb = ThumbnailDiskCacheMb,
            PictureMemoryMb = PictureMemoryMb,
            ShowBookmarkDownloads = ShowBookmarkDownloads,
            ShowBookmarkDocuments = ShowBookmarkDocuments,
            ShowBookmarkPictures = ShowBookmarkPictures,
            ShowBookmarkDesktop = ShowBookmarkDesktop,
            ShowBookmarkMusic = ShowBookmarkMusic,
            ShowBookmarkVideos = ShowBookmarkVideos,
            ShowBookmarkRecycleBin = ShowBookmarkRecycleBin,
            TreeKeyboardNavigates = TreeKeyboardNavigates,
            TreeScrollsSideways = TreeScrollsSideways,
            ShellExtensionsEnabled = ShellExtensionsEnabled,
            // Persisted as "what is off", so a future Wander release that
            // adds menu entries shows them by default instead of inheriting
            // an implicit "not in the saved list = hidden".
            HiddenContextMenuItems = MenuItemRows.Where(r => r.IsHidden).Select(r => r.Key).ToArray(),
            BlockedShellExtensions = _blockedShellKeys.ToArray(),
            KnownShellEntries = ContextMenuSettings.TrimKnownEntries(_seenShellEntries, _blockedShellKeys),
            TrackedShellScopes = _trackedScopes.ToArray(),
            RecentShellScopes = _recentScopes,
            CustomActions = ActionCatalog.ToStored(ActionPresets.All, Actions),
            ToolPaths = _toolPaths,
            ShowDebugMenu = ShowDebugMenu,
            LogActions = LogActions,
            LogPaths = LogPaths,
        };
    }


    private static bool Same(string a, string b) {
        return string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
    }


    private void RebuildMenuToggles(IReadOnlyList<string> hidden) {
        var off = new HashSet<string>(hidden, StringComparer.OrdinalIgnoreCase);
        MenuItemRows.Clear();
        foreach (var node in ContextMenuCatalog.HideableTree) {
            MenuItemRows.Add(new MenuItemRowViewModel(
                node, off.Contains(node.Id.ToString()), OnMenuToggleChanged));
        }
    }

    /// <summary>
    /// Rebuilds the table from the last scan plus what has been met.
    ///
    /// <para>
    /// Reads <see cref="_blockedShellKeys"/> and never writes it. That
    /// direction matters: the rows are a projection, and a rebuild happens
    /// on Cancel too — recomputing the blocked set from the rows there would
    /// resurrect exactly the ticks the rollback just discarded. A handler
    /// whose application has since been uninstalled keeps its entry in the
    /// set for the same reason, without needing a row to hold it.
    /// </para>
    /// </summary>
    private void RebuildShellRows() {
        var rows = ShellExtensionCatalog.Build(
            _handlers, _seenShellEntries, _blockedShellKeys, _trackedScopes);

        ShellExtensionRows.Clear();
        foreach (var row in rows) {
            ShellExtensionRows.Add(new ShellExtensionRowViewModel(row, OnShellRowToggled));
        }
        Raise(nameof(ShellExtensionRows));
    }

    private void OnShellRowToggled(ShellExtensionRowViewModel row) {
        // Every key the row folded in, not just its own: two registry
        // entries that look identical on screen are one checkbox, and
        // leaving the second one unblocked would keep the item in the menu.
        foreach (string key in row.Keys) {
            if (row.IsBlocked) {
                _blockedShellKeys.Add(key);
            } else {
                _blockedShellKeys.Remove(key);
            }
        }

        OnMenuToggleChanged();
    }

    private void RebuildActionRows(IReadOnlyList<CustomAction> stored) {
        ActionRows.Clear();
        _debugActions.Clear();
        foreach (var action in ActionCatalog.Merge(ActionPresets.All, stored)) {
            if (action.DebugOnly) {
                _debugActions.Add(action);
                continue;
            }
            ActionRows.Add(NewActionRow(action));
        }
        OnActionsChanged();
    }

    private ActionRowViewModel AppendAction(CustomAction action) {
        var row = NewActionRow(action);
        ActionRows.Add(row);
        OnActionsChanged();

        return row;
    }

    private ActionRowViewModel NewActionRow(CustomAction action) {
        var row = new ActionRowViewModel(action, OnActionsChanged, LocateProgram, () => _programChoices);
        row.SetTools(_toolLocations);

        return row;
    }

    /// <summary>The same nudge as <see cref="OnMenuToggleChanged"/>, for the actions table.</summary>
    private void OnActionsChanged() {
        RefreshProgramChoices();
        Raise(nameof(ActionRows));
    }

    /// <summary>
    /// The actions' program list anew, after a row's program changed or a
    /// row came or went. A new list only when its lines differ: a box handed
    /// a new list selects again, and every keystroke in the form would.
    /// </summary>
    private void RefreshProgramChoices() {
        var next = ActionCatalog.ProgramChoices(Actions);
        if (next.SequenceEqual(_programChoices)) {
            return;
        }

        _programChoices = next;
        foreach (var row in ActionRows) {
            row.RaiseProgramChoices();
        }
    }

    /// <summary>
    /// Table rows are collections, not properties, so the owner's
    /// "settings changed → persist" hook needs an explicit nudge.
    /// </summary>
    private void OnMenuToggleChanged() {
        Raise(nameof(MenuItemRows));
    }


    /// <summary>
    /// The palette and its three swatches are all projections of the same
    /// two knobs, so they move together — one call rather than four
    /// <c>Raise</c>s scattered across the setters that can drift apart.
    /// </summary>
    private void RaisePalette() {
        Raise(nameof(GalleryPalette));
        Raise(nameof(GalleryLightSwatch));
        Raise(nameof(GalleryGreySwatch));
        Raise(nameof(GalleryDarkSwatch));
    }


    private static int ClampInt(int value, int min, int max) {
        return Math.Max(min, Math.Min(max, value));
    }

    private static string? SystemDocuments() {
        return ServiceLocator.TryGet<IKnownFolders>()?.GetDocuments();
    }

    /// <summary>Where an action's program is; with no locator every program counts as there - nothing can say otherwise.</summary>
    private static string? LocateProgram(string program) {
        return ServiceLocator.TryGet<IToolLocator>() is { } locator ? locator.Locate(program) : program;
    }
}
