using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Threading;
using Wander.App.Controls;
using Wander.App.Converters;
using Wander.App.Dialogs;
using Wander.App.Resources;
using Wander.App.Util;
using Wander.App.ViewModels;
using Wander.Core;
using Wander.Core.Diagnostics;
using Wander.Core.FileSystem;
using Wander.Core.Folders;
using Wander.Core.Layout;
using Wander.Core.Listing;
using Wander.Core.Logging;
using Wander.Core.Preview;
using Wander.Core.Workspace;

namespace Wander.App.Views;

/// <summary>
/// Hosts the folder listing in every display mode and owns the gestures
/// they share. See the comment at the top of <c>FileListView.xaml</c> for
/// the split against <see cref="MainWindow"/>.
///
/// <para>
/// The control reads its data from the inherited <see cref="MainViewModel"/>
/// and reports the two things it cannot finish on its own upwards:
/// <see cref="DragStartRequested"/> (the drag preview window and the
/// drop pipeline live in the window) and <see cref="ContextMenuRequested"/>
/// (the menu is assembled from Core's model plus the shell's, both wired
/// in the window).
/// </para>
/// </summary>
public partial class FileListView : UserControl {
    private readonly SelectionController _selection = new();
    private readonly RubberBandController _rubberBand;

    // --- Drag source arming --------------------------------------------
    private Point _dragOrigin;
    private bool _dragArmed;

    // The same for the right button: a right-drag opens a menu on the
    // drop instead of doing what the modifiers say - Explorer's gesture.
    private Point _rightDragOrigin;
    private bool _rightDragArmed;

    /// <summary>Set on right-button-down: did the click land on empty space?</summary>
    private bool _contextIsBackground;

    /// <summary>Jump-to-name from the keyboard; see <see cref="List_PreviewTextInput"/>.</summary>
    private readonly TypeAheadController _typeAhead = new();

    /// <summary>The views currently unbound from the rows - see <see cref="ApplyViewAttachment"/>.</summary>
    private readonly HashSet<Selector> _detachedViews = new();

    /// <summary>
    /// True while a selection is being put on in one call (<see cref="PutSelection"/>).
    /// The selection handler steps aside for it; the user's own is reported
    /// once at the end, the model's not at all - it is the model's already.
    /// </summary>
    private bool _applyingSelection;

    /// <summary>
    /// A row the keyboard was sent to that the list has not made a container
    /// for yet - scrolled to a moment ago, in a view just switched on. The
    /// list's generator says when it has (<see cref="OnContainersGenerated"/>).
    /// </summary>
    private FileSystemEntry? _pendingFocus;

    /// <summary>The inline rename editor while a name is being edited, and the layer it sits in - see the rename section.</summary>
    private RenameAdorner? _renameAdorner;
    private AdornerLayer? _renameLayer;

    /// <summary>
    /// The editor's text box while an edit is open. Kept because a click
    /// that lands outside it has to commit the edit, and the adorner owns
    /// the box rather than lending it out.
    /// </summary>
    private TextBox? _renameBox;


    public FileListView() {
        InitializeComponent();
        _rubberBand = new RubberBandController(
            () => Vm.Entries,
            SetListSelection,
            ClearListSelection);
        DataContextChanged += OnDataContextChanged;
        foreach (var view in new ItemsControl[] { DetailsView, TilesView, IconsView, GalleryView }) {
            view.ItemContainerGenerator.StatusChanged += OnContainersGenerated;
        }
    }


    /// <summary>
    /// The user started dragging the current selection out of the list. The
    /// window answers by running the drag loop with its preview window.
    /// </summary>
    public event EventHandler<FileListDragRequest>? DragStartRequested;

    /// <summary>The list wants its context menu shown.</summary>
    public event EventHandler<FileListMenuRequest>? ContextMenuRequested;

    /// <summary>
    /// Enter or Space on pictures in the gallery: the window shows them full
    /// screen (PLAN Q5) - one, a pair, or the selection one by one.
    /// </summary>
    public event EventHandler<FullscreenPlan>? FullscreenRequested;

    /// <summary>The filter field's "more" button: the window raises the search window, which it owns (PLAN G6).</summary>
    public event EventHandler? SearchWindowRequested;


