using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Wander.App.Controllers;
using Wander.App.Controls;
using Wander.App.DragPreview;
using Wander.App.Resources;
using Wander.App.Util;
using Wander.App.ViewModels;
using Wander.Core.FileSystem;
using Wander.Core.Layout;
using Wander.Core.Navigation;
using Wander.Core.Panels;
using Wander.Core.Shell;
using Wander.Core.Workspace;


namespace Wander.App.Views;

/// <summary>
/// The two folder panels - bookmarks above, the drives below - as the
/// adapter between WPF and the window's model (REDESIGN 4.7). Each panel
/// is a plain list of the lines the model shows (decision P3); a click, a
/// chevron, a key or a letter typed here becomes an event for the model
/// (<see cref="WorkspaceController"/>), and what the model says comes back
/// as the lines' own flags. Nothing here decides what is lit, open or
/// targeted.
///
/// <para>
/// What stays here is what only a view can do: the drag of a line, the
/// drop on one, the editor laid over a line's name, putting the keyboard
/// on the line the model sends it to (<see cref="FocusRow"/>), and not
/// letting the panel slide sideways to the end of a long name. The window
/// answers the rest: the context menu, the keyboard going back to the list,
/// running a drop through the view model.
/// </para>
/// </summary>
public partial class FolderTreesView : UserControl {
    /// <summary>How wide the editor is at least, over a label only as wide as its name.</summary>
    private const double RenameEditorMinWidth = 180;

    private DropTargetController _drops = null!;
    private OutgoingDrag _drag = null!;

    // --- Drag from a line ------------------------------------------------
    private TreeNodeViewModel? _dragLine;
    private Point _dragOrigin;

    // A right-button press on a line arms a drag as well: moved past the
    // threshold it drags the folder, released in place it opens the menu -
    // on a line that has one (an archive's lines drag and have none).
    private TreeNodeViewModel? _menuLine;
    private TreeNodeViewModel? _rightDragLine;
    private Point _rightDragOrigin;
    private bool _rightDragArmed;

    // --- Keyboard ------------------------------------------------------------
    /// <summary>Jump-to-name in a panel (decision B26) - the list's own controller.</summary>
    private readonly TypeAheadController _typeAhead = new();

    /// <summary>A line the model sent the keyboard to before it was drawn - see <see cref="FocusRow"/>.</summary>
    private (Pane Pane, string Path)? _pendingFocus;

    // --- Scrolling ------------------------------------------------------------
    /// <summary>A scroll into view with its sideways part pinned is on its way - see <see cref="Line_RequestBringIntoView"/>.</summary>
    private bool _pinningSideways;

    // --- Rename ---------------------------------------------------------------
    private RenameAdorner? _renameAdorner;
    private AdornerLayer? _renameLayer;
    private TextBox? _renameBox;
    private Pane _renamePane;


    public FolderTreesView() {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }


    private MainViewModel Vm => (MainViewModel)DataContext;


    /// <summary>A folder panel wants its context menu shown.</summary>
    public event EventHandler<FolderMenuRequest>? ContextMenuRequested;

    /// <summary><c>Esc</c> in a panel: the keyboard belongs back in the list.</summary>
    public event EventHandler? FocusListRequested;

    /// <summary>
    /// A drop held by the right mouse button landed on a line: the window
    /// opens the menu that asks what to do with it, where every other menu
    /// is built.
    /// </summary>
    public event EventHandler<DropMenuRequest>? DropMenuRequested;


    /// <summary>True when the bookmarks panel has anything to stand on.</summary>
    public bool HasBookmarks => BookmarksList.HasItems;

    /// <summary>A name is being edited in a panel: the keyboard is the editor's, and the window's shortcuts wait.</summary>
    public bool IsRenaming => _renameBox is not null;


    /// <summary>
    /// Hands over the two window-level collaborators of drag &amp; drop.
    /// Not constructor arguments because the control is built by XAML;
    /// called once, from <c>MainWindow.OnLoaded</c>, where both exist.
    /// </summary>
    public void Connect(DropTargetController drops, OutgoingDrag drag) {
        _drops = drops;
        _drag = drag;
    }


    // --- What the window asks of the panels -----------------------------

    /// <summary>
    /// Puts the keyboard in the bookmarks panel. False when it is put away
    /// or empty - there is nothing to focus there, and the caller moves on
    /// to the next zone.
    /// </summary>
    public bool FocusBookmarks() {
        return Vm.IsBookmarksExpanded && BookmarksList.HasItems && FocusPanel(BookmarksList);
    }

    /// <summary>Puts the keyboard in the drives panel.</summary>
    public bool FocusDrives() {
        return FocusPanel(DrivesList);
    }

    /// <summary>
    /// Which panel an element belongs to, or null when it is neither - the
    /// header, the divider, the strip, or something outside the control.
    /// </summary>
    public NavigationSource? PaneOf(object? source) {
        foreach (var hit in ListVisuals.Ancestors(source)) {
            if (ReferenceEquals(hit, BookmarksList)) {
                return NavigationSource.Bookmark;
            }
            if (ReferenceEquals(hit, DrivesList)) {
                return NavigationSource.Drives;
            }
        }

        return null;
    }

