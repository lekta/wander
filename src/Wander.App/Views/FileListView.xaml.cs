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
using Wander.Core.Layout;
using Wander.Core.Logging;

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

    // A committed rename re-lists the folder asynchronously, so the row to
    // put the keyboard on does not exist yet when the editor closes. This
    // says "the next selection restore is mine" — undo and refresh must not
    // steal focus from wherever the user actually is.
    private bool _focusRowAfterRestore;

    /// <summary>Set on right-button-down: did the click land on empty space?</summary>
    private bool _contextIsBackground;

    /// <summary>Jump-to-name from the keyboard; see <see cref="List_PreviewTextInput"/>.</summary>
    private readonly TypeAheadController _typeAhead = new();


    /// <summary>
    /// The keyboard was in here and went nowhere — the row that had it was
    /// rebuilt out of existence and focus fell back onto the window. See
    /// <see cref="OnKeyboardFocusWithinChanged"/>.
    /// </summary>
    private bool _focusFellOutOfTheList;

    /// <summary>The views currently unbound from the rows - see <see cref="ApplyViewAttachment"/>.</summary>
    private readonly HashSet<Selector> _detachedViews = new();

    /// <summary>
    /// True while <see cref="SetListSelection"/> is putting a selection on
    /// row by row. The selection handler steps aside for the round and is
    /// told once at the end.
    /// </summary>
    private bool _applyingSelection;

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
        IsKeyboardFocusWithinChanged += OnKeyboardFocusWithinChanged;
    }


    /// <summary>
    /// The user started dragging the current selection out of the list. The
    /// window answers by running the drag loop with its preview window.
    /// </summary>
    public event EventHandler<FileListDragRequest>? DragStartRequested;

    /// <summary>The list wants its context menu shown.</summary>
    public event EventHandler<FileListMenuRequest>? ContextMenuRequested;


    private MainViewModel Vm => (MainViewModel)DataContext;


    /// <summary>
    /// Notices the keyboard leaving for nowhere.
    ///
    /// <para>
    /// A rebuild that replaces the focused row leaves WPF with nothing to
    /// move focus to, so it hands it to the window. That is not the user
    /// going somewhere — it is the list dropping them — and the next
    /// selection restore takes it back. Focus landing on a real element (the
    /// address bar, the tree, the search box) is the user going somewhere,
    /// and is left alone.
    /// </para>
    /// </summary>
    private void OnKeyboardFocusWithinChanged(object sender, DependencyPropertyChangedEventArgs e) {
        if (IsKeyboardFocusWithin) {
            _focusFellOutOfTheList = false;

            return;
        }

        _focusFellOutOfTheList = Keyboard.FocusedElement is null or Window;
    }


    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e) {
        if (e.OldValue is MainViewModel old) {
            old.SelectionRestoreRequested -= RestoreListSelection;
            old.SelectionRefreshRequested -= RefreshListSelection;
            old.InlineRenameRequested -= StartRenameOn;
            old.PropertyChanged -= OnViewModelChanged;
            old.Settings.PropertyChanged -= OnSettingsChanged;
            old.FolderArrived -= OnFolderArrived;
            old.Entries.CollectionChanged -= OnEntriesChanged;
        }
        if (e.NewValue is MainViewModel vm) {
            vm.SelectionRestoreRequested += RestoreListSelection;
            vm.SelectionRefreshRequested += RefreshListSelection;
            vm.InlineRenameRequested += StartRenameOn;
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
            KeepFocusAcrossViewSwap();
        } else if (e.PropertyName == nameof(MainViewModel.RenamingPath) && Vm.RenamingPath is null) {
            // The view model ended the edit - a commit, an Escape, or a
            // listing rebuilt under the editor. The editor goes with it.
            HideRenameEditor();
        }
    }


    /// <summary>
    /// Carries the keyboard from one view to the next when the mode changes.
    ///
    /// <para>
    /// The four views are four controls, and switching mode collapses the one
    /// that had the keyboard. Focus then falls to the window, and the file
    /// area is left looking selected but answering to nothing — walk into a
    /// folder of photographs, which turns the gallery on by itself, and the
    /// next arrow key went nowhere. Only done when the area already had the
    /// keyboard: a mode changed from the toolbar or the settings dialog must
    /// not pull focus out of wherever the user is.
    /// </para>
    /// </summary>
    private void KeepFocusAcrossViewSwap() {
        if (!IsKeyboardFocusWithin) {
            return;
        }

        // After the swap, not during it: the incoming control is still
        // collapsed at this point and cannot take focus.
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => {
            if (DataContext is MainViewModel) {
                FocusList();
            }
        }));
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

            FirstScreenWatch.Begin(path, clock, VisibleIcons(), ServiceLocator.Get<ILogger>());
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
    // FocusList, ClearSelection and StartRename (in the rename section);
    // everything else here is this control's own business.

    /// <summary>
    /// Hands the keyboard back to the list — to the selected row when there
    /// is one. Focusing the container itself is not enough: a list with
    /// focus but no focused item leaves the arrow keys resuming from
    /// wherever the cursor was last, which is usually the top.
    /// </summary>
    public void FocusList() {
        if (Vm.SelectedEntry is { } entry) {
            // Falling back to the list itself is right only when there is
            // nothing to stand on. When there *is* a row and its container
            // simply has not been realised yet, taking the fallback leaves
            // the keyboard on the list with no row under it — which is the
            // state where the next arrow key starts from the top instead of
            // from where the user was.
            FocusRowWhenReady(entry);

            return;
        }

        ActiveList()?.Focus();
    }


    /// <summary>Clears the selection in whichever container is on screen.</summary>
    public void ClearSelection() {
        SelectionController.ClearActive(ActiveList());
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
    /// Puts the keyboard on one row. Returns false when the row has no
    /// realised container (virtualised away), so the caller can fall back
    /// to focusing the list itself.
    /// </summary>
    /// <summary>
    /// Puts the keyboard on a row, waiting for it to exist if it does not yet.
    ///
    /// <para>
    /// <see cref="FocusRow"/> can only focus a container the panel has
    /// realised, and right after a listing lands there may not be one: the
    /// rows arrived a moment ago, the panel is still generating containers,
    /// and a folder that is also loading thumbnails and expanding a tree
    /// branch gives it plenty of reason to be late. Failing there is silent —
    /// the row ends up selected but the keyboard stays on the list, and the
    /// next arrow key resumes from the top instead of the row. One retry
    /// after the layout pass is enough; anything still missing then is a row
    /// that genuinely is not in the list.
    /// </para>
    /// </summary>
    private void FocusRowWhenReady(FileSystemEntry entry) {
        if (FocusRow(entry)) {
            return;
        }

        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => {
            if (DataContext is not MainViewModel || !Vm.Entries.Contains(entry)) {
                return;
            }

            if (!FocusRow(entry)) {
                // Still nothing to stand on. The list itself is a worse place
                // for the keyboard than the row, but a better one than the
                // window — from here Tab and the arrow keys still work.
                ActiveList()?.Focus();
            }
        }));
    }


    private bool FocusRow(FileSystemEntry entry) {
        // The caret follows the keyboard, and this is where the keyboard is
        // put on a row deliberately. The presses WPF answers by itself
        // (an arrow inside the grid) are picked up in List_SelectionChanged.
        Vm.CaretPath = entry.FullPath;

        switch (ActiveList()) {
            case DataGrid dg:
                dg.ScrollIntoView(entry);
                dg.UpdateLayout();
                // Arrow keys in a DataGrid follow the *current cell*, not the
                // selection. Leaving it stale is what made the next arrow
                // press jump to a row near the top instead of the neighbour.
                if (dg.Columns.Count > 0) {
                    dg.CurrentCell = new DataGridCellInfo(entry, dg.Columns[0]);
                }
                if (dg.ItemContainerGenerator.ContainerFromItem(entry) is DataGridRow row
                    && ListVisuals.FindDescendant<DataGridCell>(row) is { } cell) {
                    return cell.Focus();
                }

                return false;

            case ListBox lb:
                lb.ScrollIntoView(entry);
                lb.UpdateLayout();

                return lb.ItemContainerGenerator.ContainerFromItem(entry) is ListBoxItem item
                    && item.Focus();

            default:
                return false;
        }
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

        // A selection being put on by the code below arrives one row at a
        // time and raises this on every add, and every raise walks the
        // whole of SelectedItems again - 5000 rows cost 12.5 million walked
        // rows and eleven seconds (measured, PLAN block 0 step 0.7).
        // SetListSelection reports once, when it is done.
        if (_applyingSelection) {
            return;
        }

        ReportSelection(sender);
    }

    /// <summary>
    /// Hands the control's current selection to the view model. One call
    /// per real selection change - the user's, or one finished round of
    /// <see cref="SetListSelection"/>.
    /// </summary>
    private void ReportSelection(object host) {
        var entries = new List<FileSystemEntry>();
        switch (host) {
            case DataGrid dg:
                foreach (var item in dg.SelectedItems) {
                    if (item is FileSystemEntry fe) {
                        entries.Add(fe);
                    }
                }
                break;
            case ListBox lb:
                foreach (var item in lb.SelectedItems) {
                    if (item is FileSystemEntry fe) {
                        entries.Add(fe);
                    }
                }
                break;
        }
        Vm.SelectedEntries = entries;
        UpdateCaret(entries);
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
    private void UpdateCaret(IReadOnlyList<FileSystemEntry> selected) {
        // Nothing here walks the listing: a marquee over five thousand
        // files raises this once per file (ApplyDelta adds them one at a
        // time), and a scan of the rows in each of those would be the
        // gesture's cost squared.
        if (Keyboard.FocusedElement is FrameworkElement { DataContext: FileSystemEntry focused }) {
            Vm.CaretPath = focused.FullPath;

            return;
        }

        if (selected.Count > 0) {
            Vm.CaretPath = selected[^1].FullPath;
        }
    }


    /// <summary>
    /// Puts the selection back after a refresh reconciled the list. Only the
    /// controls know about multi-selection (SelectedItems is theirs, not the
    /// view model's), so the VM asks and this does it.
    /// </summary>
    private void RestoreListSelection(IReadOnlyList<FileSystemEntry> items) {
        // Two reasons to take the keyboard back: a rename that just ended
        // (the row is the one the user was editing) and an operation that
        // ran behind a modal dialog, which left focus on the window.
        // Third reason, and the one no caller can predict: the listing that
        // just landed replaced the row that had the keyboard, so focus fell
        // back onto the list itself. That is the dotted rectangle round the
        // whole area after Alt+Left — and, because a list with focus but no
        // focused row resumes from the top, the reason the next arrow press
        // jumped to the first item. The restored selection takes the
        // keyboard back to where the user was.
        bool focusFellToTheList = ActiveList() is { } focused
            && ReferenceEquals(Keyboard.FocusedElement, focused);
        bool takeFocus = _focusRowAfterRestore || Vm.FocusListAfterRestore
            || focusFellToTheList || _focusFellOutOfTheList;
        _focusRowAfterRestore = false;
        _focusFellOutOfTheList = false;
        Vm.FocusListAfterRestore = false;

        if (items.Count == 0) {
            return;
        }

        // By delta, like every other selection change here. Clearing first
        // takes SelectedItem to null on the way, and SelectedItem is bound
        // two-way to SelectedEntry — so a refresh that put the selection
        // back exactly where it was still told the preview pane the file
        // had gone and come back, and it reloaded. Writing a rating into a
        // sidecar is enough to cause one of those refreshes, which is how a
        // click on a star ended up re-decoding a RAW.
        switch (ActiveList()) {
            case DataGrid dg:
                SetListSelection(dg, items);
                dg.CurrentItem = items[0];
                dg.ScrollIntoView(items[0]);
                break;
            case ListBox lb:
                SetListSelection(lb, items);
                lb.ScrollIntoView(items[0]);
                break;
        }

        // Only after a rename: the row the user was just editing is the row
        // the keyboard belongs on. A refresh or an undo triggered from
        // somewhere else must leave focus where it is.
        if (takeFocus) {
            FocusRowWhenReady(items[0]);
        }
    }


    /// <summary>
    /// Puts the selection back on rows that were swapped for updated copies.
    ///
    /// <para>
    /// Deliberately thinner than <see cref="RestoreListSelection"/>: no
    /// scrolling, no <c>CurrentItem</c>, no focus. Nothing moved and nothing
    /// finished — a number inside a row the user is looking at changed, and
    /// the only thing to repair is that the list dropped the replaced object
    /// out of its selection on the way.
    /// </para>
    /// </summary>
    private void RefreshListSelection(IReadOnlyList<FileSystemEntry> items) {
        if (items.Count == 0) {
            return;
        }

        switch (ActiveList()) {
            case DataGrid dg:
                SetListSelection(dg, items);
                break;
            case ListBox lb:
                SetListSelection(lb, items);
                break;
        }
    }


    private static void ClearListSelection(ItemsControl host) {
        switch (host) {
            case ListBox lb: lb.UnselectAll(); break;
            case DataGrid dg: dg.UnselectAll(); break;
        }
    }

    private void SetListSelection(ItemsControl host, IEnumerable<FileSystemEntry> items) {
        // Set the selection by delta — clearing+adding everything would
        // collapse and re-expand the control's selection, causing visible
        // flicker on ListBox and unnecessary SelectionChanged churn.
        //
        // The handler stands aside for the whole round rather than running
        // on every add: see List_SelectionChanged. Measured because the
        // shape is suspicious; PerfLog only writes a line once a category
        // is slow, so this one appearing in the log is itself the answer.
        bool changed;
        using (PerfLog.Measure("ui.selection-apply")) {
            _applyingSelection = true;
            try {
                changed = host switch {
                    ListBox lb => ApplyDelta(lb.SelectedItems, items),
                    DataGrid dg => ApplyDelta(dg.SelectedItems, items),
                    _ => false,
                };
            } finally {
                _applyingSelection = false;
            }
        }

        // Only when something moved, so that a delta which turned out to be
        // a no-op stays as silent as it was when the control raised the
        // event itself.
        if (changed) {
            ReportSelection(host);
        }
    }

    /// <returns>True when the selection actually moved.</returns>
    private static bool ApplyDelta(System.Collections.IList currentSelection, IEnumerable<FileSystemEntry> targetItems) {
        bool changed = false;
        var target = new HashSet<FileSystemEntry>(targetItems);
        for (int i = currentSelection.Count - 1; i >= 0; i--) {
            if (currentSelection[i] is FileSystemEntry existing && !target.Contains(existing)) {
                currentSelection.RemoveAt(i);
                changed = true;
            }
        }

        var present = new HashSet<FileSystemEntry>();
        foreach (var o in currentSelection) {
            if (o is FileSystemEntry fe) {
                present.Add(fe);
            }
        }
        foreach (var entry in target) {
            if (!present.Contains(entry)) {
                currentSelection.Add(entry);
                changed = true;
            }
        }

        return changed;
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
        Vm.CaretPath = clicked.FullPath;

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
            if (!FocusRow(row)) {
                host.Focus();
            }

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

        if (!_dragArmed || e.LeftButton != MouseButtonState.Pressed) {
            return;
        }

        var pos = e.GetPosition(this);
        if (Math.Abs(pos.X - _dragOrigin.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(pos.Y - _dragOrigin.Y) < SystemParameters.MinimumVerticalDragDistance) {
            return;
        }

        _dragArmed = false;
        _selection.NotifyDragStarted(); // drag started — keep the full selection

        var paths = Vm.SelectedEntries.Select(en => en.FullPath).ToArray();
        if (paths.Length == 0) {
            return;
        }

        // The payload carries the companions; `paths` — what the user
        // selected — still drives the drag preview, because that is what
        // they think they are dragging.
        DragStartRequested?.Invoke(this, new FileListDragRequest(
            (DependencyObject)sender,
            paths,
            Vm.WithCompanions(Vm.SelectedEntries).ToArray()));
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
    }

    private void List_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e) {
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
    /// Modifiers are left alone deliberately — <c>Ctrl</c> + digits are
    /// window zones and <c>Ctrl</c> + <c>Shift</c> + digits are the view
    /// modes, so a bare digit is the only shape free to mean this.
    /// </para>
    /// </summary>
    private bool TryRateFromKeyboard(Key key) {
        // A digit typed into the rename editor is part of the name. The
        // tunnelling handler sees it before the editor does, so the editor
        // has to be checked for here, as TryEnterList and TryGridStep do.
        if (Vm.ViewMode != ViewMode.Gallery || Keyboard.Modifiers != ModifierKeys.None
            || Vm.RenamingPath is not null) {
            return false;
        }

        int rank = key switch {
            >= Key.D0 and <= Key.D5 => key - Key.D0,
            >= Key.NumPad0 and <= Key.NumPad5 => key - Key.NumPad0,
            _ => -1,
        };
        if (rank < 0 || Vm.SelectedEntries.Count == 0) {
            return false;
        }

        Vm.SetRankForSelection(rank.ToString(System.Globalization.CultureInfo.InvariantCulture));

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
            return FocusRow(selected);
        }

        // Nothing selected, but the focus rectangle is still on the row the
        // user last stood on — a click on empty space leaves exactly that.
        // The press means "back into the list", and the list resumes where
        // the rectangle is rather than at its top edge.
        if (CaretEntry() is { } caret) {
            SetListSelection(list, new[] { caret });

            return FocusRow(caret);
        }

        var entry = key is Key.Down or Key.Right ? entries[0] : entries[^1];
        SetListSelection(list, new[] { entry });

        return FocusRow(entry);
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
        FocusRow(entries[target]);

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
            FocusRow(entries[target]);
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
        // Read before anything is unbound. The controls own multi-selection
        // (SelectedItems is theirs, not the view model's), so a view that is
        // about to be bound has to be told what is selected - SelectedEntry
        // alone would land one row of it.
        var selection = Vm.SelectedEntries;

        foreach (var view in views) {
            if (ShouldDetach(view, active) && _detachedViews.Add(view)) {
                // SelectedItem is bound two-way, and a Selector losing its
                // items clears it - so both halves have to go before the
                // rows do, or the view on its way out writes null over the
                // view model's selection. Two steps because the two
                // families bind it differently: the table has its binding
                // on the element (ClearBinding removes it), the tile views
                // get theirs from the TilePanel style, which a local null
                // overrides without being asked to write through.
                BindingOperations.ClearBinding(view, Selector.SelectedItemProperty);
                view.SelectedItem = null;
                view.ItemsSource = null;
            }
        }

        bool attached = false;
        foreach (var view in views) {
            if (!ShouldDetach(view, active) && _detachedViews.Remove(view)) {
                view.SetBinding(ItemsControl.ItemsSourceProperty, new Binding(nameof(MainViewModel.Entries)));
                view.SetBinding(Selector.SelectedItemProperty,
                    new Binding(nameof(MainViewModel.SelectedEntry)) { Mode = BindingMode.TwoWay });
                attached = true;
            }
        }

        // Every control keeps its own SelectedItems, so the view coming on
        // screen is handed the selection whether or not it was just bound.
        // Before, only the table (the one view that gets rebound) received
        // it: three rows picked there showed as one in the tiles while
        // Ctrl+C still copied three. Applied by delta - unchanged is a
        // no-op. A view that has just been bound is also scrolled to the
        // top, so that one is brought back to the selection.
        if (active is { } host && selection.Count > 0) {
            SetListSelection(host, selection);
            if (attached) {
                if (host is DataGrid grid) {
                    grid.CurrentItem = selection[0];
                }
                ScrollRowIntoView(host, selection[0]);
            }
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


    /// <summary>
    /// The view model asked for the editor on a row it just put the
    /// selection on — a folder that has only this second been created.
    /// Checked against the selection rather than trusted: the restore that
    /// carried the request is what set it, and anything that overtook it
    /// means the row is no longer the one to edit.
    /// </summary>
    private void StartRenameOn(FileSystemEntry entry) {
        if (ReferenceEquals(Vm.SelectedEntry, entry)) {
            StartRename();
        }
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
            if (edited is null || !FocusRow(edited)) {
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
        // listing lands and the selection is restored onto the new name.
        _focusRowAfterRestore = takeFocus;
        Vm.CommitRename(box.Text);
        if (takeFocus) {
            FocusList();
        }
    }
}


/// <summary>What the window needs to run a drag started in the list.</summary>
/// <param name="Source">The control the drag originated from.</param>
/// <param name="Paths">What the user selected — drives the drag preview.</param>
/// <param name="Payload">What actually travels, companions included.</param>
public sealed record FileListDragRequest(DependencyObject Source, string[] Paths, string[] Payload);

/// <summary>Where and in what mode the list wants its context menu.</summary>
public sealed record FileListMenuRequest(FrameworkElement Host, PlacementMode Placement, bool IsBackground);