    private MainViewModel Vm => (MainViewModel)DataContext;


    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e) {
        if (e.OldValue is MainViewModel old) {
            old.PropertyChanged -= OnViewModelChanged;
            old.Settings.PropertyChanged -= OnSettingsChanged;
            old.FolderArrived -= OnFolderArrived;
            old.Entries.CollectionChanged -= OnEntriesChanged;
        }
        if (e.NewValue is MainViewModel vm) {
            vm.PropertyChanged += OnViewModelChanged;
            vm.Settings.PropertyChanged += OnSettingsChanged;
            vm.FolderArrived += OnFolderArrived;
            vm.Entries.CollectionChanged += OnEntriesChanged;
            ShowSortIndicator();
            ApplyDetailsIconSize();
            ApplyTileMetrics();
            ApplyVisibleFirst();
            ApplyViewAttachment();
            ShowRatingColumn();
            ShowSearchColumns();
        }
    }

    /// <summary>
    /// A new listing means the half-typed name belongs to a folder that is
    /// no longer on screen.
    /// </summary>
    private void OnEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e) {
        _typeAhead.Reset();
    }


    // --- Sorting from the column headers --------------------------------

    /// <summary>
    /// A click on a column header. The grid's own sort is refused
    /// (<c>e.Handled</c>) and the request goes to the view model instead:
    /// the order is produced by the enumerator, once, for every view — a
    /// second sort applied to the Details rows on top of it would leave
    /// Tiles and LargeIcons showing a different order than the table.
    ///
    /// <para>
    /// Clicking the column that is already sorted flips the direction; that
    /// part lives in <c>SetSortKey</c>, which the View menu shares.
    /// </para>
    /// </summary>
    private void Details_Sorting(object sender, DataGridSortingEventArgs e) {
        e.Handled = true;
        Vm.SetSortKeyCommand.Execute(e.Column.SortMemberPath);
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e) {
        if (e.PropertyName == nameof(MainViewModel.HasRatings)) {
            ShowRatingColumn();
        } else if (e.PropertyName == nameof(MainViewModel.IsSearchResults)) {
            ShowSearchColumns();
        } else if (e.PropertyName == nameof(MainViewModel.ViewMode)) {
            ApplyViewAttachment();
            // The four views are four controls, and the one that had the
            // keyboard has just collapsed under it. Whether the keyboard
            // follows into the new one is the model's to say (K-5); the row
            // it lands on is made by the new view's next layout pass.
            Vm.Workspace.Post(new ViewModeChanged());
        } else if (e.PropertyName == nameof(MainViewModel.RenamingPath) && Vm.RenamingPath is null) {
            // The view model ended the edit - a commit, an Escape, or a
            // listing rebuilt under the editor. The editor goes with it.
            HideRenameEditor();
        }
    }


    /// <summary>
    /// "Folder" and "Match" appear only while the list is showing search
    /// results. Assigned from code rather than bound for the same reason
    /// <see cref="ShowRatingColumn"/> is: a
    /// <see cref="System.Windows.Controls.DataGridColumn"/> is not in the
    /// visual tree, so a binding on it resolves to nothing — silently.
    /// </summary>
    private void ShowSearchColumns() {
        if (DataContext is not MainViewModel vm) {
            return;
        }

        var visibility = vm.IsSearchResults ? Visibility.Visible : Visibility.Collapsed;
        FolderColumn.Visibility = visibility;
        MatchColumn.Visibility = visibility;
        // The tiles' second line follows the same switch; the rows are
        // replaced wholesale when results come and go, so every tile reads
        // the flag fresh.
        ((TileSecondLineConverter)Resources["TileSecondLine"]).ShowFolder = vm.IsSearchResults;
    }

    /// <summary>
    /// The rating column appears only in folders where something is rated.
    /// Assigned from here rather than bound for the same reason the icon
    /// column's width is: a <see cref="System.Windows.Controls.DataGridColumn"/>
    /// is not in the visual tree, so a binding on it resolves to nothing —
    /// silently.
    /// </summary>
    private void ShowRatingColumn() {
        if (DataContext is MainViewModel vm) {
            RatingColumn.Visibility = vm.HasRatings ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e) {
        switch (e.PropertyName) {
            case nameof(SettingsViewModel.SortKey):
            case nameof(SettingsViewModel.SortAscending):
                ShowSortIndicator();
                break;

            case nameof(SettingsViewModel.DetailsIconSize):
                ApplyDetailsIconSize();
                break;

            case nameof(SettingsViewModel.TilesMetrics):
            case nameof(SettingsViewModel.IconsMetrics):
            case nameof(SettingsViewModel.GalleryMetrics):
                ApplyTileMetrics();
                break;

            case nameof(SettingsViewModel.VisibleFirstLoading):
                ApplyVisibleFirst();
                break;
        }
    }


    /// <summary>
    /// Mirrors the "read what is on screen first" setting into the icon
    /// control, which has no DataContext of its own to read it from.
    /// </summary>
    private void ApplyVisibleFirst() {
        if (DataContext is MainViewModel vm) {
            AsyncIcon.VisibleFirst = vm.Settings.VisibleFirstLoading;
        }
    }


    /// <summary>
    /// The rows of a folder the user walked into have landed: once they are
    /// laid out, the icons on the first screen are handed to
    /// <see cref="FirstScreenWatch"/>, which times them from the navigation.
    /// Loaded priority runs right after the layout pass, so the containers
    /// exist and the viewport question has an answer.
    /// </summary>
    private void OnFolderArrived(string path, Stopwatch clock) {
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => {
            if (DataContext is not MainViewModel vm || !string.Equals(vm.CurrentPath, path, StringComparison.OrdinalIgnoreCase)) {
                return;
            }

            FirstScreenWatch.Begin(path, clock, VisibleIcons(), Log.Current);
        }));
    }


    /// <summary>
    /// The icons of the realised rows that are inside the viewport. The
    /// tile panels realise exactly the visible range; the table keeps a
    /// page of rows either side, and those are not the first screen.
    /// </summary>
    private List<AsyncIcon> VisibleIcons() {
        var icons = new List<AsyncIcon>();
        if (ActiveList() is not { } list) {
            return icons;
        }

        var generator = list.ItemContainerGenerator;
        for (int i = 0; i < list.Items.Count; i++) {
            if (generator.ContainerFromIndex(i) is DependencyObject container
                && ListVisuals.FindDescendant<AsyncIcon>(container) is { } icon
                && icon.IsInViewport()) {
                icons.Add(icon);
            }
        }

        return icons;
    }

    /// <summary>
    /// The two numbers the table's icon needs: the width of its column and
    /// the size of the icon in it. Neither is bound, for two different
    /// reasons.
    ///
    /// <para>
    /// A <see cref="System.Windows.Controls.DataGridColumn"/> is not in the
    /// visual tree and has no DataContext, so a binding on its Width
    /// resolves to nothing at all — silently, which is the worst way for a
    /// binding to fail. The icon inside the cell could be bound, and was:
    /// twice, through <c>RelativeSource</c> up to the UserControl, in every
    /// realised row. It is a resource now for the same reason the tile sizes
    /// are (see <see cref="ApplyTileMetrics"/>) — a lookup up the tree with
    /// nothing to subscribe to.
    /// </para>
    /// </summary>
    private void ApplyDetailsIconSize() {
        if (DataContext is MainViewModel vm) {
            IconColumn.Width = new DataGridLength(vm.Settings.DetailsIconColumnWidth);
            // A double, not the int the setting is: a DynamicResource hands
            // the value over as it stands, without a type converter, and
            // Width would quietly keep its default.
            Resources["DetailsIconSize"] = (double)vm.Settings.DetailsIconSize;
        }
    }


    /// <summary>
    /// Hands the tile templates their few numbers as resources.
    ///
    /// <para>
    /// The templates used to bind every size through
    /// <c>RelativeSource AncestorType=UserControl</c> and a four-hop path
    /// into the settings - eight such bindings per tile, resolved and
    /// subscribed for every container built, on every folder. A
    /// DynamicResource is a lookup up the tree and nothing to subscribe to;
    /// rewriting the entry here is what makes the settings dialog still
    /// resize tiles live. The font size of the name is not here at all: it
    /// is set on each ListBox and inherited.
    /// </para>
    /// </summary>
    private void ApplyTileMetrics() {
        if (DataContext is not MainViewModel vm) {
            return;
        }

        var tiles = vm.Settings.TilesMetrics;
        Resources["TilesCellMargin"] = new Thickness(tiles.Margin);
        Resources["TilesImageSize"] = tiles.ImageSize;
        Resources["TilesSecondaryFontSize"] = tiles.SecondaryFontSize;

        var icons = vm.Settings.IconsMetrics;
        Resources["IconsCellMargin"] = new Thickness(icons.Margin);
        Resources["IconsImageSize"] = icons.ImageSize;
        Resources["IconsLabelHeight"] = icons.LabelHeight;

        var gallery = vm.Settings.GalleryMetrics;
        Resources["GalleryCellMargin"] = new Thickness(gallery.Margin);
        Resources["GalleryImageSize"] = gallery.ImageSize;
        Resources["GalleryLabelHeight"] = gallery.LabelHeight;
    }

    /// <summary>
    /// Puts the little arrow on the column that is actually sorted. The
    /// grid would normally do this as part of sorting, which is exactly what
    /// we refused above — so the indicator is driven from the settings
    /// instead, and the View menu moves it too.
    /// </summary>
    private void ShowSortIndicator() {
        if (DataContext is not MainViewModel vm) {
            return;
        }

        string active = vm.Settings.SortKey.ToString();
        foreach (var column in DetailsView.Columns) {
            column.SortDirection = column.SortMemberPath == active
                ? (vm.Settings.SortAscending ? ListSortDirection.Ascending : ListSortDirection.Descending)
                : null;
        }
    }


    // --- Public surface used by MainWindow -----------------------------
    // FocusList, ClearSelection and StartRename (in the rename section), and
    // the model's effects on the list: ApplySelection, FocusRow, OpenEditor.
    // Everything else here is this control's own business.

    /// <summary>
    /// Hands the keyboard back to the list - onto the row it was on (the
    /// caret), else the selected one. Focusing the container itself is not
    /// enough: a list with focus but no focused item leaves the arrow keys
    /// resuming from wherever the cursor was last, which is usually the top.
    /// </summary>
    public void FocusList() {
        if ((CaretEntry() ?? Vm.SelectedEntry) is { } entry) {
            if (!IsSamePath(entry.FullPath, Vm.CaretPath)) {
                Vm.Workspace.Post(new ListCaretMoved(entry.FullPath));
            }
            FocusEntry(entry, scroll: true);

            return;
        }

        ActiveList()?.Focus();
    }


    /// <summary>Clears the selection in whichever container is on screen.</summary>
    public void ClearSelection() {
        SelectionController.ClearActive(ActiveList());
    }


    /// <summary>
    /// The model's selection (ApplyListSelection) put on the list on screen
    /// as it is - in one call, and not told back: it is the model's already.
    /// What it mends is the list's own doing: a row rebuilt or replaced drops
    /// out of a WPF selection on the way (REDESIGN 4.8).
    /// <paramref name="scroll"/> brings the main row into view, the table's
    /// current row on it.
    /// </summary>
    public void ApplySelection(ListState list, bool scroll) {
        if (ActiveList() is not { } host) {
            return;
        }

        var rows = EntriesOf(list.Selection, list.Primary);
        PutSelection(host, rows, report: false);
        if (scroll && rows.Count > 0) {
            if (host is DataGrid grid) {
                grid.CurrentItem = rows[0];
            }
            ScrollRowIntoView(host, rows[0]);
        }
    }


    /// <summary>
    /// <paramref name="entry"/> becomes the selection, as an arrow key would
    /// make it, and is brought into view - F3 past the last match in the
    /// preview going on to the next found file (PLAN B6). The keyboard
    /// stays where it is; in the list, it comes onto the row.
    /// </summary>
    public void SelectRow(FileSystemEntry entry) {
        if (ActiveList() is not { } host) {
            return;
        }

        PutSelection(host, new[] { entry }, report: true);
        if (host is DataGrid grid) {
            grid.CurrentItem = entry;
        }
        ScrollRowIntoView(host, entry);
        if (host.IsKeyboardFocusWithin) {
            FocusEntry(entry, scroll: true);
        }
    }


    /// <summary>
    /// The model's FocusRow on the list: the keyboard onto the row on
    /// <paramref name="path"/>. <paramref name="scroll"/> brings it into
    /// view and waits for the list to make its container if it has to;
    /// without it, a row off screen leaves the keyboard on the list itself -
    /// nothing moves under the user.
    /// </summary>
    public void FocusRow(string path, bool scroll) {
        if (EntryAt(path) is { } entry) {
            FocusEntry(entry, scroll);
        } else {
            ActiveList()?.Focus();
        }
    }


    /// <summary>The model's OpenEditor: the name editor on the row on <paramref name="path"/> - while it is the one selected.</summary>
    public void OpenEditor(string path) {
        if (Vm.SelectedEntry is { } entry && IsSamePath(entry.FullPath, path)) {
            StartRename();
        }
    }


    /// <summary>The container backing the current view mode.</summary>
    private ItemsControl? ActiveList() {
        return Vm.ViewMode switch {
            ViewMode.Details => DetailsView,
            ViewMode.Tiles => TilesView,
            ViewMode.LargeIcons => IconsView,
            ViewMode.Gallery => GalleryView,
            _ => null,
        };
    }


    /// <summary>
    /// Puts the keyboard on a row. A row the list has not made a container
    /// for yet - scrolled to a moment ago, in a view just switched on - gets
    /// it once the list has made one (<see cref="OnContainersGenerated"/>),
    /// the list itself holding the keyboard meanwhile. No layout pass is
    /// forced for it: that realised a screenful of rows with their icons on
    /// the spot, up to half a second (TECHDEBT ui.restore).
    /// </summary>
    private void FocusEntry(FileSystemEntry entry, bool scroll) {
        _pendingFocus = null;
        if (TryFocusEntry(entry, scroll)) {
            return;
        }

        if (scroll) {
            _pendingFocus = entry;
        }
        if (ActiveList() is { IsKeyboardFocusWithin: false } list) {
            list.Focus();
        }
    }


    /// <summary>The keyboard onto the row's container, if the list has made it.</summary>
    private bool TryFocusEntry(FileSystemEntry entry, bool scroll) {
        switch (ActiveList()) {
            case DataGrid dg:
                if (scroll) {
                    dg.ScrollIntoView(entry);
                }
                // Arrow keys in a DataGrid follow the *current cell*, not the
                // selection. Leaving it stale is what made the next arrow
                // press jump to a row near the top instead of the neighbour.
                if (dg.Columns.Count > 0) {
                    dg.CurrentCell = new DataGridCellInfo(entry, dg.Columns[0]);
                }

                return dg.ItemContainerGenerator.ContainerFromItem(entry) is DataGridRow row
                    && ListVisuals.FindDescendant<DataGridCell>(row) is { } cell
                    && cell.Focus();

            case ListBox lb:
                if (scroll) {
                    lb.ScrollIntoView(entry);
                }

                return lb.ItemContainerGenerator.ContainerFromItem(entry) is ListBoxItem item && item.Focus();

            default:
                return false;
        }
    }


    /// <summary>
    /// A list made containers: the row waiting for the keyboard takes it if
    /// its container is among them and the keyboard is still in the list.
    /// </summary>
    private void OnContainersGenerated(object? sender, EventArgs e) {
        if (_pendingFocus is not { } entry
            || sender is not ItemContainerGenerator { Status: GeneratorStatus.ContainersGenerated } generator
            || ActiveList() is not { } list || !ReferenceEquals(list.ItemContainerGenerator, generator)) {
            return;
        }

        if (!IsKeyboardFocusWithin) {
            _pendingFocus = null;

            return;
        }
        if (TryFocusEntry(entry, scroll: false)) {
            _pendingFocus = null;
        }
    }


    /// <summary>
    /// The user put the keyboard on a row - an arrow at the edge of the grid,
    /// a letter typed, the way back out of the editor: the caret goes with it.
    /// </summary>
    private void TakeRow(FileSystemEntry entry) {
        Vm.Workspace.Post(new ListCaretMoved(entry.FullPath));
        FocusEntry(entry, scroll: true);
    }


    // --- Selection ------------------------------------------------------

    private void List_SelectionChanged(object sender, SelectionChangedEventArgs e) {
        // Containers can raise this while the control is still being wired
        // up, before the view model has been inherited down the tree.
        if (DataContext is not MainViewModel) {
            return;
        }

        // A view being unbound reports an empty selection on the way out
        // (see ApplyViewAttachment). That is the control emptying, not the
        // user deselecting anything.
        if (sender is Selector leaving && _detachedViews.Contains(leaving)) {
            return;
        }

        // A selection put on in one call (PutSelection) reports itself
        // when it is done, if it is the user's.
        if (_applyingSelection) {
            return;
        }

        ReportSelection(sender);
    }

    /// <summary>
    /// Tells the model what the control's selection is now - the user's
    /// doing: a click, a key, a sweep, Ctrl+A; one report per change. Not
    /// while the view model lays the rows down again: the list drops a
    /// rebuilt or replaced row out of its selection on its own (REDESIGN
    /// 4.8), and the model puts the selection back once the rows have landed.
    /// </summary>
    private void ReportSelection(object host) {
        if (Vm.IsSyncingRows) {
            return;
        }

        var selected = SelectedEntriesOf(host);
        var primary = (host as Selector)?.SelectedItem as FileSystemEntry ?? selected.FirstOrDefault();
        Vm.Workspace.Post(new ListSelectionChanged(
            selected.Select(e => e.FullPath).ToArray(), primary?.FullPath, CaretAfter(selected)));
    }

    private static List<FileSystemEntry> SelectedEntriesOf(object host) {
        var entries = new List<FileSystemEntry>();
        System.Collections.IList? items = host switch {
            DataGrid dg => dg.SelectedItems,
            ListBox lb => lb.SelectedItems,
            _ => null,
        };
        if (items is null) {
            return entries;
        }

        foreach (var item in items) {
            if (item is FileSystemEntry fe) {
                entries.Add(fe);
            }
        }

        return entries;
    }


    /// <summary>
    /// Where the focus rectangle goes after the selection changed.
    ///
    /// <para>
    /// The keyboard's own row wins: with Shift held the selection is a run
    /// and the caret is the end of it the user is moving, which is exactly
    /// the row that has focus. Failing that — a selection set from code, a
    /// click WPF handled itself — the last selected row is the caret, the
    /// same one Explorer leaves the rectangle on. An emptied selection
    /// leaves the caret alone: a click on empty space is the case this
    /// whole thing exists for.
    /// </para>
    /// </summary>
    private string? CaretAfter(IReadOnlyList<FileSystemEntry> selected) {
        // Nothing here walks the listing: a selection report comes once per
        // change, and a scan of the rows in each would be a sweep's cost
        // multiplied by the folder.
        if (Keyboard.FocusedElement is FrameworkElement { DataContext: FileSystemEntry focused }) {
            return focused.FullPath;
        }

        return selected.Count > 0 ? selected[^1].FullPath : Vm.CaretPath;
    }


    private static void ClearListSelection(ItemsControl host) {
        switch (host) {
            case ListBox lb: lb.UnselectAll(); break;
            case DataGrid dg: dg.UnselectAll(); break;
        }
    }

    /// <summary>The user's selection from a gesture of this control's own - a sweep, a key at the grid's edge, a letter typed, a right-click outside the selection.</summary>
    private void SetListSelection(ItemsControl host, IEnumerable<FileSystemEntry> items) {
        PutSelection(host, items.ToList(), report: true);
    }

    /// <summary>
    /// Makes <paramref name="rows"/> the selection in one call (AB), the first
    /// of them the control's selected item - unless it is that already.
    /// <paramref name="report"/>: tell the model, when it is the user's.
    /// </summary>
    private void PutSelection(ItemsControl host, IReadOnlyList<FileSystemEntry> rows, bool report) {
        if (IsSelectedAlready(host, rows)) {
            return;
        }

        // Measured because the shape used to be suspicious (a selection put
        // on row by row is quadratic inside WPF); PerfLog only writes a line
        // once a category is slow, so this one appearing in the log is
        // itself the answer.
        using (PerfLog.Measure("ui.selection-apply")) {
            _applyingSelection = true;
            try {
                switch (host) {
                    case FileListBox lb:
                        lb.ReplaceSelection(rows);
                        break;
                    case FileDataGrid dg:
                        dg.ReplaceSelection(rows);
                        break;
                }
            } finally {
                _applyingSelection = false;
            }
        }

        if (report) {
            ReportSelection(host);
        }
    }

    /// <summary>The control's selection is exactly these rows - the same objects, whatever the order.</summary>
    private static bool IsSelectedAlready(ItemsControl host, IReadOnlyList<FileSystemEntry> rows) {
        System.Collections.IList? current = host switch {
            ListBox lb => lb.SelectedItems,
            DataGrid dg => dg.SelectedItems,
            _ => null,
        };
        if (current is null || current.Count != rows.Count) {
            return false;
        }

        var wanted = new HashSet<object>(rows, ReferenceEqualityComparer.Instance);
        foreach (var item in current) {
            if (!wanted.Contains(item)) {
                return false;
            }
        }

        return true;
    }


    /// <summary>The list's row on <paramref name="path"/>, or null.</summary>
    private FileSystemEntry? EntryAt(string? path) {
        if (path is null) {
            return null;
        }

        foreach (var entry in Vm.Entries) {
            if (IsSamePath(entry.FullPath, path)) {
                return entry;
            }
        }

        return null;
    }

    /// <summary>The list's rows on <paramref name="paths"/>, the main one first.</summary>
    private List<FileSystemEntry> EntriesOf(IReadOnlyList<string> paths, string? primary) {
        var byPath = new Dictionary<string, FileSystemEntry>(Vm.Entries.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var entry in Vm.Entries) {
            byPath.TryAdd(entry.FullPath, entry);
        }

        var rows = new List<FileSystemEntry>(paths.Count);
        FileSystemEntry? main = null;
        if (primary is not null && byPath.TryGetValue(primary, out main)) {
            rows.Add(main);
        }
        foreach (string path in paths) {
            if (byPath.TryGetValue(path, out var row) && !ReferenceEquals(row, main)) {
                rows.Add(row);
            }
        }

        return rows;
    }

    private static bool IsSamePath(string? a, string? b) {
        return a is not null && b is not null && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }


    // --- Mouse gestures --------------------------------------------------

    private void List_MouseDoubleClick(object sender, MouseButtonEventArgs e) {
        // Double-clicking a word inside the rename editor must not open the
        // file that is being renamed.
        if (ListVisuals.IsInsideTextBox(e.OriginalSource) || ListVisuals.IsChrome(e.OriginalSource)) {
            return;
        }

        if (Vm.SelectedEntry is { } entry) {
            Vm.OpenEntry(entry);
        }
    }


    private void List_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
        _dragArmed = false;
        _dragOrigin = e.GetPosition(this);

        // A click inside the inline rename editor is a caret move, not a
        // selection change and not the start of a drag.
        if (ListVisuals.IsInsideTextBox(e.OriginalSource)) {
            return;
        }

        // The scroll bar and the column headers are the list's own chrome:
        // they are not "empty space", and treating them as such is what
        // turned dragging the scroll thumb into a selection sweep.
        if (ListVisuals.IsChrome(e.OriginalSource)) {
            return;
        }

        CommitRenameOnClickAway(e.OriginalSource);

        var clicked = ListVisuals.EntryAt(e.OriginalSource);
        if (clicked is null) {
            // Empty area: start a rubber-band lasso. The drag-source path
            // doesn't apply here (no source items), so we skip its arming
            // and own the gesture end-to-end via MouseMove / MouseUp.
            _selection.TryArmDeferred(sender, null, Vm.SelectedEntries, Keyboard.Modifiers);
            if (sender is ItemsControl host) {
                // Onto the list itself rather than onto a row: the click
                // means "the folder, not a file in it", and the first arrow
                // key enters the rows from there (see TryEnterList).
                TakeKeyboardOnClick(host, null);
                _rubberBand.Arm(host, e, Vm.SelectedEntries);
                e.Handled = true;
            }

            return;
        }
        _dragArmed = true;
        // A press on a row is where the keyboard would go next, whoever
        // ends up handling it — WPF, when the press is left to it, moves
        // focus without telling anyone.
        Vm.Workspace.Post(new ListCaretMoved(clicked.FullPath));

        if (_selection.TryArmDeferred(sender, clicked, Vm.SelectedEntries, Keyboard.Modifiers)) {
            TakeKeyboardOnClick(sender as ItemsControl, clicked);
            e.Handled = true;
        }
    }


    /// <summary>
    /// Brings the keyboard along with a click the list decided to handle
    /// itself.
    ///
    /// <para>
    /// Both branches above mark the press handled — one to own the lasso,
    /// the other to hold a multi-selection together until the button comes
    /// back up — and a handled press never reaches the control, so the list
    /// never focuses itself. The click then landed in the file area while
    /// the keyboard stayed in whichever panel it came from. Only when the
    /// keyboard is somewhere else: with it already inside the list, the
    /// caret is where the user put it and must not be moved.
    /// </para>
    /// </summary>
    private void TakeKeyboardOnClick(ItemsControl? host, FileSystemEntry? row) {
        if (host is null) {
            return;
        }

        // A press on a row moves the keyboard onto it — which is what WPF
        // would have done if the press had not been marked handled, and
        // what keeps the focus rectangle and the row the arrows count from
        // being the same row. A press on empty space moves nothing when the
        // keyboard is already in the list: the caret is where the user put
        // it, and clearing the selection does not move it.
        if (row is not null) {
            FocusEntry(row, scroll: true);

            return;
        }

        if (!host.IsKeyboardFocusWithin) {
            host.Focus();
        }
    }


    private void List_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e) {
        // Armed as well as active: a press on empty space that never moved
        // far enough to paint a rectangle still holds the mouse capture,
        // and letting go is where that ends. The selection it cleared on
        // the way down stays cleared — the gesture was a click on the
        // background, which is what clearing it means.
        if (_rubberBand.IsHost(sender)) {
            _rubberBand.End();
            e.Handled = true;

            return;
        }

        _selection.CommitOnMouseUp();
        _dragArmed = false;
    }


    private void List_PreviewMouseMove(object sender, MouseEventArgs e) {
        // Rubber-band wins over drag-source: if we started a marquee on
        // empty space, every subsequent mouse-move is selection-update,
        // not drag-arming.
        if (_rubberBand.IsHost(sender)) {
            // Defensive: if we missed the MouseUp (capture stolen, window
            // alt-tab, …), bail out cleanly the moment we see LMB up.
            if (e.LeftButton != MouseButtonState.Pressed) {
                _rubberBand.End();

                return;
            }

            _rubberBand.Update(e);
            e.Handled = true;

            return;
        }

        if (_rightDragArmed) {
            if (e.RightButton != MouseButtonState.Pressed) {
                _rightDragArmed = false;
            } else if (MovedPastDragThreshold(e.GetPosition(this), _rightDragOrigin)) {
                _rightDragArmed = false;
                StartDrag(sender, rightButton: true);
            }

            return;
        }

        if (!_dragArmed || e.LeftButton != MouseButtonState.Pressed) {
            return;
        }

        if (!MovedPastDragThreshold(e.GetPosition(this), _dragOrigin)) {
            return;
        }

        _dragArmed = false;
        _selection.NotifyDragStarted(); // drag started — keep the full selection
        StartDrag(sender, rightButton: false);
    }


    private static bool MovedPastDragThreshold(Point pos, Point origin) {
        return Math.Abs(pos.X - origin.X) >= SystemParameters.MinimumHorizontalDragDistance
            || Math.Abs(pos.Y - origin.Y) >= SystemParameters.MinimumVerticalDragDistance;
    }

    /// <summary>
    /// Hands the selection to the window's drag pipeline. The payload
    /// carries the companions; the paths - what the user selected - still
    /// drive the drag preview, because that is what they think they are
    /// dragging.
    /// </summary>
    private void StartDrag(object sender, bool rightButton) {
        var paths = Vm.SelectedEntries.Select(en => en.FullPath).ToArray();
        if (paths.Length == 0) {
            return;
        }

        DragStartRequested?.Invoke(this, new FileListDragRequest(
            (DependencyObject)sender,
            paths,
            Vm.WithCompanions(Vm.SelectedEntries).ToArray(),
            rightButton));
    }


    /// <summary>
    /// <c>Ctrl</c> + the wheel pressed puts the current view back to its
    /// standard size. The same finger that changed the size undoes it, which
    /// is the only reason this is a mouse gesture and not a hotkey.
    /// </summary>
    private void List_PreviewMouseDown(object sender, MouseButtonEventArgs e) {
        if (e.ChangedButton == MouseButton.Middle && Keyboard.Modifiers == ModifierKeys.Control) {
            Vm.ResetListSize();
            e.Handled = true;
        }
    }


    private void List_PreviewMouseWheel(object sender, MouseWheelEventArgs e) {
        // Ctrl + wheel resizes the current view (Explorer parity). One notch
        // of the wheel is one step; a free-spinning wheel can report a
        // fraction of a notch, so the division is what keeps a slow scroll
        // from doing nothing at all.
        if (Keyboard.Modifiers == ModifierKeys.Control) {
            int steps = e.Delta / Mouse.MouseWheelDeltaForOneLine;
            Vm.ZoomList(steps != 0 ? steps : Math.Sign(e.Delta));
            e.Handled = true;
            return;
        }

        if (ListVisuals.TryShiftScrollHorizontally((DependencyObject)sender, e)) {
            e.Handled = true;
        }
    }


    // --- Context menu ----------------------------------------------------

    private void List_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e) {
        if (sender is not ItemsControl host || ListVisuals.IsChrome(e.OriginalSource)) {
            return;
        }

        CommitRenameOnClickAway(e.OriginalSource);

        var clicked = ListVisuals.EntryAt(e.OriginalSource);
        _contextIsBackground = clicked is null;

        if (clicked is null) {
            // Explorer parity: right-clicking empty space drops the
            // selection and offers the folder's own menu instead.
            ClearListSelection(host);
        } else if (!Vm.SelectedEntries.Contains(clicked)) {
            // Right-clicking outside the selection moves it to the clicked
            // row; right-clicking inside one keeps the whole multi-selection.
            SetListSelection(host, new[] { clicked });
        }

        // A row under the right button can be dragged as well; the menu on
        // release is what a press that never moved gets.
        _rightDragArmed = clicked is not null;
        _rightDragOrigin = e.GetPosition(this);
    }

    private void List_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e) {
        _rightDragArmed = false;
        if (sender is FrameworkElement host && !ListVisuals.IsChrome(e.OriginalSource)) {
            ContextMenuRequested?.Invoke(this, new FileListMenuRequest(host, PlacementMode.MousePoint, _contextIsBackground));
            e.Handled = true;
        }
    }

    private void List_PreviewKeyDown(object sender, KeyEventArgs e) {
        // Shift+F10 arrives as a system key, the dedicated Menu key doesn't.
        bool menuKey = e.Key == Key.Apps
            || (e.Key == Key.System && e.SystemKey == Key.F10 && Keyboard.Modifiers == ModifierKeys.Shift);
        if (menuKey && sender is FrameworkElement host) {
            _contextIsBackground = Vm.SelectedEntries.Count == 0;
            e.Handled = true;

            // The dedicated Menu key opens on its release, not here: an
            // unhandled Apps key-up goes to DefWindowProc, which turns it
            // into WM_CONTEXTMENU - and that late event landed on the menu
            // opened at the press and dismissed it, which is why it
            // appeared and vanished. See List_PreviewKeyUp, which both
            // opens the menu and swallows the key-up. Shift+F10 makes its
            // WM_CONTEXTMENU on the press instead, so the press being
            // handled is enough and the menu can open right away.
            if (e.Key != Key.Apps) {
                ContextMenuRequested?.Invoke(
                    this, new FileListMenuRequest(host, PlacementMode.Center, _contextIsBackground));
            }

            return;
        }

        if (TryRateFromKeyboard(e.Key)) {
            e.Handled = true;

            return;
        }

        // Enter or Space on pictures in the gallery: full screen, as in a
        // viewer (PLAN Q5) - one, two together, or more one by one. A
        // selection with anything but pictures keeps Enter = open.
        if (Vm.ViewMode == ViewMode.Gallery && e.Key is Key.Enter or Key.Space
            && Keyboard.Modifiers == ModifierKeys.None && Vm.RenamingPath is null
            && FullscreenRequested is not null
            && FullscreenPlan.Of(Vm.SelectedEntries, Vm.Entries, Vm.CaretPath) is { } plan) {
            // Not on a held key: full screen closes on its own Enter, and
            // the repeat would open it again, and again.
            if (!e.IsRepeat) {
                FullscreenRequested(this, plan);
            }
            e.Handled = true;

            return;
        }

        // Enter in the table. DataGrid answers the key itself - commits an
        // edit, moves the current cell down a row - and marks it handled,
        // so the window's KeyBinding (Enter -> Open) never saw it and the
        // table was the one view where Enter opened nothing. This tunnelling
        // handler runs first; the rename editor is checked for the same way
        // the digits and arrows check for it.
        if (sender is DataGrid && e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None
            && Vm.RenamingPath is null) {
            if (Vm.OpenCommand.CanExecute(null)) {
                Vm.OpenCommand.Execute(null);
            }
            e.Handled = true;

            return;
        }

        if (sender is Selector target && TryEnterList(target, e.Key)) {
            e.Handled = true;

            return;
        }

        if (sender is ListBox list && TryGridStep(list, e.Key, Keyboard.Modifiers)) {
            e.Handled = true;
        }
    }


    /// <summary>
    /// The Menu key's other half. The menu opens here, on the release, and
    /// the release is marked handled so DefWindowProc never synthesises
    /// WM_CONTEXTMENU out of it - the message that used to dismiss the menu
    /// the moment it opened. Nothing is deferred: with the key-up consumed
    /// there is no keyboard event left in flight for the menu to misread.
    /// </summary>
    private void List_PreviewKeyUp(object sender, KeyEventArgs e) {
        if (e.Key == Key.Apps && sender is FrameworkElement host) {
            e.Handled = true;
            ContextMenuRequested?.Invoke(
                this, new FileListMenuRequest(host, PlacementMode.Center, _contextIsBackground));
        }
    }


    /// <summary>
    /// <c>0</c>…<c>5</c> in the gallery: set that many stars on everything
    /// selected. The keys every photo browser uses for it, and the reason
    /// the gallery exists — going through a shoot means rating without
    /// taking a hand off the arrow keys.
    ///
    /// <para>
    /// Only in the gallery, and that is a real trade: in the other views
    /// digits belong to type-ahead, and a folder of files named
    /// <c>2024-05-…</c> would become unreachable by typing if this were
    /// global. The gallery is the one view where names are not how you find
    /// things.
    /// </para>
    ///
    /// <para>
    /// <c>Shift</c> + the same digits is the colour label - the second
    /// thing a sidecar records, on the same keys. The other modifiers are
    /// taken: <c>Ctrl</c> + digits are window zones and <c>Ctrl</c> +
    /// <c>Shift</c> + digits are the view modes. The numeric keypad only
    /// rates: with <c>Shift</c> held Windows turns it into Home / End and
    /// the arrows, and those never arrive here as digits.
    /// </para>
    /// </summary>
    private bool TryRateFromKeyboard(Key key) {
        // A digit typed into the rename editor is part of the name. The
        // tunnelling handler sees it before the editor does, so the editor
        // has to be checked for here, as TryEnterList and TryGridStep do.
        var modifiers = Keyboard.Modifiers;
        bool colour = modifiers == ModifierKeys.Shift;
        if (Vm.ViewMode != ViewMode.Gallery || (modifiers != ModifierKeys.None && !colour)
            || Vm.RenamingPath is not null) {
            return false;
        }

        int index = key switch {
            >= Key.D0 and <= Key.D5 => key - Key.D0,
            >= Key.NumPad0 and <= Key.NumPad5 when !colour => key - Key.NumPad0,
            _ => -1,
        };
        if (index < 0 || Vm.SelectedEntries.Count == 0) {
            return false;
        }

        if (colour) {
            Vm.SetColorForSelection(index);
        } else {
            Vm.SetRankForSelection(index.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        return true;
    }


    /// <summary>
    /// The first arrow key pressed with the keyboard on the list itself
    /// rather than on one of its rows.
    ///
    /// <para>
    /// That state is where <c>Ctrl+1</c> and a click on empty space both
    /// leave the focus, and WPF answers an arrow key there with nothing at
    /// all — there is no caret to move from, so the list is dead until the
    /// mouse rescues it. Down and Right enter at the top, Up and Left at the
    /// bottom, which is what Explorer does from the same state. With a
    /// selection still standing the caret goes back onto it instead: the
    /// press means "put me back in the list", not "jump to the end of it".
    /// </para>
    /// </summary>
    private bool TryEnterList(Selector list, Key key) {
        if (Vm.RenamingPath is not null || Keyboard.Modifiers != ModifierKeys.None) {
            return false;
        }

        // A row that already has the keyboard handles its own arrows.
        if (Keyboard.FocusedElement is ListBoxItem or DataGridCell) {
            return false;
        }

        var entries = Vm.Entries;
        if (entries.Count == 0 || key is not (Key.Up or Key.Down or Key.Left or Key.Right)) {
            return false;
        }

        if (Vm.SelectedEntry is { } selected && entries.Contains(selected)) {
            TakeRow(selected);

            return true;
        }

        // Nothing selected, but the focus rectangle is still on the row the
        // user last stood on — a click on empty space leaves exactly that.
        // The press means "back into the list", and the list resumes where
        // the rectangle is rather than at its top edge.
        if (CaretEntry() is { } caret) {
            SetListSelection(list, new[] { caret });
            TakeRow(caret);

            return true;
        }

        var entry = key is Key.Down or Key.Right ? entries[0] : entries[^1];
        SetListSelection(list, new[] { entry });
        TakeRow(entry);

        return true;
    }


    // --- Arrow keys at the edge of a wrap layout -------------------------

    /// <summary>
    /// WPF moves the selection to the nearest container in the direction
    /// pressed and does nothing when there is none — which is every row end,
    /// the whole top row and the whole bottom row. <see cref="GridNavigation"/>
    /// says where those presses belong: the grid is one list folded into
    /// rows, so Right runs off the end of a row into the next one, and Up /
    /// Down at the outer rows reach the first / last item.
    ///
    /// <para>
    /// `Shift` extends across the edge the same way it extends inside the
    /// grid — the selection grows from the anchor to wherever the caret
    /// lands. `Ctrl` (move without selecting) is left to the control:
    /// standing in for it means owning the caret separately from the
    /// selection, and the edges are not worth that. Details has a single
    /// column and is left alone entirely: there is no row to wrap into, and
    /// Left / Right there belong to the grid's own cell navigation.
    /// </para>
    /// </summary>
    private bool TryGridStep(ListBox list, Key key, ModifierKeys modifiers) {
        bool extend = modifiers == ModifierKeys.Shift;
        if ((modifiers != ModifierKeys.None && !extend) || Vm.RenamingPath is not null) {
            return false;
        }

        GridStep? step = key switch {
            Key.Left => GridStep.Left,
            Key.Right => GridStep.Right,
            Key.Up => GridStep.Up,
            Key.Down => GridStep.Down,
            _ => null,
        };
        if (step is null) {
            return false;
        }

        // The panel is the only thing that knows how many cells fit a row,
        // and it knows it only after a layout pass.
        if (ListVisuals.FindDescendant<VirtualizingWrapPanel>(list) is not { Columns: > 0 } panel) {
            return false;
        }

        var entries = Vm.Entries;
        int index = CaretIndex(list, entries);
        if (!GridNavigation.IsAtEdge(index, step.Value, panel.Columns, entries.Count)) {
            return false;
        }

        int target = GridNavigation.Move(index, step.Value, panel.Columns, entries.Count);
        if (target < 0) {
            // An edge with nothing beyond it — the first item pressing Up,
            // the last pressing Down. Left for WPF to do nothing about.
            return false;
        }

        SetListSelection(list, extend
            ? Range(entries, AnchorIndex(list, entries, index), target)
            : new[] { entries[target] });
        TakeRow(entries[target]);

        return true;
    }


    /// <summary>
    /// Where the caret is — the focused row, not the selected one. With
    /// Shift held the two part company (the selection is a run, the caret
    /// is one end of it), and an arrow key moves the caret.
    /// </summary>
    private int CaretIndex(ListBox list, IList<FileSystemEntry> entries) {
        if (Keyboard.FocusedElement is ListBoxItem row) {
            int focused = list.ItemContainerGenerator.IndexFromContainer(row);
            if (focused >= 0) {
                return focused;
            }
        }

        if (Vm.SelectedEntry is { } selected) {
            return entries.IndexOf(selected);
        }

        return CaretEntry() is { } caret ? entries.IndexOf(caret) : -1;
    }


    /// <summary>
    /// The row the focus rectangle is on, or null when it points at
    /// something this folder no longer has — a file deleted, a folder left
    /// and come back to with the list re-read.
    /// </summary>
    private FileSystemEntry? CaretEntry() {
        if (Vm.CaretPath is not { Length: > 0 } path) {
            return null;
        }

        foreach (var entry in Vm.Entries) {
            if (string.Equals(entry.FullPath, path, StringComparison.OrdinalIgnoreCase)) {
                return entry;
            }
        }

        return null;
    }


    /// <summary>
    /// The row a Shift-extension grows from. WPF keeps its own anchor
    /// privately, so this reads it back off the selection instead - which
    /// end of the run that is belongs to <see cref="GridNavigation.Anchor"/>.
    /// </summary>
    private static int AnchorIndex(ListBox list, IList<FileSystemEntry> entries, int caret) {
        var selected = new HashSet<FileSystemEntry>(list.SelectedItems.OfType<FileSystemEntry>());

        return GridNavigation.Anchor(caret, entries.Count, i => selected.Contains(entries[i]));
    }


    private static IEnumerable<FileSystemEntry> Range(IList<FileSystemEntry> entries, int a, int b) {
        var run = new List<FileSystemEntry>();
        for (int i = Math.Min(a, b); i <= Math.Max(a, b); i++) {
            run.Add(entries[i]);
        }

        return run;
    }


    // --- Type-ahead ------------------------------------------------------

    /// <summary>
    /// Letters typed into the list jump to the file whose name starts with
    /// them (Explorer parity). The prefix and the "same letter cycles"
    /// behaviour live in <see cref="TypeAheadController"/>; this end only
    /// decides whether the keystroke is meant for the list at all, and
    /// moves the selection when it is.
    ///
    /// <para>
    /// The handler is a tunnelling one on the container, so it sees input
    /// destined for the inline rename editor inside a row before the editor
    /// does — hence the explicit stand-down while a name is being edited.
    /// </para>
    /// </summary>
    private void List_PreviewTextInput(object sender, TextCompositionEventArgs e) {
        if (Vm.RenamingPath is not null || Keyboard.Modifiers is ModifierKeys.Control or ModifierKeys.Alt) {
            return;
        }

        // Space is how the keyboard toggles the current row's selection;
        // taking it for a search that starts with a space helps nobody.
        if (e.Text == " ") {
            return;
        }

        var entries = Vm.Entries;
        int current = Vm.SelectedEntry is { } selected ? entries.IndexOf(selected) : -1;
        int target = _typeAhead.Type(e.Text, entries.Select(x => x.Name).ToList(), current);
        if (target < 0) {
            // Nothing matches — swallow it anyway, so the keystroke doesn't
            // fall through to whatever else might act on a letter.
            e.Handled = true;
            return;
        }

        if (sender is ItemsControl host) {
            SetListSelection(host, new[] { entries[target] });
            TakeRow(entries[target]);
        }
        e.Handled = true;
    }


    // --- Which view holds the rows ---------------------------------------
    // Only the view on screen is bound to Entries; see ARCHITECTURE.md,
    // "Вид, которого не видно, не строит ничего".

    /// <summary>
    /// Binds the view on screen to the rows and unbinds the ones that are
    /// not. Runs on every mode change, so the incoming view is bound before
    /// it is shown.
    ///
    /// <para>
    /// Detaching goes first and attaching last: a view losing its rows
    /// reports an empty selection, and that report must not land after the
    /// incoming view has re-selected the row.
    /// </para>
    /// </summary>
    private void ApplyViewAttachment() {
        var active = ActiveList();
        Selector[] views = { DetailsView, TilesView, IconsView, GalleryView };

        foreach (var view in views) {
            if (ShouldDetach(view, active) && _detachedViews.Add(view)) {
                // The selection on its way out is the control emptying, not
                // the user deselecting anything (List_SelectionChanged).
                view.ItemsSource = null;
            }
        }

        bool attached = false;
        foreach (var view in views) {
            if (!ShouldDetach(view, active) && _detachedViews.Remove(view)) {
                view.SetBinding(ItemsControl.ItemsSourceProperty, new Binding(nameof(MainViewModel.Entries)));
                attached = true;
            }
        }

        // Every control keeps its own SelectedItems, so the view coming on
        // screen is handed the model's selection whether or not it was just
        // bound: three rows picked in the table showed as one in the tiles
        // while Ctrl+C still copied three. Unchanged is a no-op. A view that
        // has just been bound is also scrolled to the top, so that one is
        // brought back to the selection.
        if (active is not null && Vm.SelectedEntries.Count > 0) {
            ApplySelection(Vm.Workspace.State.List, scroll: attached);
        }
    }


    /// <summary>
    /// Should this view be holding no rows right now?
    ///
    /// <para>
    /// Only the table, and only when it is not the view on screen. Its
    /// panel is WPF's own, so unlike <see cref="VirtualizingWrapPanel"/> it
    /// cannot be told to build nothing while the control around it is
    /// collapsed - and a Reset on the shared rows reaches it all the same,
    /// so every navigation had it realise and tear down a screenful of rows
    /// behind a collapsed table (PLAN R2, T2). The three tile views already
    /// build nothing when hidden and stay bound.
    /// </para>
    /// </summary>
    private bool ShouldDetach(Selector view, ItemsControl? active) {
        return !ReferenceEquals(view, active) && ReferenceEquals(view, DetailsView);
    }


    private static void ScrollRowIntoView(ItemsControl host, FileSystemEntry entry) {
        switch (host) {
            case DataGrid grid: grid.ScrollIntoView(entry); break;
            case ListBox list: list.ScrollIntoView(entry); break;
        }
    }



    // --- The filter field (PLAN G6) ----------------------------------------
    // On the strip over the list since 2026-09-25; it used to be in the
    // window's toolbar, and the window still reaches it (Ctrl+F, the Tab
    // ring) through SearchBox.

    private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e) {
        // Esc: one press does the lot — stop whatever is running, drop the
        // filter and the results, and put the keyboard back in the list. A
        // ladder of three presses meant the user had to know which rung
        // they were on, and the answer to "what is going on" is never
        // "press it again".
        if (e.Key == Key.Escape) {
            if (Vm.ContentSearch.IsRunning) {
                Vm.StopSearchCommand.Execute(null);
            }
            Vm.ClearSearchCommand.Execute(null);
            FocusList();
            e.Handled = true;

            return;
        }

        // Enter: the box is the shallow half — the filter has already been
        // applied letter by letter — so Enter only moves the keyboard to
        // the results. A deep search is set up in the search window, and
        // Enter belongs to it there.
        if (e.Key == Key.Enter) {
            FocusList();
            e.Handled = true;
        }
    }

    private void SearchOptions_Click(object sender, RoutedEventArgs e) {
        SearchWindowRequested?.Invoke(this, EventArgs.Empty);
    }



    // --- Rename ----------------------------------------------------------
    // In-place editing (A3) is the normal path: one TextBox for the whole
    // control, laid over the row's name label by a RenameAdorner while
    // MainViewModel.RenamingPath says which row. The row templates carry
    // no editor of their own - a TextBox in every row was the single most
    // expensive thing in them (PLAN R, 2026-09-02). PromptDialog stays as
    // the fallback for the case the inline editor cannot be reached - a row
    // that virtualisation has not realised.

    public void StartRename() {
        if (Vm.SelectedEntry is not FileSystemEntry entry) {
            return;
        }

        if (TryStartInlineRename(entry)) {
            return;
        }

        string? input = ServiceLocator.Get<IDialogs>().Prompt(Strings.RenameTitle, Strings.RenamePrompt, entry.Name, filenameMode: true);
        if (input is null || input == entry.Name) {
            return;
        }

        Vm.RenameCommand.Execute(input);
    }


    private bool TryStartInlineRename(FileSystemEntry entry) {
        var list = ActiveList();
        if (list is null) {
            return false;
        }

        // The row has to exist as a visual before its editor can be focused,
        // and a row scrolled out of a virtualising panel does not.
        ScrollIntoView(list, entry);
        list.UpdateLayout();
        if (list.ItemContainerGenerator.ContainerFromItem(entry) is not FrameworkElement container) {
            return false;
        }

        // The editor sits over the name label, and needs the label's adorner
        // layer - the scroll viewport's, so it moves and clips with the row.
        var label = ListVisuals.FindDescendant<TextBlock>(container, "NameLabel");
        if (label is null || AdornerLayer.GetAdornerLayer(label) is not { } layer) {
            return false;
        }

        Vm.BeginRename(entry);
        if (Vm.RenamingPath is null) {
            return false;
        }

        HideRenameEditor();
        var box = CreateRenameEditor(entry, label);
        _renameAdorner = new RenameAdorner(label, box);
        _renameBox = box;
        _renameLayer = layer;
        layer.Add(_renameAdorner);
        layer.UpdateLayout();
        box.Focus();
        SelectNameWithoutExtension(box, entry);

        return true;
    }


    /// <summary>
    /// A fresh TextBox per edit rather than one kept for the control's
    /// lifetime: an adorner owns its child visual, and a reused TextBox
    /// would have to be pulled out of the last adorner before it could go
    /// into the next. One TextBox per rename costs nothing anyone can see.
    /// </summary>
    private TextBox CreateRenameEditor(FileSystemEntry entry, TextBlock label) {
        var box = new TextBox {
            // The row, for the Escape path that puts focus back on it.
            DataContext = entry,
            Text = entry.Name,
            FontSize = label.FontSize,
            Padding = new Thickness(0),
            MinWidth = 60,
            // A name is wider than the label it is edited in — that is why
            // it is being renamed at all — and a single-line box answers
            // that by scrolling, so the user edits a name they can see six
            // characters of. Wrapping shows the whole of it instead; the
            // adorner grows the editor to the height the wrapped text needs
            // (see RenameAdorner), and Enter still commits because
            // AcceptsReturn is left off.
            TextWrapping = TextWrapping.Wrap,
        };
        box.PreviewKeyDown += RenameBox_PreviewKeyDown;
        box.PreviewTextInput += RenameBox_PreviewTextInput;
        box.LostKeyboardFocus += RenameBox_LostKeyboardFocus;

        return box;
    }


    /// <summary>
    /// Takes the editor down. Idempotent, and quiet about focus: the caller
    /// decides where the keyboard goes next.
    /// </summary>
    private void HideRenameEditor() {
        _renameBox = null;
        if (_renameAdorner is not { } adorner) {
            return;
        }

        // The layer is remembered rather than looked up again: a row
        // recycled out from under the editor has no layer above it any
        // more, and the adorner would stay in the one it was added to.
        _renameAdorner = null;
        _renameLayer?.Remove(adorner);
        _renameLayer = null;
    }


    private static void ScrollIntoView(ItemsControl list, FileSystemEntry entry) {
        switch (list) {
            case DataGrid dg: dg.ScrollIntoView(entry); break;
            case ListBox lb: lb.ScrollIntoView(entry); break;
        }
    }


    /// <summary>
    /// Explorer parity: the extension stays out of the initial selection, so
    /// typing replaces the name and leaves ".png" alone. Folders and
    /// dot-files ("<c>.gitignore</c>") select whole — there is no extension
    /// to protect there.
    /// </summary>
    private static void SelectNameWithoutExtension(TextBox box, FileSystemEntry entry) {
        int dot = entry.Kind == EntryKind.Directory ? -1 : entry.Name.LastIndexOf('.');
        if (dot > 0) {
            box.Select(0, dot);
        } else {
            box.SelectAll();
        }
    }


    private void RenameBox_PreviewKeyDown(object sender, KeyEventArgs e) {
        // Enter and Escape both have window-level KeyBindings (Open / clear
        // selection), so they must be swallowed here or renaming a file
        // would open it. Delete and Backspace need no such guard — the
        // TextBox marks those handled itself.
        if (e.Key == Key.Enter) {
            CommitInlineRename((TextBox)sender, takeFocus: true);
            e.Handled = true;

            return;
        }

        if (e.Key == Key.Escape) {
            // The row comes from the editor's own DataContext, not from the
            // selection: this is the row the user was editing, whatever the
            // selection happens to be by now.
            var edited = ((FrameworkElement)sender).DataContext as FileSystemEntry;
            Vm.CancelRename();
            if (EntryAt(edited?.FullPath) is { } row) {
                TakeRow(row);
            } else {
                FocusList();
            }
            e.Handled = true;
        }
    }

    private void RenameBox_PreviewTextInput(object sender, TextCompositionEventArgs e) {
        // Refuse the characters Windows will not accept in a name at input
        // time, the way PromptDialog does — a rejected rename after the fact
        // would just lose what the user typed.
        if (e.Text.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) {
            e.Handled = true;
            Vm.Status = Strings.InvalidFileNameChars + "\\ / : * ? \" < > |";
        }
    }

    /// <summary>
    /// A click landed somewhere in the list while a name was being edited:
    /// apply the edit, the way Explorer does.
    ///
    /// <para>
    /// The editor normally commits on losing the keyboard, but a press the
    /// list handles itself never takes the keyboard off it — a click on
    /// empty space is handled to own the lasso, and the focus stays in the
    /// editor because the editor <em>is</em> inside the list. So the editor
    /// hung there over a folder that had already dropped its selection.
    /// </para>
    /// </summary>
    private void CommitRenameOnClickAway(object originalSource) {
        if (_renameBox is not { } box || ListVisuals.IsInsideTextBox(originalSource)) {
            return;
        }

        CommitInlineRename(box, takeFocus: false);

        // And take the keyboard off the editor now. The editor is removed a
        // moment later (the view model clears RenamingPath, and the rename
        // itself finishes asynchronously), and WPF answers the removal of
        // the focused element by handing focus to the window — where the
        // list's own key handlers never see an arrow press. The press that
        // follows this one puts the keyboard on a row; this only makes sure
        // it is not left nowhere.
        ActiveList()?.Focus();
    }

    private void RenameBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) {
        // Clicking away commits, matching Explorer. Escape has already
        // cleared RenamingPath by the time focus leaves, so a cancelled edit
        // falls out of CommitInlineRename on its own. No focus grab here:
        // the user has just clicked somewhere and that is where focus
        // belongs.
        CommitInlineRename((TextBox)sender, takeFocus: false);
    }

    /// <summary>
    /// Applies the edited name. <paramref name="takeFocus"/> separates the
    /// two ways an edit ends: Enter means the user is still working in the
    /// list and the keyboard belongs there, while a click elsewhere means
    /// they have already moved on and focus must stay where they put it.
    /// </summary>
    private void CommitInlineRename(TextBox box, bool takeFocus) {
        if (Vm.RenamingPath is null) {
            return;
        }

        // The re-listing that follows is asynchronous, so the renamed row is
        // focused twice over: now (it is still there under its old name,
        // which keeps the keyboard inside the list) and again when the new
        // listing lands, the model sending the keyboard onto the new name
        // (K-4).
        Vm.CommitRename(box.Text, takeFocus);
        if (takeFocus) {
            FocusList();
        }
    }
}


/// <summary>What the window needs to run a drag started in the list.</summary>
/// <param name="Source">The control the drag originated from.</param>
/// <param name="Paths">What the user selected — drives the drag preview.</param>
/// <param name="Payload">What actually travels, companions included.</param>
/// <param name="RightButton">The drag is held by the right mouse button: the drop opens a menu instead of acting.</param>
public sealed record FileListDragRequest(DependencyObject Source, string[] Paths, string[] Payload, bool RightButton = false);

/// <summary>Where and in what mode the list wants its context menu.</summary>
public sealed record FileListMenuRequest(FrameworkElement Host, PlacementMode Placement, bool IsBackground);