    /// <summary>
    /// Paints the "the keyboard is here" outline on one panel and takes it
    /// off the other. The brush comes from the window: the same one outlines
    /// the file list, and one state in two colours would read as two states.
    /// </summary>
    public void ShowFocusOutline(NavigationSource? pane, Brush active) {
        BookmarksFrame.BorderBrush = pane == NavigationSource.Bookmark ? active : Brushes.Transparent;
        DrivesList.BorderBrush = pane == NavigationSource.Drives ? active : Brushes.Transparent;
    }

    /// <summary>
    /// The tail of <c>Ctrl+1</c> and <c>Ctrl+Shift+E</c>: the keyboard into
    /// <paramref name="pane"/>. Where its cursor lands - the open folder,
    /// opened down to, or the row the panel held (P-22) - is the model's
    /// answer to the keyboard arriving for that reason, which the window
    /// gives; the panel then puts the keyboard on that line.
    /// </summary>
    public void RevealAndFocus(NavigationSource pane) {
        if (pane == NavigationSource.Bookmark) {
            // Put away, the list is Collapsed and cannot take focus; a
            // shortcut that silently did nothing would read as broken.
            Vm.IsBookmarksExpanded = true;
            UpdateLayout();
        }

        FocusPanel(pane == NavigationSource.Bookmark ? BookmarksList : DrivesList);
    }

    /// <summary>
    /// The model's <see cref="Wander.Core.Workspace.FocusRow"/>: the keyboard
    /// onto the line of <paramref name="pane"/> on <paramref name="path"/>,
    /// scrolled into view. A line not drawn yet - a branch on the way to it
    /// still being read, a cursor hidden in a closed branch - gets it once it
    /// is drawn, if the panel still has the keyboard; the panel holds it
    /// meanwhile.
    /// </summary>
    public void FocusRow(Pane pane, string path) {
        var list = ListOf(pane);
        _pendingFocus = null;
        if (!list.IsVisible || _renameBox is not null) {
            return;
        }

        if (Vm.Trees.LineAt(pane, path) is { } line && FocusLine(list, line)) {
            return;
        }

        _pendingFocus = (pane, path);
        if (!list.IsKeyboardFocusWithin) {
            list.Focus();
        }
    }

    /// <summary>
    /// Puts the bookmarks strip back to idle. The drag that lit it up is run
    /// by the window, which owns the plaque and ends the gesture.
    /// </summary>
    public void ClearBookmarkTarget() {
        SetBookmarkDropZoneActive(false);
    }


