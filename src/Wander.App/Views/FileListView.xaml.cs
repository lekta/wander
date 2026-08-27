using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Wander.App.Controls;
using Wander.App.Resources;
using Wander.App.Util;
using Wander.App.ViewModels;
using Wander.Core.FileSystem;
using Wander.Core.Layout;

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


    public FileListView() {
        InitializeComponent();
        _rubberBand = new RubberBandController(
            () => Vm.Entries,
            SetListSelection,
            ClearListSelection);
        DataContextChanged += OnDataContextChanged;
    }


    /// <summary>
    /// The user started dragging the current selection out of the list. The
    /// window answers by running the drag loop with its preview window.
    /// </summary>
    public event EventHandler<FileListDragRequest>? DragStartRequested;

    /// <summary>The list wants its context menu shown.</summary>
    public event EventHandler<FileListMenuRequest>? ContextMenuRequested;


    private MainViewModel Vm => (MainViewModel)DataContext;


    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e) {
        if (e.OldValue is MainViewModel old) {
            old.SelectionRestoreRequested -= RestoreListSelection;
            old.PropertyChanged -= OnViewModelChanged;
            old.Settings.PropertyChanged -= OnSettingsChanged;
            old.Entries.CollectionChanged -= OnEntriesChanged;
        }
        if (e.NewValue is MainViewModel vm) {
            vm.SelectionRestoreRequested += RestoreListSelection;
            vm.PropertyChanged += OnViewModelChanged;
            vm.Settings.PropertyChanged += OnSettingsChanged;
            vm.Entries.CollectionChanged += OnEntriesChanged;
            ShowSortIndicator();
            ApplyIconColumnWidth();
            ShowRatingColumn();
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
        }
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
                ApplyIconColumnWidth();
                break;
        }
    }

    /// <summary>
    /// Keeps the icon column as wide as the icon in it. Assigned rather than
    /// bound: a <see cref="System.Windows.Controls.DataGridColumn"/> is not
    /// in the visual tree and has no DataContext, so a binding on its Width
    /// resolves to nothing at all — silently, which is the worst way for a
    /// binding to fail.
    /// </summary>
    private void ApplyIconColumnWidth() {
        if (DataContext is MainViewModel vm) {
            IconColumn.Width = new DataGridLength(vm.Settings.DetailsIconColumnWidth);
        }
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
    // Just FocusList; everything else here is this control's own business.

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
    /// Hands the keyboard back to the list — to the selected row when there
    /// is one. Focusing the container itself is not enough: a list with
    /// focus but no focused item leaves the arrow keys resuming from
    /// wherever the cursor was last, which is usually the top.
    /// </summary>
    public void FocusList() {
        if (Vm.SelectedEntry is { } entry && FocusRow(entry)) {
            return;
        }

        ActiveList()?.Focus();
    }


    /// <summary>
    /// Puts the keyboard on one row. Returns false when the row has no
    /// realised container (virtualised away), so the caller can fall back
    /// to focusing the list itself.
    /// </summary>
    private bool FocusRow(FileSystemEntry entry) {
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


    /// <summary>Clears the selection in whichever container is on screen.</summary>
    public void ClearSelection() {
        SelectionController.ClearActive(ActiveList());
    }


    // --- Selection ------------------------------------------------------

    private void List_SelectionChanged(object sender, SelectionChangedEventArgs e) {
        // Containers can raise this while the control is still being wired
        // up, before the view model has been inherited down the tree.
        if (DataContext is not MainViewModel) {
            return;
        }

        var entries = new List<FileSystemEntry>();
        switch (sender) {
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
        bool takeFocus = _focusRowAfterRestore || Vm.FocusListAfterRestore || focusFellToTheList;
        _focusRowAfterRestore = false;
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
            FocusRow(items[0]);
        }
    }


    private static void ClearListSelection(ItemsControl host) {
        switch (host) {
            case ListBox lb: lb.UnselectAll(); break;
            case DataGrid dg: dg.UnselectAll(); break;
        }
    }

    private static void SetListSelection(ItemsControl host, IEnumerable<FileSystemEntry> items) {
        // Set the selection by delta — clearing+adding everything would
        // collapse and re-expand the control's selection, causing visible
        // flicker on ListBox and unnecessary SelectionChanged churn.
        switch (host) {
            case ListBox lb: ApplyDelta(lb.SelectedItems, items); break;
            case DataGrid dg: ApplyDelta(dg.SelectedItems, items); break;
        }
    }

    private static void ApplyDelta(System.Collections.IList currentSelection, IEnumerable<FileSystemEntry> targetItems) {
        var target = new HashSet<FileSystemEntry>(targetItems);
        for (int i = currentSelection.Count - 1; i >= 0; i--) {
            if (currentSelection[i] is FileSystemEntry existing && !target.Contains(existing)) {
                currentSelection.RemoveAt(i);
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
            }
        }
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
                _rubberBand.Start(host, e, Vm.SelectedEntries);
                e.Handled = true;
            }

            return;
        }
        _dragArmed = true;

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
        if (host is null || host.IsKeyboardFocusWithin) {
            return;
        }

        if (row is null || !FocusRow(row)) {
            host.Focus();
        }
    }


    private void List_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e) {
        if (_rubberBand.IsActive) {
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
            ContextMenuRequested?.Invoke(this, new FileListMenuRequest(host, PlacementMode.Center, _contextIsBackground));
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
        if (!IsAtEdge(index, step.Value, panel.Columns, entries.Count)) {
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

        return Vm.SelectedEntry is { } selected ? entries.IndexOf(selected) : -1;
    }


    /// <summary>
    /// The row a Shift-extension grows from. WPF keeps its own anchor
    /// privately, so this reads it back off the selection instead: what
    /// Shift builds is a run, and the anchor is the end the caret is not
    /// sitting on.
    /// </summary>
    private static int AnchorIndex(ListBox list, IList<FileSystemEntry> entries, int caret) {
        var selected = new HashSet<FileSystemEntry>(list.SelectedItems.OfType<FileSystemEntry>());
        int first = -1;
        int last = -1;
        for (int i = 0; i < entries.Count; i++) {
            if (selected.Contains(entries[i])) {
                if (first < 0) {
                    first = i;
                }
                last = i;
            }
        }

        if (first < 0) {
            return caret;
        }

        return caret == last ? first : last;
    }


    private static IEnumerable<FileSystemEntry> Range(IList<FileSystemEntry> entries, int a, int b) {
        var run = new List<FileSystemEntry>();
        for (int i = Math.Min(a, b); i <= Math.Max(a, b); i++) {
            run.Add(entries[i]);
        }

        return run;
    }


    /// <summary>
    /// Is this the press WPF cannot answer? Anything in the middle of the
    /// grid has a neighbour in that direction and is none of our business.
    /// </summary>
    private static bool IsAtEdge(int index, GridStep step, int columns, int count) {
        if (index < 0) {
            return false;
        }

        return step switch {
            GridStep.Left => index % columns == 0,
            GridStep.Right => index % columns == columns - 1 || index == count - 1,
            GridStep.Up => index < columns,
            GridStep.Down => index + columns >= count,
            _ => false,
        };
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


    // --- Rename ----------------------------------------------------------
    // In-place editing (A3) is the normal path: the row template carries a
    // collapsed TextBox and MainViewModel.RenamingPath makes it visible.
    // PromptDialog stays as the fallback for the case the inline editor
    // cannot be reached — a row that virtualisation has not realised.

    public void StartRename() {
        if (Vm.SelectedEntry is not FileSystemEntry entry) {
            return;
        }

        if (TryStartInlineRename(entry)) {
            return;
        }

        string? input = PromptDialog.Show(Strings.RenameTitle, Strings.RenamePrompt, entry.Name, filenameMode: true);
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

        Vm.BeginRename(entry);
        if (Vm.RenamingPath is null) {
            return false;
        }

        container.UpdateLayout();
        var box = ListVisuals.FindDescendant<TextBox>(container);
        if (box is null) {
            Vm.CancelRename();

            return false;
        }

        box.Text = entry.Name;
        box.Focus();
        SelectNameWithoutExtension(box, entry);

        return true;
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
