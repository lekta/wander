using System.Collections.ObjectModel;
using System.Windows.Media;
using Wander.Core.Companions;
using Wander.Core.FileSystem;
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

    // --- General -------------------------------------------------------
    private bool _restoreLastFolder;
    public bool RestoreLastFolder {
        get => _restoreLastFolder;
        set => SetField(ref _restoreLastFolder, value);
    }


    // --- Behaviour ------------------------------------------------------
    private bool _autoRefresh;
    public bool AutoRefresh {
        get => _autoRefresh;
        set => SetField(ref _autoRefresh, value);
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
            }
        }
    }

    private int _galleryImageSize;
    public int GalleryImageSize {
        get => _galleryImageSize;
        set {
            if (SetField(ref _galleryImageSize, ClampInt(value, 64, 600))) {
                Raise(nameof(GalleryMetrics));
            }
        }
    }

    private int _galleryMargin;
    public int GalleryMargin {
        get => _galleryMargin;
        set {
            if (SetField(ref _galleryMargin, ClampInt(value, 0, 32))) {
                Raise(nameof(GalleryMetrics));
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


    // --- Ratings --------------------------------------------------------
    private SidecarFormat _rawRatingFormat;
    public SidecarFormat RawRatingFormat {
        get => _rawRatingFormat;
        set => SetField(ref _rawRatingFormat, value);
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


    // --- Context menu ---------------------------------------------------
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

    private bool _showSystemShellExtensions;
    public bool ShowSystemShellExtensions {
        get => _showSystemShellExtensions;
        set {
            if (SetField(ref _showSystemShellExtensions, value)) {
                RebuildShellRows();
            }
        }
    }

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

    public IReadOnlyList<string> RecentScopes => _recentScopes;

    /// <summary>Every scope the table is built from: the fixed set plus the user's.</summary>
    public IReadOnlyList<string> ScannedScopes => ShellScopes.Base.Concat(_trackedScopes).ToArray();

    /// <summary>One row per hideable built-in entry, in menu order and shape.</summary>
    public ObservableCollection<MenuItemRowViewModel> MenuItemRows { get; } = new();

    private IReadOnlyList<ShellHandler> _handlers = Array.Empty<ShellHandler>();

    private HashSet<string> _blockedShellKeys = new(StringComparer.OrdinalIgnoreCase);


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
            // Nine pages, not eleven. "Безопасность" was never about
            // security — it is what the listing shows — and two pages
            // holding one checkbox each cost a click to reach and taught
            // nobody anything. Their contents moved to the page whose
            // question they actually answer.
            new GeneralSettingsCategory(this),
            new VisibilitySettingsCategory(this),
            new LayoutSettingsCategory(this),
            new GallerySettingsCategory(this),
            new ThumbnailsSettingsCategory(this),
            new BookmarksSettingsCategory(this),
            new ContextMenuSettingsCategory(this),
            new HotkeysSettingsCategory(this),
            new DebugSettingsCategory(this),
        };
        _selectedCategory = Categories[0];
    }


    public void ApplyFrom(AppSettings s) {
        // Bulk update without raising for unchanged values; bindings only
        // refresh when something actually shifted.
        RestoreLastFolder = s.RestoreLastFolder;
        AutoRefresh = s.AutoRefresh;
        ShowHidden = s.ShowHidden;
        ShowSystem = s.ShowSystem;
        HideSystemRootFolders = s.HideSystemRootFolders;
        ConfirmRecycle = s.ConfirmRecycle;
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
        RawRatingFormat = s.RawRatingFormat;
        ThumbnailDiskCacheEnabled = s.ThumbnailDiskCacheEnabled;
        ThumbnailDiskCacheMb = s.ThumbnailDiskCacheMb;
        ThumbnailMemoryEntries = s.ThumbnailMemoryEntries;
        ShowBookmarkDownloads = s.ShowBookmarkDownloads;
        ShowBookmarkDocuments = s.ShowBookmarkDocuments;
        ShowBookmarkPictures = s.ShowBookmarkPictures;
        ShowBookmarkDesktop = s.ShowBookmarkDesktop;
        ShowBookmarkMusic = s.ShowBookmarkMusic;
        ShowBookmarkVideos = s.ShowBookmarkVideos;
        ShowBookmarkRecycleBin = s.ShowBookmarkRecycleBin;
        TreeKeyboardNavigates = s.TreeKeyboardNavigates;
        ShellExtensionsEnabled = s.ShellExtensionsEnabled;
        _showSystemShellExtensions = s.ShowSystemShellExtensions;
        Raise(nameof(ShowSystemShellExtensions));
        RebuildMenuToggles(s.HiddenContextMenuItems);
        _seenShellEntries.Clear();
        _seenShellEntries.AddRange(s.KnownShellEntries);
        _trackedScopes.Clear();
        _trackedScopes.AddRange(s.TrackedShellScopes);
        _recentScopes = s.RecentShellScopes;
        _blockedShellKeys = new HashSet<string>(s.BlockedShellExtensions, StringComparer.OrdinalIgnoreCase);
        RebuildShellRows();
        ShowDebugMenu = s.ShowDebugMenu;
    }


    /// <summary>
    /// Records the rows the shell just drew, so the settings table knows
    /// which installed handlers actually show up — and what they call
    /// themselves, which the registry cannot say for a COM handler.
    ///
    /// <para>
    /// Rows already known are left alone: meeting "7-Zip" again must not
    /// re-enable a 7-Zip the user switched off. An entry whose description
    /// was empty last time is filled in if the handler published one now.
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
            if (known.Help.Length == 0 && entry.Help.Length > 0) {
                _seenShellEntries[at] = known with { Help = entry.Help };
                changed = true;
            }
        }

        if (changed) {
            RebuildShellRows();
            OnMenuToggleChanged();
        }
    }

    private static bool Same(string a, string b) {
        return string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
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
    /// The seen list goes too, on purpose. It is what makes rows appear that
    /// no scan produced, so leaving it would reset the switches and keep the
    /// clutter — and it costs nothing: the next right-click starts filling
    /// it in again.
    /// </para>
    /// </summary>
    public void ResetContextMenu() {
        _blockedShellKeys.Clear();
        _seenShellEntries.Clear();
        _trackedScopes.Clear();
        _recentScopes = Array.Empty<string>();
        ShowSystemShellExtensions = false;
        ShellExtensionsEnabled = true;

        foreach (var row in MenuItemRows) {
            row.IsHidden = false;
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

    public AppSettings ToRecord() {
        return new AppSettings {
            RestoreLastFolder = RestoreLastFolder,
            AutoRefresh = AutoRefresh,
            ShowHidden = ShowHidden,
            ShowSystem = ShowSystem,
            HideSystemRootFolders = HideSystemRootFolders,
            ConfirmRecycle = ConfirmRecycle,
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
            RawRatingFormat = RawRatingFormat,
            ThumbnailDiskCacheEnabled = ThumbnailDiskCacheEnabled,
            ThumbnailDiskCacheMb = ThumbnailDiskCacheMb,
            ThumbnailMemoryEntries = ThumbnailMemoryEntries,
            ShowBookmarkDownloads = ShowBookmarkDownloads,
            ShowBookmarkDocuments = ShowBookmarkDocuments,
            ShowBookmarkPictures = ShowBookmarkPictures,
            ShowBookmarkDesktop = ShowBookmarkDesktop,
            ShowBookmarkMusic = ShowBookmarkMusic,
            ShowBookmarkVideos = ShowBookmarkVideos,
            ShowBookmarkRecycleBin = ShowBookmarkRecycleBin,
            TreeKeyboardNavigates = TreeKeyboardNavigates,
            ShellExtensionsEnabled = ShellExtensionsEnabled,
            // Persisted as "what is off", so a future Wander release that
            // adds menu entries shows them by default instead of inheriting
            // an implicit "not in the saved list = hidden".
            HiddenContextMenuItems = MenuItemRows.Where(r => r.IsHidden).Select(r => r.Key).ToArray(),
            BlockedShellExtensions = _blockedShellKeys.ToArray(),
            KnownShellEntries = ContextMenuSettings.TrimKnownEntries(_seenShellEntries, _blockedShellKeys),
            ShowSystemShellExtensions = ShowSystemShellExtensions,
            TrackedShellScopes = _trackedScopes.ToArray(),
            RecentShellScopes = _recentScopes,
            ShowDebugMenu = ShowDebugMenu,
        };
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
            _handlers, _seenShellEntries, _blockedShellKeys, ShowSystemShellExtensions);

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
}