    // --- The model, drawn -------------------------------------------------------

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e) {
        if (e.OldValue is MainViewModel old) {
            old.PropertyChanged -= OnViewModelChanged;
            old.Trees.Projected -= OnProjected;
        }
        if (e.NewValue is MainViewModel vm) {
            vm.PropertyChanged += OnViewModelChanged;
            vm.Trees.Projected += OnProjected;
            ApplyBookmarksLayout();
        }
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e) {
        // Folded, the bookmarks cannot hold the keyboard: told before WPF
        // drops it on the window, so the model sends it on (K-8).
        if (e.PropertyName == nameof(MainViewModel.IsBookmarksExpanded) && !Vm.IsBookmarksExpanded) {
            Post(new PaneHidden(new[] { WindowZone.Bookmarks }));
        }
        // BookmarksHeight as well as the toggle: the saved height arrives
        // after the window is loaded (MainViewModel.RestorePaneSizes), long
        // after the data context did.
        if (e.PropertyName is nameof(MainViewModel.IsBookmarksExpanded) or nameof(MainViewModel.BookmarksHeight)) {
            ApplyBookmarksLayout();
        }
    }

    /// <summary>
    /// The lines were brought in step with the model. A line the keyboard
    /// was sent to before it was drawn takes it now (<see cref="FocusRow"/>);
    /// a redraw that took the focused line's container away - a panel
    /// refilled in one go - leaves the keyboard on the panel itself, and it
    /// goes back onto the line under the cursor.
    /// </summary>
    private void OnProjected(object? sender, EventArgs e) {
        if (_renameBox is not null) {
            return;
        }

        if (_pendingFocus is { } pending) {
            var list = ListOf(pending.Pane);
            if (!list.IsKeyboardFocusWithin) {
                // The keyboard went elsewhere meanwhile: the move is off.
                _pendingFocus = null;
            } else if (Vm.Trees.LineAt(pending.Pane, pending.Path) is { } line) {
                _pendingFocus = null;
                FocusLine(list, line);
            }

            return;
        }

        foreach (var (list, pane) in new[] { (BookmarksList, Pane.Bookmarks), (DrivesList, Pane.Drives) }) {
            if (ReferenceEquals(Keyboard.FocusedElement, list) && Vm.Trees.CaretLine(pane) is { } caret) {
                FocusLine(list, caret);
            }
        }
    }

    /// <summary>
    /// The keyboard into a panel: onto the line under its cursor when it is
    /// drawn, else onto the list - the model puts the cursor where the
    /// keyboard's arrival says, and the line takes the keyboard once drawn
    /// (<see cref="OnProjected"/>). A line is never focused to put the
    /// cursor on it: that is how WPF's tree selected rows nobody chose.
    /// </summary>
    private bool FocusPanel(FolderPanelList list) {
        if (Vm.Trees.CaretLine(PaneOf(list)) is { } caret && FocusLine(list, caret)) {
            return true;
        }

        return list.Focus();
    }

    /// <summary>
    /// Scrolls a line into view and puts the keyboard on it - now when its
    /// container exists, else once the list has made it.
    /// </summary>
    private static bool FocusLine(FolderPanelList list, TreeNodeViewModel line) {
        list.ScrollIntoView(line);
        if (list.ItemContainerGenerator.ContainerFromItem(line) is FolderPanelItem item) {
            return item.Focus();
        }

        list.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => {
            if (list.IsKeyboardFocusWithin && list.ItemContainerGenerator.ContainerFromItem(line) is FolderPanelItem later) {
                later.Focus();
            }
        });

        return list.Focus();
    }


    // --- Layout ---------------------------------------------------------------------

    /// <summary>
    /// The bookmarks region owns a fixed pixel height that the divider
    /// changes. Collapsed, it falls back to Auto - the header row is all
    /// that is left, and everything below it moves up.
    /// </summary>
    private void ApplyBookmarksLayout() {
        if (Vm.IsBookmarksExpanded) {
            BookmarksRow.MinHeight = 44;
            BookmarksRow.Height = new GridLength(Vm.BookmarksHeight);
        } else {
            BookmarksRow.MinHeight = 0;
            BookmarksRow.Height = GridLength.Auto;
        }
    }

    private void BookmarksSplitter_DragCompleted(object sender, DragCompletedEventArgs e) {
        Vm.BookmarksHeight = BookmarksRow.ActualHeight;
    }


    // --- Mouse ------------------------------------------------------------------------

    /// <summary>
    /// A press on a line: on the chevron it opens or closes the row (Alt:
    /// the children too, or everything below), twice on the line likewise
    /// (decision B9); once, it opens the row's folder - the model's
    /// <see cref="RowClicked"/> - and arms a drag. The line's own press puts
    /// the keyboard on it and selects nothing (<see cref="FolderPanelItem"/>).
    /// </summary>
    private void List_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
        _dragLine = null;
        if (PressBelongsToRenameEditor(e.OriginalSource) || sender is not FolderPanelList list
            || LineAt(e.OriginalSource) is not { } line) {
            return;
        }

        var pane = PaneOf(list);
        if (IsOnChevron(e.OriginalSource)) {
            if (line.HasChevron) {
                Post(new ChevronToggled(pane, line.FullPath, !line.IsExpanded, All: (Keyboard.Modifiers & ModifierKeys.Alt) != 0));
            }
            // A chevron takes no keyboard: it is about opening and closing,
            // nothing else.
            e.Handled = true;

            return;
        }

        // A button inside the line - the bookmark's "..." - is a control, not
        // a grip, and its click is its own.
        if (ListVisuals.IsInsideControl(e.OriginalSource)) {
            return;
        }

        if (e.ClickCount == 2) {
            // The first click has gone into the folder already.
            if (line.HasChevron) {
                Post(new ChevronToggled(pane, line.FullPath, !line.IsExpanded, All: false));
            }
            e.Handled = true;

            return;
        }

        // The panel is a drag source as in Explorer: it is where the folder
        // you want to move *to* is visible, so it is also where the folder
        // you want to move *from* often is.
        _dragLine = CanDrag(line) ? line : null;
        _dragOrigin = e.GetPosition(this);
        Post(new RowClicked(pane, line.FullPath, WorkspaceController.Now));
    }

    private void List_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e) {
        _dragLine = null;
    }

    private void List_PreviewMouseMove(object sender, MouseEventArgs e) {
        if (_rightDragArmed) {
            if (e.RightButton != MouseButtonState.Pressed) {
                _rightDragArmed = false;
            } else if (_rightDragLine is { } grabbed && MovedPastDragThreshold(e.GetPosition(this), _rightDragOrigin)) {
                _rightDragArmed = false;
                _rightDragLine = null;
                // The drag swallows the release; a menu waiting for it would
                // open on the next stray one instead.
                _menuLine = null;
                var grabbedPaths = new[] { grabbed.FullPath };
                _drag.Run((DependencyObject)sender, grabbedPaths, grabbedPaths, rightButton: true);
            }

            return;
        }

        if (_dragLine is not { } line || e.LeftButton != MouseButtonState.Pressed
            || !MovedPastDragThreshold(e.GetPosition(this), _dragOrigin)) {
            return;
        }

        _dragLine = null;
        var paths = new[] { line.FullPath };
        _drag.Run((DependencyObject)sender, paths, paths);
    }

    private void List_PreviewMouseWheel(object sender, MouseWheelEventArgs e) {
        if (ListVisuals.TryShiftScrollHorizontally((DependencyObject)sender, e)) {
            e.Handled = true;
        }
    }

    /// <summary>
    /// A right-button press on a line: nothing moves - not the highlight, not
    /// the cursor, not the list's selection (decision B2). Released in place
    /// it opens the line's menu (the user's own bookmark: the bookmark's;
    /// any other folder: the folder's), moved it drags the folder. Handled,
    /// or the line would take the keyboard.
    /// </summary>
    private void List_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e) {
        if (PressBelongsToRenameEditor(e.OriginalSource)) {
            return;
        }

        // The "..." button is a control, not a grip - same as for the left button.
        var line = ListVisuals.IsInsideControl(e.OriginalSource) ? null : LineAt(e.OriginalSource);
        _menuLine = line is not null && HasMenu(line) ? line : null;
        _rightDragLine = line is not null && CanDrag(line) ? line : null;
        _rightDragArmed = _rightDragLine is not null;
        _rightDragOrigin = e.GetPosition(this);
        e.Handled = _menuLine is not null;
    }

    private void List_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e) {
        _rightDragArmed = false;
        _rightDragLine = null;
        var line = _menuLine;
        // Consumed by this release: a press elsewhere released over the
        // panel must not find this line still waiting for its menu.
        _menuLine = null;
        if (line is null || sender is not FolderPanelList list) {
            return;
        }

        OpenMenu(list, line, PlacementMode.MousePoint);
        e.Handled = true;
    }

    /// <summary>
    /// A line's menu: the bookmark's own for one of the user's bookmarks,
    /// the folder's otherwise - its "Paste" and "Rename" about the line,
    /// framed while the menu is open (decision B4), nothing else moving.
    /// </summary>
    private void OpenMenu(FolderPanelList list, TreeNodeViewModel line, PlacementMode placement) {
        var pane = PaneOf(list);
        if (pane == Pane.Bookmarks && line.IsRemovableBookmark) {
            var host = (FrameworkElement?)list.ItemContainerGenerator.ContainerFromItem(line) ?? list;
            ShowBookmarkMenu(host, line);

            return;
        }

        ContextMenuRequested?.Invoke(this, new FolderMenuRequest(list, pane, line.FullPath, placement));
    }

    /// <summary>
    /// A line scrolled into view - the one taking the keyboard, mostly. Up and
    /// down is what that is for; sideways it pulled the panel right to show
    /// the whole of a long name, and the chevrons and the levels above left
    /// the screen on every click (2026-09-22, P-23). Unless
    /// <c>AppSettings.TreeScrollsSideways</c> wants that, the request goes
    /// out again with its sideways part pinned to what is on screen now.
    /// </summary>
    private void Line_RequestBringIntoView(object sender, RequestBringIntoViewEventArgs e) {
        if (_pinningSideways
            || Vm.Settings.TreeScrollsSideways
            || e.TargetObject is not FrameworkElement target
            || ListVisuals.Ancestors(target).OfType<ScrollContentPresenter>().FirstOrDefault() is not { } viewport) {
            return;
        }

        // The viewport's left edge and width in the line's own coordinates:
        // a rectangle spanning exactly what is on screen sideways asks for no
        // sideways scroll at all, and its height is the line's own.
        var rect = e.TargetRect.IsEmpty ? new Rect(target.RenderSize) : e.TargetRect;
        double left = viewport.TranslatePoint(new Point(0, 0), target).X;
        e.Handled = true;
        _pinningSideways = true;
        try {
            target.BringIntoView(new Rect(left, rect.Y, viewport.ActualWidth, rect.Height));
        } finally {
            _pinningSideways = false;
        }
    }


    // --- Keyboard ------------------------------------------------------------------------

    /// <summary>
    /// The keys of a panel (decision B26). The ones that move the cursor, and
    /// Enter, go to the model; Ctrl+Up/Down move the user's own bookmark;
    /// Delete on a bookmark asks what it is about; Esc hands the keyboard back
    /// to the list; Shift+F10 opens the menu of the line under the cursor
    /// (the Menu key does, on its release). Caught here, ahead of the
    /// window's own bindings, which would act on the file list instead.
    /// </summary>
    private void List_PreviewKeyDown(object sender, KeyEventArgs e) {
        if (sender is not FolderPanelList list || _renameBox is not null) {
            // A name is being edited: the keys are the editor's, which sits
            // inside the panel and sees them after this - RenameBox_PreviewKeyDown.
            return;
        }

        var pane = PaneOf(list);
        var caret = Vm.Trees.CaretLine(pane);
        var modifiers = Keyboard.Modifiers;

        if (e.Key == Key.Enter && modifiers == ModifierKeys.None) {
            Post(new RowActivated(pane, WorkspaceController.Now));
            e.Handled = true;

            return;
        }

        // Ctrl + arrows reorder the user's own bookmarks. Bookmarks panel
        // only: the drives list what the machine has, in the order the
        // machine has it.
        if (e.Key is Key.Up or Key.Down && modifiers == ModifierKeys.Control) {
            if (pane == Pane.Bookmarks && caret is { IsRemovableBookmark: true }) {
                Vm.MoveBookmark(caret.FullPath, e.Key == Key.Up ? -1 : 1);
            }
            e.Handled = true;

            return;
        }

        // Delete on a built-in bookmark (Downloads, Documents...): the row is
        // switched off in the settings and the folder is left alone, with
        // Shift or without. Delete on the user's own bookmark: the bookmark,
        // or its folder? Asked rather than guessed. A folder under a
        // bookmark is an ordinary folder and falls through to the window.
        if (e.Key == Key.Delete && modifiers is ModifierKeys.None or ModifierKeys.Shift
            && pane == Pane.Bookmarks && caret is not null) {
            if (caret.IsBuiltInBookmark && Vm.HideSpecialBookmark(caret)) {
                e.Handled = true;

                return;
            }
            if (caret.IsRemovableBookmark) {
                Vm.DeleteFromBookmark(caret, permanent: modifiers == ModifierKeys.Shift);
                e.Handled = true;

                return;
            }
        }

        if (e.Key == Key.Escape) {
            FocusListRequested?.Invoke(this, EventArgs.Empty);
            e.Handled = true;

            return;
        }

        // The menu of the line under the cursor. Shift+F10 arrives as a
        // system key and opens on the press; the Menu key opens on its
        // release (List_PreviewKeyUp) - an unhandled release turns into
        // WM_CONTEXTMENU and would dismiss a menu opened on the press.
        if (e.Key == Key.Apps || (e.Key == Key.System && e.SystemKey == Key.F10 && modifiers == ModifierKeys.Shift)) {
            if (e.Key != Key.Apps && caret is not null && HasMenu(caret)) {
                OpenMenu(list, caret, PlacementMode.Bottom);
            }
            e.Handled = true;

            return;
        }

        if (modifiers == ModifierKeys.None && PanelKeyOf(e.Key) is { } key) {
            Post(new CaretMoveRequested(pane, key, list.PageSize, WorkspaceController.Now));
            e.Handled = true;
        }
    }

    private void List_PreviewKeyUp(object sender, KeyEventArgs e) {
        if (e.Key != Key.Apps || sender is not FolderPanelList list) {
            return;
        }

        e.Handled = true;
        if (Vm.Trees.CaretLine(PaneOf(list)) is { } caret && HasMenu(caret)) {
            OpenMenu(list, caret, PlacementMode.Bottom);
        }
    }

    /// <summary>
    /// Letters typed into a panel put its cursor on the next line whose name
    /// starts with them (decision B26) - the list's own controller, the same
    /// "a letter again moves on" - and open nothing.
    /// </summary>
    private void List_PreviewTextInput(object sender, TextCompositionEventArgs e) {
        if (_renameBox is not null || sender is not FolderPanelList list
            || Keyboard.Modifiers is ModifierKeys.Control or ModifierKeys.Alt || e.Text == " ") {
            return;
        }

        var pane = PaneOf(list);
        var lines = Vm.Trees.Lines(pane);
        int current = -1;
        for (int i = 0; i < lines.Count; i++) {
            if (lines[i].IsCaret) {
                current = i;
                break;
            }
        }

        int target = _typeAhead.Type(e.Text, lines.Select(l => l.Name).ToList(), current);
        if (target >= 0) {
            Post(new CaretMoved(pane, lines[target].FullPath));
        }
        e.Handled = true;
    }

    private static PanelKey? PanelKeyOf(Key key) {
        return key switch {
            Key.Up => PanelKey.Up,
            Key.Down => PanelKey.Down,
            Key.Left => PanelKey.Left,
            Key.Right => PanelKey.Right,
            Key.Home => PanelKey.Home,
            Key.End => PanelKey.End,
            Key.PageUp => PanelKey.PageUp,
            Key.PageDown => PanelKey.PageDown,
            _ => null,
        };
    }


    // --- Bookmark menu --------------------------------------------------------------------

    /// <summary>
    /// The bookmark line menu - where it sits in the list, where its folder
    /// went, and whether it stays at all. Built here rather than declared in
    /// the line template: a ContextMenu inside a DataTemplate gets its own
    /// name scope and its own visual tree, so a binding that reaches out to
    /// the window by name silently never resolves.
    /// </summary>
    private void ShowBookmarkMenu(FrameworkElement placement, TreeNodeViewModel line) {
        if (!line.IsRemovableBookmark) {
            return;
        }

        var menu = new ContextMenu {
            PlacementTarget = placement,
            Placement = PlacementMode.Bottom,
        };

        // Only for a bookmark whose folder is gone: for a live one there
        // is nothing to relocate, and offering it would invite pointing a
        // working bookmark somewhere else by accident.
        if (line.IsMissing) {
            var locate = new MenuItem { Header = Strings.BookmarksLocate };
            locate.Click += (_, _) => Vm.RelocateBookmark(line.FullPath);
            menu.Items.Add(locate);
            menu.Items.Add(new Separator());
        }

        var up = new MenuItem { Header = Strings.BookmarksMoveUp, InputGestureText = "Ctrl+↑" };
        up.Click += (_, _) => MoveBookmark(line.FullPath, -1);
        menu.Items.Add(up);

        var down = new MenuItem { Header = Strings.BookmarksMoveDown, InputGestureText = "Ctrl+↓" };
        down.Click += (_, _) => MoveBookmark(line.FullPath, +1);
        menu.Items.Add(down);

        menu.Items.Add(new Separator());

        var remove = new MenuItem { Header = Strings.BookmarksRemove };
        remove.Click += (_, _) => Vm.RemoveBookmarkCommand.Execute(line);
        menu.Items.Add(remove);

        menu.IsOpen = true;
    }

    /// <summary>Moves a bookmark from its menu, and the panel's cursor onto it - the line the menu was about.</summary>
    private void MoveBookmark(string path, int delta) {
        if (Vm.MoveBookmark(path, delta)) {
            Post(new CaretMoved(Pane.Bookmarks, path));
        }
    }

    /// <summary>The "..." button on a bookmark line.</summary>
    private void BookmarkRowMenu_Click(object sender, RoutedEventArgs e) {
        if (sender is FrameworkElement { DataContext: TreeNodeViewModel line } button) {
            ShowBookmarkMenu(button, line);
            e.Handled = true;
        }
    }


    // --- Rename ---------------------------------------------------------------------------
    // F2 on a line, or "Rename" in its menu: the folder is renamed where its
    // name is read, with the editor the list lays over a row (RenameAdorner)
    // and the operation behind the list's rename - guard, log, undo, the
    // wait for a holder (MainViewModel.RenameFolderAsync). The row stays
    // where it is, open if it was open: the model follows the new path
    // (Relocated) instead of reading the level as if a folder had gone.

    /// <summary>
    /// Whether <see cref="StartRename"/> has a line to open its editor on
    /// for <paramref name="path"/> in <paramref name="pane"/> - what greys
    /// the menu's "Rename". No disk here: it is asked on every requery of
    /// the commands.
    /// </summary>
    public bool CanRename(Pane pane, string path) {
        return RenameLine(pane, path) is not null;
    }

    /// <summary>
    /// Opens the editor on the line of <paramref name="pane"/> standing on
    /// <paramref name="path"/>. False when there is none on screen, or it is
    /// not one that can be renamed - a drive, a shell folder, an archive or a
    /// folder inside one, a bookmark whose folder is gone, a built-in bookmark
    /// (its label is Windows's name for the folder, not the folder's).
    /// </summary>
    public bool StartRename(Pane pane, string path) {
        var line = RenameLine(pane, path);
        if (line is null || Archives.Contains(line.FullPath)) {
            return false;
        }

        var list = ListOf(pane);
        list.ScrollIntoView(line);
        list.UpdateLayout();
        if (list.ItemContainerGenerator.ContainerFromItem(line) is not FrameworkElement container) {
            return false;
        }

        // The editor sits over the name label and needs the label's adorner
        // layer - the panel's scroll viewport's, so it moves and clips with
        // the line.
        var label = ListVisuals.FindDescendant<TextBlock>(container, "NameLabel");
        if (label is null || AdornerLayer.GetAdornerLayer(label) is not { } layer) {
            return false;
        }

        HideRenameEditor();
        var box = new TextBox {
            // The line, for the commit and for the Escape that puts the
            // keyboard back on it.
            DataContext = line,
            Text = line.Name,
            FontSize = label.FontSize,
            Padding = new Thickness(0),
            // Wrapped so the whole name stays in view, as in the list; the
            // minimum width is the editor's own, because a label here is
            // exactly as wide as the name it shows.
            TextWrapping = TextWrapping.Wrap,
        };
        box.PreviewKeyDown += RenameBox_PreviewKeyDown;
        box.PreviewTextInput += RenameBox_PreviewTextInput;
        box.LostKeyboardFocus += RenameBox_LostKeyboardFocus;
        _renamePane = pane;
        _renameBox = box;
        _renameLayer = layer;
        _renameAdorner = new RenameAdorner(label, box, RenameEditorMinWidth);
        layer.Add(_renameAdorner);
        layer.UpdateLayout();
        Post(new EditRequested(pane, line.FullPath));
        box.Focus();
        box.SelectAll();

        return true;
    }

    /// <summary>The line a rename of <paramref name="path"/> in <paramref name="pane"/> would be about - the cursor's when it stands there - if it can be renamed.</summary>
    private TreeNodeViewModel? RenameLine(Pane pane, string path) {
        return Vm.Trees.LineAt(pane, path) is { Row.IsRenamable: true } line ? line : null;
    }

    private void RenameBox_PreviewKeyDown(object sender, KeyEventArgs e) {
        // Enter and Escape are the panel's (open the folder, back to the
        // list) and the window's (open, clear the selection) before they
        // are the editor's, so both are settled here. Up and Down would
        // walk the cursor out from under the editor.
        switch (e.Key) {
            case Key.Enter:
                CommitInlineRename((TextBox)sender, takeFocus: true);
                e.Handled = true;
                break;
            case Key.Escape:
                CancelInlineRename();
                e.Handled = true;
                break;
            case Key.Up or Key.Down:
                e.Handled = true;
                break;
        }
    }

    private void RenameBox_PreviewTextInput(object sender, TextCompositionEventArgs e) {
        // The characters Windows will not take in a name are refused as
        // they are typed, as in the list - a rename rejected after the
        // fact would only lose what was typed.
        if (e.Text.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) {
            e.Handled = true;
            Vm.Status = Strings.InvalidFileNameChars + "\\ / : * ? \" < > |";
        }
    }

    private void RenameBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) {
        // Clicking away commits, as in Explorer and in the list. Escape has
        // already taken the editor down by the time focus leaves, so a
        // cancelled edit falls out of CommitInlineRename on its own.
        CommitInlineRename((TextBox)sender, takeFocus: false);
    }

    /// <summary>
    /// A press in a panel while a name is being edited. Inside the editor
    /// it is the editor's - a caret move, its own menu - and true comes
    /// back so the caller leaves it alone. Anywhere else it applies the
    /// edit, the way Explorer does, and the press goes on to whatever it
    /// was for.
    /// </summary>
    private bool PressBelongsToRenameEditor(object originalSource) {
        if (_renameBox is not { } box) {
            return false;
        }
        if (ListVisuals.IsInsideTextBox(originalSource)) {
            return true;
        }

        CommitInlineRename(box, takeFocus: false);

        return false;
    }

    /// <summary>
    /// Applies the edited name. <paramref name="takeFocus"/> separates the
    /// two ways an edit ends: Enter means the user is still in the panel
    /// and the keyboard belongs on the line; a click elsewhere means they
    /// have moved on, and the keyboard is theirs to place.
    /// </summary>
    [SuppressMessage("ReSharper", "AsyncVoidMethod",
        Justification = "An event handler's tail. RenameFolderAsync reports its own failures; the rest runs on the dispatcher, where an exception lands in App.HookCrashLogging.")]
    private async void CommitInlineRename(TextBox box, bool takeFocus) {
        // Taking the editor down is itself a loss of focus, and that
        // arrives here too: only the editor still up is committed.
        if (!ReferenceEquals(box, _renameBox)) {
            return;
        }

        var line = (TreeNodeViewModel)box.DataContext;
        var pane = _renamePane;
        var list = ListOf(pane);
        string path = line.FullPath;
        string newName = box.Text;
        HideRenameEditor();
        Post(new EditEnded(pane));
        if (takeFocus) {
            FocusPanel(list);
        }
        if (string.IsNullOrWhiteSpace(newName) || newName == line.Name) {
            return;
        }

        string? renamed = await Vm.RenameFolderAsync(path, newName);
        if (renamed is null || !takeFocus) {
            return;
        }

        // The line is under its new name in place (the model followed it);
        // the cursor goes onto it - it is what the next operation is about.
        Post(new CaretMoved(pane, renamed));
        FocusPanel(list);
    }

    private void CancelInlineRename() {
        var list = ListOf(_renamePane);
        HideRenameEditor();
        Post(new EditEnded(_renamePane));
        FocusPanel(list);
    }

    /// <summary>Takes the editor down. Idempotent, and quiet about focus: the caller decides where the keyboard goes.</summary>
    private void HideRenameEditor() {
        _renameBox = null;
        if (_renameAdorner is not { } adorner) {
            return;
        }

        _renameAdorner = null;
        _renameLayer?.Remove(adorner);
        _renameLayer = null;
    }


    // --- Drop target ------------------------------------------------------------------------
    //
    // Where a drop would land, whether it is allowed and what it would do is
    // DropTargetController's answer - one instance for every surface, shared
    // with the file list. What is left here is the XAML wiring and running
    // the plan through the view model.

    private void OnDragOver(object sender, DragEventArgs e) {
        _drops.DragOver(e);
    }

    private void OnDrop(object sender, DragEventArgs e) {
        _drops.Execute(
            e,
            plan => Vm.HandleDrop(plan.Paths, plan.Target, plan.Effect),
            plan => DropMenuRequested?.Invoke(this, new DropMenuRequest((FrameworkElement)sender, plan)));
    }


    // --- Bookmarks panel drop -------------------------------------------
    //
    // Two-mode dispatch by hit location:
    //  - Drop ON an existing bookmark line that is a real filesystem folder
    //    -> copy/move into that folder. We forward the event to the standard
    //    OnDragOver/OnDrop pair, which re-resolves the target via the line's
    //    DataContext and shares all the same self-drop / effect-choice /
    //    highlight machinery the drives panel uses.
    //  - Drop on the header, empty area, or a shell-namespace bookmark
    //    (Recycle Bin can't accept drops) -> register the dragged folders
    //    as new bookmarks.
    // We decide the mode BEFORE delegating; OnDragOver's own ResolveDropTarget
    // would otherwise fall back to Vm.CurrentPath for empty area, which would
    // wrongly turn "add bookmark" into "copy into current folder".

    private void BookmarksPanel_DragOver(object sender, DragEventArgs e) {
        // Through the controller's reading of the payload, not the data
        // object's own: a drag out of an archive carries no file list, and
        // this check used to turn the whole strip into "no drop" for it
        // before the bookmark folder underneath got a say.
        if (DropTargetController.PayloadPaths(e.Data) is null) {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        if (DropTargetController.IsOverDroppableBookmarkFolder(e)) {
            // Defer to the standard handler - same effect, same highlight,
            // same self-drop protection as the drives panel.
            OnDragOver(sender, e);
            return;
        }

        bool acceptable = CanAcceptBookmarkDrop(e);
        e.Effects = acceptable ? DragDropEffects.Link : DragDropEffects.None;
        // Clear any leftover highlight from a previous in-folder hover so
        // empty-area drops don't look like they're targeting something -
        // and the hover itself, which would otherwise go on opening it.
        _drops.SetHighlight(null);
        _drops.EndHover();
        SetBookmarkDropZoneActive(acceptable);
        e.Handled = true;
    }

    private void BookmarksPanel_Drop(object sender, DragEventArgs e) {
        if (DropTargetController.IsOverDroppableBookmarkFolder(e)) {
            OnDrop(sender, e);
            return;
        }

        try {
            AddDroppedBookmarks(e);
        } finally {
            _drops.Clear();
        }
    }

    // --- Bookmark "+" strip -------------------------------------------
    //
    // Sits at the bottom of the bookmarks region, above the divider.
    // A drop adds what was dropped. The parent BookmarksPanel still accepts
    // drops on its empty area, so users who learned that gesture are not
    // forced to aim at the strip.

    private void BookmarkDropZone_DragEnter(object sender, DragEventArgs e) {
        if (!CanAcceptBookmarkDrop(e)) {
            return;
        }
        SetBookmarkDropZoneActive(true);
    }

    private void BookmarkDropZone_DragOver(object sender, DragEventArgs e) {
        _drops.EndHover();
        if (!CanAcceptBookmarkDrop(e)) {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }
        // Link cursor (arrow with curved-arrow overlay) reads as "make a
        // reference here" - closest stock cursor to "bookmark".
        e.Effects = DragDropEffects.Link;
        e.Handled = true;
    }

    private void BookmarkDropZone_DragLeave(object sender, DragEventArgs e) {
        SetBookmarkDropZoneActive(false);
    }

    private void BookmarkDropZone_Drop(object sender, DragEventArgs e) {
        try {
            AddDroppedBookmarks(e);
        } finally {
            SetBookmarkDropZoneActive(false);
        }
    }

    /// <summary>
    /// Bookmarks every folder in the drop, and says so when there were
    /// none - the strip and the empty area below the bookmarks answer a
    /// drop the same way, they only differ in what they clean up
    /// afterwards.
    /// </summary>
    private void AddDroppedBookmarks(DragEventArgs e) {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) {
            return;
        }

        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths) {
            return;
        }

        int added = 0;
        foreach (string p in paths) {
            if (Directory.Exists(p)) {
                Vm.Bookmarks.Add(p);
                added++;
            }
        }
        if (added == 0) {
            Vm.Status = Strings.BookmarksFoldersOnly;
        }
        e.Handled = true;
    }


    /// <summary>
    /// A drag is worth reacting to when it carries at least one folder that
    /// is not bookmarked already - dropping a folder that is in the list
    /// would do nothing, so the strip should not promise otherwise.
    /// </summary>
    private bool CanAcceptBookmarkDrop(DragEventArgs e) {
        if (DropTargetController.PayloadPaths(e.Data) is not { } paths) {
            return false;
        }

        return paths.Any(p => Directory.Exists(p) && !Vm.Bookmarks.Contains(p));
    }

    /// <summary>
    /// Lights the drop strip while a drag it can accept is over the
    /// bookmarks. This is the strip's only reactive state - it is not a
    /// button, so an idle mouse passing over it changes nothing.
    /// </summary>
    private void SetBookmarkDropZoneActive(bool active) {
        _drops.IsBookmarkTarget = active;
        BookmarkDropZone.Background = active ? Palette.DropZoneActiveFill : Palette.DropZoneFill;
        BookmarkDropZoneGlyph.Foreground = active ? Palette.DropZoneActiveGlyph : Palette.DropZoneGlyph;
        BookmarkDropZoneGlyph.FontWeight = active ? FontWeights.Bold : FontWeights.Normal;
        _drag.UpdateForCurrentTarget();
    }


    // --- Helpers ----------------------------------------------------------------------------

    private void Post(WorkspaceEvent e) {
        Vm.Workspace.Post(e);
    }

    private Pane PaneOf(FolderPanelList list) {
        return ReferenceEquals(list, BookmarksList) ? Pane.Bookmarks : Pane.Drives;
    }

    private FolderPanelList ListOf(Pane pane) {
        return pane == Pane.Bookmarks ? BookmarksList : DrivesList;
    }

    /// <summary>The line a hit belongs to.</summary>
    private static TreeNodeViewModel? LineAt(object originalSource) {
        foreach (var hit in ListVisuals.Ancestors(originalSource)) {
            if (hit is FrameworkElement { DataContext: TreeNodeViewModel line }) {
                return line;
            }
        }

        return null;
    }

    /// <summary>The press landed on the line's chevron.</summary>
    private static bool IsOnChevron(object originalSource) {
        foreach (var hit in ListVisuals.Ancestors(originalSource)) {
            if (hit is FrameworkElement { Name: "Chevron" }) {
                return true;
            }
            if (hit is FolderPanelItem) {
                return false;
            }
        }

        return false;
    }

    /// <summary>
    /// A line with a real folder behind it: what is given a folder's menu. An
    /// archive, a folder inside one and a shell location answer no - the
    /// container is read-only by decision.
    /// </summary>
    private static bool HasMenu(TreeNodeViewModel line) {
        return CanDrag(line) && !Archives.Contains(line.FullPath);
    }

    /// <summary>
    /// A line that can be dragged away: a folder, an archive - a file like
    /// any other - and a folder inside an archive, whose drop unpacks it the
    /// way a row dragged out of the list does (2026-09-23). A shell location
    /// is nothing a drop could take.
    /// </summary>
    private static bool CanDrag(TreeNodeViewModel line) {
        return !string.IsNullOrEmpty(line.FullPath)
            && line.Kind is not PanelRowKind.Shell
            && !line.FullPath.StartsWith("shell:", StringComparison.OrdinalIgnoreCase);
    }

    private static bool MovedPastDragThreshold(Point pos, Point origin) {
        return Math.Abs(pos.X - origin.X) >= SystemParameters.MinimumHorizontalDragDistance
            || Math.Abs(pos.Y - origin.Y) >= SystemParameters.MinimumVerticalDragDistance;
    }
}


/// <summary>
/// A folder panel asking for the context menu of one folder of
/// <paramref name="Pane"/>, placed at <paramref name="Host"/>.
/// </summary>
public sealed record FolderMenuRequest(FrameworkElement Host, Pane Pane, string Folder, PlacementMode Placement);


/// <summary>
/// A folder panel asking for the menu of a right-button drop, placed at
/// <paramref name="Host"/>.
/// </summary>
public sealed record DropMenuRequest(FrameworkElement Host, DropPlan Plan);
