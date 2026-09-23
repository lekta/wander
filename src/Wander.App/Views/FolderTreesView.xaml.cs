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
using Wander.App.Controls;
using Wander.App.DragPreview;
using Wander.App.Resources;
using Wander.App.Util;
using Wander.App.ViewModels;
using Wander.Core;
using Wander.Core.Logging;
using Wander.Core.Navigation;
using Wander.Core.Shell;


namespace Wander.App.Views;

/// <summary>
/// The two folder panels — bookmarks above, the drives tree below — and
/// every gesture that belongs to a tree row: the click that opens a folder,
/// the chevron that only expands, the drag of a node, the right click that
/// targets a folder without going there, <c>Shift</c> + wheel, and the
/// keyboard walk that does not list every folder it passes.
///
/// <para>
/// The control reads its data from the inherited <see cref="MainViewModel"/>
/// and reports upwards what it cannot finish on its own: the context menu
/// (assembled in the window from Core's model plus the shell's), the
/// operation target (the file list has to give up its selection for it) and
/// the keyboard going back to the list. Drag and drop is done with the
/// window's two collaborators, handed over once in <see cref="Connect"/> —
/// the plaque and the drop rules have to be the same objects here and in
/// the list, or two surfaces would answer the same drag differently.
/// </para>
/// </summary>
public partial class FolderTreesView : UserControl {
    private DropTargetController _drops = null!;
    private OutgoingDrag _drag = null!;

    // --- Tree expand/collapse gesture state -----------------------------
    private bool _userClickedExpander;
    private bool _altWasHeld;
    /// <summary>The highlight is being put back where a closing branch took it from - see <see cref="OnTreeSelectionChanged"/>.</summary>
    private bool _restoringSelection;

    // --- Tree as drag source / operation target -------------------------
    /// <summary>
    /// Set for the duration of a click on a tree row: only a click opens the
    /// folder it lands on. See <see cref="OnTreeSelectionChanged"/>.
    /// </summary>
    private bool _treeClickNavigates;

    // --- Tree keyboard navigation, coalesced ----------------------------
    // With TreeKeyboardNavigates on, a held arrow key selects several rows
    // a second and each selection is a full navigation — listing, layout,
    // thumbnails. Run every one and the window falls seconds behind the
    // key (measured: ui.stall 3.6–4.9 s in the session log). A lone press
    // still navigates immediately; only presses arriving on the heels of a
    // navigation are held until the cursor settles, so a burst costs one
    // listing — the folder the user stopped on.
    /// <summary>A press this soon after a navigation is part of a burst.</summary>
    private const int TreeNavBurstMs = 250;
    /// <summary>How long the tree cursor has to rest before a burst navigates.</summary>
    private const int TreeNavSettleMs = 90;

    private DispatcherTimer? _treeNavDebounce;
    private (TreeView Tree, string Path, NavigationSource Source)? _pendingTreeNav;
    private long _lastTreeNavAtMs;

    private TreeNodeViewModel? _treeDragNode;
    private Point _treeDragOrigin;
    private TreeNodeViewModel? _treeMenuNode;

    // A right-button press on a drives-tree row arms a drag as well: moved
    // past the threshold it drags the folder, released in place it opens
    // the folder's menu.
    private Point _treeRightDragOrigin;
    private bool _treeRightDragArmed;

    /// <summary>
    /// The row last made the operation target, with its panel. A right
    /// click targets a row without selecting it, and this is where the
    /// menu's "Rename" then finds the row it is about.
    /// </summary>
    private (TreeView Tree, TreeNodeViewModel Node)? _targetRow;

    // --- Tree scrolling -------------------------------------------------
    /// <summary>A scroll into view with its sideways part pinned is on its way - see <see cref="TreeViewItem_RequestBringIntoView"/>.</summary>
    private bool _pinningSideways;

    // --- Rename ---------------------------------------------------------
    /// <summary>How wide the editor is at least, over a label only as wide as its name.</summary>
    private const double RenameEditorMinWidth = 180;

    private RenameAdorner? _renameAdorner;
    private AdornerLayer? _renameLayer;
    private TextBox? _renameBox;
    private TreeView? _renameTree;



    public FolderTreesView() {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Tree.IsKeyboardFocusWithinChanged += Tree_IsKeyboardFocusWithinChanged;
        BookmarksTree.IsKeyboardFocusWithinChanged += Tree_IsKeyboardFocusWithinChanged;
    }


    private MainViewModel Vm => (MainViewModel)DataContext;


    /// <summary>A folder panel wants its context menu shown.</summary>
    public event EventHandler<FolderMenuRequest>? ContextMenuRequested;

    /// <summary>
    /// The folder the next operation is about is now this one — the row
    /// under the keyboard cursor, or the one that was right-clicked. The
    /// window answers by clearing the list's selection, so that exactly one
    /// highlighted set is on screen.
    /// </summary>
    public event EventHandler<string>? FolderTargeted;

    /// <summary><c>Esc</c> in a panel: the keyboard belongs back in the list.</summary>
    public event EventHandler? FocusListRequested;

    /// <summary>
    /// A drop held by the right mouse button landed on a row: the window
    /// opens the menu that asks what to do with it, where every other menu
    /// is built.
    /// </summary>
    public event EventHandler<DropMenuRequest>? DropMenuRequested;


    /// <summary>
    /// Hands over the two window-level collaborators of drag &amp; drop.
    /// Not constructor arguments because the control is built by XAML;
    /// called once, from <c>MainWindow.OnLoaded</c>, where both exist.
    /// </summary>
    public void Connect(DropTargetController drops, OutgoingDrag drag) {
        _drops = drops;
        _drag = drag;
    }
    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e) {
        if (e.OldValue is MainViewModel old) {
            old.PropertyChanged -= OnViewModelChanged;
        }
        if (e.NewValue is MainViewModel vm) {
            vm.PropertyChanged += OnViewModelChanged;
            ApplyBookmarksLayout();
        }
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e) {
        // BookmarksHeight as well as the toggle: the saved height arrives
        // after the window is loaded (MainViewModel.RestorePaneSizes), long
        // after the data context did.
        if (e.PropertyName is nameof(MainViewModel.IsBookmarksExpanded) or nameof(MainViewModel.BookmarksHeight)) {
            ApplyBookmarksLayout();
        }
    }



    /// <summary>
    /// The bookmarks region owns a fixed pixel height that the divider
    /// changes. Collapsed, it falls back to Auto — the header row is all
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


    // --- What the window asks of the panels -----------------------------

    /// <summary>True when the bookmarks panel has anything to stand on.</summary>
    public bool HasBookmarks => BookmarksTree.HasItems;


    /// <summary>
    /// Puts the keyboard in the bookmarks panel. False when it is put away
    /// or empty — there is nothing to focus there, and the caller moves on
    /// to the next zone.
    /// </summary>
    public bool FocusBookmarks() {
        return Vm.IsBookmarksExpanded && BookmarksTree.HasItems && FocusTree(BookmarksTree);
    }


    /// <summary>Puts the keyboard in the drives tree.</summary>
    public bool FocusDrives() {
        return FocusTree(Tree);
    }


    /// <summary>
    /// Which panel an element belongs to, or null when it is neither — the
    /// header, the divider, the strip, or something outside the control.
    /// </summary>
    public NavigationSource? PaneOf(object? source) {
        foreach (var hit in ListVisuals.Ancestors(source)) {
            if (ReferenceEquals(hit, BookmarksTree)) {
                return NavigationSource.Bookmark;
            }
            if (ReferenceEquals(hit, Tree)) {
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
        Tree.BorderBrush = pane == NavigationSource.Drives ? active : Brushes.Transparent;
    }


    /// <summary>
    /// Makes the folder under the cursor in <paramref name="pane"/> the one
    /// the next operation is about — what arriving in a panel with the
    /// keyboard means.
    /// </summary>
    public void TargetSelected(NavigationSource pane) {
        TargetTreeNode(pane == NavigationSource.Bookmark ? BookmarksTree : Tree);
    }


    /// <summary>
    /// Opens <paramref name="pane"/> down to the current folder and puts the
    /// keyboard on its row — the tail of <c>Ctrl+1</c> and
    /// <c>Ctrl+Shift+E</c>.
    ///
    /// <para>
    /// Except the drives tree while the folder on screen came from the
    /// bookmarks: its row is still lit where the user left it, and the
    /// keyboard goes back there (decided 2026-09-22) - <c>Ctrl+1</c> back
    /// and forth is going between two places, not asking twice where the
    /// one open folder is. Only a drives tree with nothing lit is opened
    /// down to that folder. With the arrow keys opening folders, arriving
    /// on the row opens it, as an arrow key landing there would.
    /// </para>
    /// </summary>
    public void RevealAndFocus(NavigationSource pane) {
        if (pane == NavigationSource.Bookmark) {
            // Put away, the tree is Collapsed and cannot take focus; a
            // shortcut that silently did nothing would read as broken.
            Vm.IsBookmarksExpanded = true;
            UpdateLayout();
        }

        var tree = pane == NavigationSource.Bookmark ? BookmarksTree : Tree;
        if (pane == NavigationSource.Drives
            && Vm.Nav.CurrentSource == NavigationSource.Bookmark
            && Tree.SelectedItem is TreeNodeViewModel { FullPath.Length: > 0 } left) {
            FocusTree(Tree);
            if (Vm.Settings.TreeKeyboardNavigates) {
                NavigateFromTree(left.FullPath, NavigationSource.Drives);
            }

            return;
        }

        Vm.RevealCurrentIn(pane);
        tree.UpdateLayout();
        FocusTree(tree);
    }


    /// <summary>
    /// Puts the bookmarks strip back to idle. The drag that lit it up is run
    /// by the window, which owns the plaque and ends the gesture.
    /// </summary>
    public void ClearBookmarkTarget() {
        SetBookmarkDropZoneActive(false);
    }


    /// <summary>
    /// The keyboard left a panel and nobody took it: WPF answers the
    /// removal of a focused row by handing focus to the window, and that
    /// is what happens to the row of a folder deleted from the panel, or
    /// of one whose level was rebuilt. The panel takes the keyboard back
    /// onto the row now highlighted - the parent the listing fell back to
    /// - so the next arrow key and the next Delete are about something;
    /// arriving there targets the row (MainWindow.OnZoneFocusChanged).
    /// Deferred, because the highlight moves in the same breath as the
    /// removal; only onto a highlighted row that is on screen, because
    /// focusing the first row instead would select it.
    /// </summary>
    private void Tree_IsKeyboardFocusWithinChanged(object sender, DependencyPropertyChangedEventArgs e) {
        if (e.NewValue is true || sender is not TreeView tree || Keyboard.FocusedElement is not Window) {
            return;
        }

        Dispatcher.BeginInvoke(DispatcherPriority.Input, () => {
            if (Keyboard.FocusedElement is Window && tree.IsVisible
                && tree.SelectedItem is { } selected && ContainerFor(tree, selected) is { } row) {
                row.Focus();
            }
        });
    }


    /// <summary>
    /// The keyboard belongs on a row, not on the tree: a TreeView with focus
    /// and no focused item resumes the arrow keys from wherever the cursor
    /// happened to be. Same reasoning as FileListView.FocusList.
    ///
    /// <para>
    /// A highlighted row inside a closed branch (a chevron closed over it)
    /// is opened up to first: the keyboard arriving in the panel means
    /// looking at the cursor, and focusing some other row instead would
    /// select that row - a TreeViewItem selects itself on focus.
    /// </para>
    /// </summary>
    private bool FocusTree(TreeView tree) {
        if (tree.SelectedItem is TreeNodeViewModel selected) {
            if (ContainerFor(tree, selected) is null && !string.IsNullOrEmpty(selected.FullPath)) {
                Vm.Trees.RevealIn(
                    ReferenceEquals(tree, BookmarksTree) ? NavigationSource.Bookmark : NavigationSource.Drives,
                    selected.FullPath);
                tree.UpdateLayout();
            }
            if (ContainerFor(tree, selected) is { } container) {
                return container.Focus();
            }
        }

        if (tree.Items.Count > 0 && tree.ItemContainerGenerator.ContainerFromIndex(0) is TreeViewItem first) {
            return first.Focus();
        }

        return false;
    }


    /// <summary>
    /// The TreeViewItem showing one node. Only expanded branches are walked
    /// — a collapsed one has no realised containers, and nothing inside it
    /// can be what we are looking for.
    /// </summary>
    private static TreeViewItem? ContainerFor(ItemsControl root, object item) {
        for (int i = 0; i < root.Items.Count; i++) {
            if (root.ItemContainerGenerator.ContainerFromIndex(i) is not TreeViewItem container) {
                continue;
            }
            if (ReferenceEquals(root.Items[i], item)) {
                return container;
            }
            if (container.IsExpanded) {
                container.UpdateLayout();
                if (ContainerFor(container, item) is { } deeper) {
                    return deeper;
                }
            }
        }

        return null;
    }


    // --- Tree: selection -----------------------------------------------

    private void Tree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e) {
        OnTreeSelectionChanged(sender, e.OldValue, e.NewValue, NavigationSource.Drives);
    }


    /// <summary>
    /// A click on a row goes into the folder; the arrow keys only move the
    /// cursor, and Enter is what opens (see <see cref="Tree_PreviewKeyDown"/>).
    ///
    /// <para>
    /// Explorer navigates on every selection change, so arrowing past ten
    /// folders on the way to the eleventh lists all ten — each one a
    /// directory read, a thumbnail pass and a lost list position. The rule
    /// here is the one the mouse already follows: moving is free, opening is
    /// deliberate. <c>AppSettings.TreeKeyboardNavigates</c> puts Explorer's
    /// habit back for anyone who has it.
    /// </para>
    /// </summary>
    private void OnTreeSelectionChanged(object sender, object? previous, object? item, NavigationSource source) {
        if (_restoringSelection || item is not TreeNodeViewModel node || string.IsNullOrEmpty(node.FullPath)) {
            return;
        }

        // The controller moving the highlight after a navigation is an
        // echo, not a click. Inside an archive the row it picks is the
        // folder holding the archive, and treated as a click that echo
        // navigated straight back out - the archive never got listed.
        if (Vm.Trees.IsSyncingSelection) {
            Log.Detail($"tree: highlight {node.FullPath} (sync)");

            return;
        }

        if (_treeClickNavigates) {
            NavigateFromTree(node.FullPath, source);

            return;
        }

        // A branch just closed over the highlighted row, and the TreeView
        // moved the highlight onto the branch (TreeView.HandleSelectionAnd
        // Collapsed) - before anyone was asked. A chevron is about opening
        // and closing, nothing else: the highlight goes back onto the row
        // it was on, hidden now as in Explorer, and nothing is navigated
        // or targeted. Undone rather than prevented, because it cannot be
        // prevented (2026-09-22).
        if (!node.IsExpanded && previous is TreeNodeViewModel hidden && IsInside(hidden.FullPath, node.FullPath)) {
            Log.Detail($"tree: highlight {node.FullPath} (branch closed over {hidden.FullPath}; put back)");
            _restoringSelection = true;
            try {
                hidden.IsSelected = true;
            } finally {
                _restoringSelection = false;
            }

            return;
        }

        if (Vm.Settings.TreeKeyboardNavigates) {
            // A lone press navigates now; a press inside a burst waits for
            // the cursor to settle. See the fields above for why.
            if (Environment.TickCount64 - _lastTreeNavAtMs >= TreeNavBurstMs) {
                NavigateFromTree(node.FullPath, source);
            } else if (sender is TreeView panel) {
                _pendingTreeNav = (panel, node.FullPath, source);
                ArmTreeNavDebounce();
            }

            return;
        }

        // Moved with the keyboard: no navigation, but the folder the cursor
        // is on becomes what the file operations act on.
        if (sender is TreeView { IsKeyboardFocusWithin: true } tree) {
            TargetTreeNode(tree);

            return;
        }

        // Control line (REDESIGN.md): a highlight that moved with no click,
        // no keyboard in the panel and no sync behind it - the TreeView's
        // own doing, and the kind of move nobody could see in the log.
        Log.Detail($"tree: highlight {node.FullPath} (no gesture; was {(previous as TreeNodeViewModel)?.FullPath ?? "none"})");
    }


    /// <summary>
    /// With the keyboard in a folder panel, the folder under the cursor is
    /// what `Delete`, `Ctrl+C` and `Alt+Enter` act on — the same targeting
    /// the right mouse button has always done in the tree.
    ///
    /// <para>
    /// The file list gives up its selection for it, deliberately: exactly
    /// one highlighted set on screen is what tells the user which of the two
    /// the next `Delete` is about. Coming back with `Ctrl+2` leaves the
    /// caret where it was, only unselected.
    /// </para>
    /// </summary>
    private void TargetTreeNode(TreeView tree) {
        if (tree.SelectedItem is not TreeNodeViewModel node || string.IsNullOrEmpty(node.FullPath)) {
            return;
        }

        _targetRow = (tree, node);
        FolderTargeted?.Invoke(this, node.FullPath);
    }


    /// <summary>
    /// The one door out of the folder panels into navigation. Every
    /// immediate navigation goes through here so it also cancels whatever a
    /// coalesced burst still holds — a click must not be followed 90 ms
    /// later by a stale keyboard destination.
    /// </summary>
    private void NavigateFromTree(string path, NavigationSource source) {
        _pendingTreeNav = null;
        _treeNavDebounce?.Stop();

        // Already there — nothing to navigate. This is not a corner case:
        // after every navigation ExpandTo re-selects the row in the tree,
        // and with TreeKeyboardNavigates on that echo arrives here as a
        // navigation to the current folder. The controller would no-op it,
        // but the ArrivalIntent planted first would dangle and be consumed
        // by the next listing — which is how Backspace stopped highlighting
        // the folder it came out of: the echo's "select the folder in the
        // tree" overwrote the arrival's "select the row we left through".
        if (string.Equals(path, Vm.CurrentPath, StringComparison.OrdinalIgnoreCase)) {
            return;
        }

        _lastTreeNavAtMs = Environment.TickCount64;
        Vm.NavigateAndSelectFolder(path, source);
    }


    private void ArmTreeNavDebounce() {
        if (_treeNavDebounce is null) {
            _treeNavDebounce = new DispatcherTimer(DispatcherPriority.Input) {
                Interval = TimeSpan.FromMilliseconds(TreeNavSettleMs),
            };
            _treeNavDebounce.Tick += (_, _) => {
                _treeNavDebounce!.Stop();
                if (_pendingTreeNav is not { } pending) {
                    return;
                }

                _pendingTreeNav = null;
                // The keyboard has left the panel — the user moved on
                // mid-burst, and this destination is not theirs any more.
                // (A pending path that is already current is dropped by
                // NavigateFromTree itself.)
                if (!pending.Tree.IsKeyboardFocusWithin) {
                    return;
                }

                NavigateFromTree(pending.Path, pending.Source);
            };
        }

        _treeNavDebounce.Stop();
        _treeNavDebounce.Start();
    }


    /// <summary>
    /// Enter opens the folder under the cursor, Esc hands the keyboard back
    /// to the list, Delete on a bookmark asks what it is about. All three
    /// have to be caught here: the window's own bindings would otherwise
    /// open whatever the <em>file list</em> has selected, clear its
    /// selection or delete without asking, none of which is what the user
    /// is pointing at.
    /// </summary>
    private void Tree_PreviewKeyDown(object sender, KeyEventArgs e) {
        if (sender is not TreeView tree) {
            return;
        }

        // A name is being edited: the keys are the editor's, which sits
        // inside the panel and sees them after this - RenameBox_PreviewKeyDown.
        if (_renameBox is not null) {
            return;
        }

        if (e.Key == Key.Enter) {
            if (tree.SelectedItem is TreeNodeViewModel node && !string.IsNullOrEmpty(node.FullPath)) {
                NavigateFromTree(
                    node.FullPath,
                    ReferenceEquals(tree, BookmarksTree) ? NavigationSource.Bookmark : NavigationSource.Drives);
            }
            e.Handled = true;

            return;
        }

        // Ctrl + arrows reorder the user's own bookmarks. Bookmarks panel
        // only: the drives tree lists what the machine has, in the order
        // the machine has it, and there is nothing there to reorder.
        if (e.Key is Key.Up or Key.Down
            && Keyboard.Modifiers == ModifierKeys.Control
            && ReferenceEquals(tree, BookmarksTree)
            && tree.SelectedItem is TreeNodeViewModel { IsRemovableBookmark: true } bookmark) {

            MoveBookmark(bookmark, e.Key == Key.Up ? -1 : 1);
            e.Handled = true;

            return;
        }

        // Delete on a built-in bookmark (Downloads, Documents...): the row is
        // switched off in the settings and the folder is left alone, with
        // Shift or without - see MainViewModel.HideSpecialBookmark. A folder
        // under such a row is an ordinary folder and falls through.
        if (e.Key == Key.Delete
            && Keyboard.Modifiers is ModifierKeys.None or ModifierKeys.Shift
            && ReferenceEquals(tree, BookmarksTree)
            && tree.SelectedItem is TreeNodeViewModel { IsRemovableBookmark: false } builtIn
            && Vm.HideSpecialBookmark(builtIn)) {

            e.Handled = true;

            return;
        }

        // Delete on a bookmark row: the bookmark, or its folder? Asked
        // rather than guessed - see MainViewModel.DeleteFromBookmark. Caught
        // here, ahead of the window's own Delete binding.
        if (e.Key == Key.Delete
            && Keyboard.Modifiers is ModifierKeys.None or ModifierKeys.Shift
            && ReferenceEquals(tree, BookmarksTree)
            && tree.SelectedItem is TreeNodeViewModel { IsRemovableBookmark: true } marked) {

            Vm.DeleteFromBookmark(marked, permanent: Keyboard.Modifiers == ModifierKeys.Shift);
            e.Handled = true;

            return;
        }

        if (e.Key == Key.Escape) {
            FocusListRequested?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }
    }

    /// <summary>
    /// The bookmark row menu — where it sits in the list, where its folder
    /// went, and whether it stays at all. Built here rather than declared
    /// in the row template: a ContextMenu inside a DataTemplate gets its
    /// own name scope and its own visual tree, so a binding that reaches
    /// out to the window by name silently never resolves, and the menu
    /// item does nothing when clicked.
    /// </summary>
    private void ShowBookmarkMenu(FrameworkElement placement, TreeNodeViewModel node) {
        if (!node.IsRemovableBookmark) {
            return;
        }

        var menu = new ContextMenu {
            PlacementTarget = placement,
            Placement = PlacementMode.Bottom,
        };

        // Only for a bookmark whose folder is gone: for a live one there
        // is nothing to relocate, and offering it would invite pointing a
        // working bookmark somewhere else by accident.
        if (node.IsMissing) {
            var locate = new MenuItem { Header = Strings.BookmarksLocate };
            locate.Click += (_, _) => Vm.RelocateBookmark(node.FullPath);
            menu.Items.Add(locate);
            menu.Items.Add(new Separator());
        }

        var up = new MenuItem { Header = Strings.BookmarksMoveUp, InputGestureText = "Ctrl+↑" };
        up.Click += (_, _) => MoveBookmark(node, -1);
        menu.Items.Add(up);

        var down = new MenuItem { Header = Strings.BookmarksMoveDown, InputGestureText = "Ctrl+↓" };
        down.Click += (_, _) => MoveBookmark(node, +1);
        menu.Items.Add(down);

        menu.Items.Add(new Separator());

        var remove = new MenuItem { Header = Strings.BookmarksRemove };
        remove.Click += (_, _) => Vm.RemoveBookmarkCommand.Execute(node);
        menu.Items.Add(remove);

        menu.IsOpen = true;
    }


    /// <summary>
    /// Moves a bookmark and follows it with the keyboard. The panel is
    /// rebuilt from scratch around the new order, so without putting the
    /// focus back on the fresh row a second Ctrl+Up would have nothing
    /// under it to move.
    /// </summary>
    private void MoveBookmark(TreeNodeViewModel node, int delta) {
        if (Vm.MoveBookmark(node, delta) is not { } moved) {
            return;
        }

        BookmarksTree.UpdateLayout();
        ContainerFor(BookmarksTree, moved)?.Focus();
    }


    /// <summary>The "…" button on a bookmark row.</summary>
    private void BookmarkRowMenu_Click(object sender, RoutedEventArgs e) {
        if (sender is FrameworkElement { DataContext: TreeNodeViewModel node } button) {
            ShowBookmarkMenu(button, node);
            e.Handled = true;
        }
    }

    /// <summary>
    /// A right-button press on a bookmark row arms a drag of its folder, as
    /// in the drives tree (<see cref="Tree_PreviewMouseRightButtonDown"/>);
    /// released in place, it opens a menu below. Handled on a row, as in
    /// the drives tree: left to the row, the press would focus it, and a
    /// TreeViewItem selects itself on focus - a right click is about the
    /// row's menu, not about moving the highlight (2026-09-22).
    /// </summary>
    private void BookmarksTree_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e) {
        if (PressBelongsToRenameEditor(e.OriginalSource)) {
            return;
        }

        // The "..." button is a control, not a grip - same as for the left button.
        _treeMenuNode = ListVisuals.IsInsideControl(e.OriginalSource) ? null : NodeAt(e.OriginalSource);
        _treeRightDragArmed = _treeMenuNode is not null;
        _treeRightDragOrigin = e.GetPosition(this);
        e.Handled = _treeMenuNode is not null;
    }

    /// <summary>
    /// The same menu, from the right mouse button - on the user's own
    /// bookmark rows. Every other row with a folder behind it - a built-in
    /// one, a folder under a bookmark - gets the folder's menu, targeted
    /// the way the drives tree does it: "Paste" and "Rename" there are
    /// about the row. Until 2026-09-22 those rows had no menu at all.
    /// </summary>
    private void BookmarksTree_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e) {
        _treeRightDragArmed = false;
        // Armed here, not in the drives tree: a release over that one must
        // not find this row waiting.
        _treeMenuNode = null;
        if (NodeAt(e.OriginalSource) is not { } node || sender is not FrameworkElement host) {
            return;
        }

        if (node.IsRemovableBookmark) {
            ShowBookmarkMenu(host, node);
        } else {
            _targetRow = (BookmarksTree, node);
            FolderTargeted?.Invoke(this, node.FullPath);
            ContextMenuRequested?.Invoke(this, new FolderMenuRequest(host, node.FullPath));
        }
        e.Handled = true;
    }

    private void Bookmarks_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e) {
        OnTreeSelectionChanged(sender, e.OldValue, e.NewValue, NavigationSource.Bookmark);
    }


    // --- Tree: custom expand/collapse semantics ------------------------

    private void Tree_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
        // Cleared first: every path out of this handler that is not "the
        // user pressed a row" must leave the selection change silent.
        _treeClickNavigates = false;

        if (PressBelongsToRenameEditor(e.OriginalSource)) {
            _treeDragNode = null;

            return;
        }

        if (HitTestExpander(e.OriginalSource as DependencyObject)) {
            _userClickedExpander = true;
            _altWasHeld = (Keyboard.Modifiers & ModifierKeys.Alt) != 0;
            _treeDragNode = null;

            return;
        }

        _userClickedExpander = false;
        _altWasHeld = false;

        // A button inside the row — the bookmark's "…" — is a control, not
        // a grip: pressing it must not arm a drag of the folder.
        if (ListVisuals.IsInsideControl(e.OriginalSource)) {
            _treeDragNode = null;

            return;
        }

        // Arm a drag from the row under the cursor. The tree is a drag
        // source in Explorer and users reach for it — the panel is where
        // the folder you want to move *to* is visible, so it is also where
        // the folder you want to move *from* often is.
        _treeDragNode = NodeAt(e.OriginalSource);
        _treeDragOrigin = e.GetPosition(this);
        // The row selects itself on this same press, so the selection change
        // that follows is this click's.
        _treeClickNavigates = _treeDragNode is not null;

        // Unless the row is already the selected one. Arrow keys move the
        // tree cursor without navigating (see OnTreeSelectionChanged), so
        // clicking the folder the cursor is standing on produces no
        // selection change to ride on — and the click, which always means
        // "open this", has to do the navigating itself.
        if (_treeDragNode is { } clicked
            && sender is TreeView tree
            && ReferenceEquals(tree.SelectedItem, clicked)) {

            NavigateFromTree(
                clicked.FullPath,
                ReferenceEquals(tree, BookmarksTree) ? NavigationSource.Bookmark : NavigationSource.Drives);
        }
    }

    private void Tree_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e) {
        _treeDragNode = null;
        _treeClickNavigates = false;
    }

    private void Tree_PreviewMouseMove(object sender, MouseEventArgs e) {
        if (_treeRightDragArmed) {
            if (e.RightButton != MouseButtonState.Pressed) {
                _treeRightDragArmed = false;
            } else if (_treeMenuNode is { } grabbed && MovedPastDragThreshold(e.GetPosition(this), _treeRightDragOrigin)) {
                _treeRightDragArmed = false;
                // The drag swallows the release; a menu waiting for it
                // would open on the next stray one instead.
                _treeMenuNode = null;
                var grabbedPaths = new[] { grabbed.FullPath };
                _drag.Run((DependencyObject)sender, grabbedPaths, grabbedPaths, rightButton: true);
            }

            return;
        }

        if (_treeDragNode is not { } node || e.LeftButton != MouseButtonState.Pressed) {
            return;
        }

        if (!MovedPastDragThreshold(e.GetPosition(this), _treeDragOrigin)) {
            return;
        }

        _treeDragNode = null;
        // The drag swallows the button release, so the click flag would
        // otherwise stay armed and the next arrow key in the tree would
        // navigate.
        _treeClickNavigates = false;
        var paths = new[] { node.FullPath };
        _drag.Run((DependencyObject)sender, paths, paths);
    }

    private void Tree_PreviewMouseWheel(object sender, MouseWheelEventArgs e) {
        if (ListVisuals.TryShiftScrollHorizontally((DependencyObject)sender, e)) {
            e.Handled = true;
        }
    }


    /// <summary>
    /// A row asking to be scrolled into view. WPF asks it for the row taking
    /// the keyboard - a click, an arrow key, a row selected while the panel
    /// has focus. Up and down is what that is for; sideways it pulled the
    /// panel right to show the whole of a long name, and the chevrons and
    /// the levels above left the screen on every click (2026-09-22). Unless
    /// <c>AppSettings.TreeScrollsSideways</c> wants that, the request goes
    /// out again with its sideways part pinned to what is on screen now.
    /// </summary>
    private void TreeViewItem_RequestBringIntoView(object sender, RequestBringIntoViewEventArgs e) {
        if (_pinningSideways
            || Vm.Settings.TreeScrollsSideways
            || e.TargetObject is not FrameworkElement target
            || ListVisuals.Ancestors(target).OfType<ScrollContentPresenter>().FirstOrDefault() is not { } viewport) {
            return;
        }

        // The viewport's left edge and width in the row's own coordinates:
        // a rectangle spanning exactly what is on screen sideways asks for
        // no sideways scroll at all, and its height is the row's own.
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

    private static bool MovedPastDragThreshold(Point pos, Point origin) {
        return Math.Abs(pos.X - origin.X) >= SystemParameters.MinimumHorizontalDragDistance
            || Math.Abs(pos.Y - origin.Y) >= SystemParameters.MinimumVerticalDragDistance;
    }


    /// <summary>
    /// Right-clicking a folder in the drives tree targets that folder —
    /// without navigating to it, the way Explorer behaves. The tree is a
    /// second selection source: the clicked node becomes the selection the
    /// menu (and Ctrl+C, Delete, Alt+Enter after it) operates on, so the
    /// file list's own selection is dropped first to keep exactly one
    /// highlighted set on screen.
    /// </summary>
    private void Tree_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e) {
        if (PressBelongsToRenameEditor(e.OriginalSource)) {
            return;
        }

        _treeMenuNode = NodeAt(e.OriginalSource);
        if (_treeMenuNode is null) {
            return;
        }

        _targetRow = ((TreeView)sender, _treeMenuNode);
        FolderTargeted?.Invoke(this, _treeMenuNode.FullPath);
        _treeRightDragArmed = true;
        _treeRightDragOrigin = e.GetPosition(this);
        e.Handled = true;
    }

    private void Tree_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e) {
        _treeRightDragArmed = false;
        var node = _treeMenuNode;
        // Consumed by this release: a press elsewhere released over the
        // tree must not find this row still waiting for its menu.
        _treeMenuNode = null;
        if (node is null || sender is not FrameworkElement host) {
            return;
        }

        // The folder is the clicked one rather than the open one: "Paste"
        // and "New folder" in this menu mean "into what I right-clicked".
        ContextMenuRequested?.Invoke(this, new FolderMenuRequest(host, node.FullPath));
        e.Handled = true;
    }


    /// <summary>
    /// The tree node a hit belongs to, if it has a real path. An archive
    /// row, or a folder inside one, answers null like a shell sentinel
    /// does: nothing here can be dragged out of it, dropped into it or
    /// done to it from a menu - the container is read-only by decision.
    /// </summary>
    private static TreeNodeViewModel? NodeAt(object originalSource) {
        foreach (var hit in ListVisuals.Ancestors(originalSource)) {
            if (hit is FrameworkElement fe && fe.DataContext is TreeNodeViewModel node) {
                return string.IsNullOrEmpty(node.FullPath)
                    || node.FullPath.StartsWith("shell:", StringComparison.OrdinalIgnoreCase)
                    || Archives.Contains(node.FullPath)
                    ? null
                    : node;
            }
        }

        return null;
    }

    private void TreeViewItem_Expanded(object sender, RoutedEventArgs e) {
        bool isUserClick = _userClickedExpander;
        bool altHeld = _altWasHeld;
        _userClickedExpander = false;

        if (!isUserClick || !altHeld) {
            return;
        }

        if (e.OriginalSource is TreeViewItem tvi && tvi.DataContext is TreeNodeViewModel node) {
            node.ExpandChildren();
        }
    }

    private void TreeViewItem_Collapsed(object sender, RoutedEventArgs e) {
        bool isUserClick = _userClickedExpander;
        bool altHeld = _altWasHeld;
        _userClickedExpander = false;

        if (!isUserClick || !altHeld) {
            return;
        }

        if (e.OriginalSource is TreeViewItem tvi && tvi.DataContext is TreeNodeViewModel node) {
            node.CollapseDescendants();
        }
    }


    private static bool HitTestExpander(DependencyObject? source) {
        foreach (var hit in ListVisuals.Ancestors(source)) {
            if (hit is ToggleButton) {
                return true;
            }
        }

        return false;
    }


    // --- Rename ---------------------------------------------------------
    // F2 on a row, or "Rename" in its menu: the folder is renamed where its
    // name is read, with the editor the list lays over a row (RenameAdorner)
    // and the operation behind the list's rename - guard, log, undo, the
    // wait for a holder (MainViewModel.RenameFolderAsync). The row stays
    // where it is, open if it was open: the panels follow the new path
    // instead of re-reading the level (TreeNodeViewModel.Follow).

    /// <summary>A name is being edited in a panel: the keyboard is the editor's, and the window's shortcuts wait.</summary>
    public bool IsRenaming => _renameBox is not null;


    /// <summary>
    /// Whether <see cref="StartRename"/> has a row to open its editor on
    /// for <paramref name="path"/> - what greys the menu's "Rename". No
    /// disk here: it is asked on every requery of the commands.
    /// </summary>
    public bool CanRename(string path) {
        return RenameTarget(path) is not null;
    }


    /// <summary>
    /// Opens the editor on the row standing on <paramref name="path"/>:
    /// the row under the keyboard cursor, or the one a right click made
    /// the target. False when no such row is on screen, or the row is not
    /// one that can be renamed - a drive, a shell folder, an archive or a
    /// folder inside one, a bookmark whose folder is gone, a built-in
    /// bookmark (its label is Windows's name for the folder, not the
    /// folder's).
    /// </summary>
    public bool StartRename(string path) {
        var target = RenameTarget(path);
        if (target is null || Archives.Contains(target.Value.Node.FullPath)) {
            return false;
        }

        var (tree, node) = target.Value;
        tree.UpdateLayout();
        if (ContainerFor(tree, node) is not { } container) {
            return false;
        }

        // The editor sits over the name label and needs the label's
        // adorner layer - the panel's scroll viewport's, so it moves and
        // clips with the row.
        var label = ListVisuals.FindDescendant<TextBlock>(container, "NameLabel");
        if (label is null || AdornerLayer.GetAdornerLayer(label) is not { } layer) {
            return false;
        }

        HideRenameEditor();
        var box = new TextBox {
            // The row, for the commit and for the Escape that puts the
            // keyboard back on it.
            DataContext = node,
            Text = node.Name,
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
        _renameTree = tree;
        _renameBox = box;
        _renameLayer = layer;
        _renameAdorner = new RenameAdorner(label, box, RenameEditorMinWidth);
        layer.Add(_renameAdorner);
        layer.UpdateLayout();
        box.Focus();
        box.SelectAll();

        return true;
    }


    /// <summary>
    /// The row on <paramref name="path"/> a rename would be about, with
    /// its panel: the cursor of the panel that has the keyboard, then the
    /// other panel's, then the row last made the target. Null when none
    /// of them stands on the path, or the one that does cannot be renamed.
    /// </summary>
    private (TreeView Tree, TreeNodeViewModel Node)? RenameTarget(string path) {
        var first = BookmarksTree.IsKeyboardFocusWithin ? BookmarksTree : Tree;
        var second = ReferenceEquals(first, Tree) ? BookmarksTree : Tree;
        var candidates = new (TreeView? Tree, TreeNodeViewModel? Node)[] {
            (first, first.SelectedItem as TreeNodeViewModel),
            (second, second.SelectedItem as TreeNodeViewModel),
            (_targetRow?.Tree, _targetRow?.Node),
        };
        foreach (var (tree, node) in candidates) {
            if (tree is not null && node is not null && IsSamePath(node.FullPath, path)) {
                return IsRenamable(tree, node) ? (tree, node) : null;
            }
        }

        return null;
    }

    /// <summary>
    /// Not a drive (in either panel), a shell folder, a bookmark whose
    /// folder is gone, or a built-in bookmark row ("Downloads",
    /// "Documents"): that label is the name Windows gives the folder, not
    /// the folder's own, and the row is a setting. A folder under it is an
    /// ordinary folder. Archives are refused by <see cref="StartRename"/>,
    /// which may ask the disk.
    /// </summary>
    private bool IsRenamable(TreeView tree, TreeNodeViewModel node) {
        if (string.IsNullOrEmpty(node.FullPath)
            || node.FullPath.StartsWith("shell:", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrEmpty(Path.GetDirectoryName(node.FullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)))
            || node.IsMissing) {
            return false;
        }

        return node.IsRemovableBookmark
            || !ReferenceEquals(tree, BookmarksTree)
            || !Vm.Bookmarks.Items.Contains(node);
    }

    private static bool IsSamePath(string a, string b) {
        return string.Equals(
            a.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            b.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>True when <paramref name="path"/> is strictly inside <paramref name="folder"/>.</summary>
    private static bool IsInside(string path, string folder) {
        string root = folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return root.Length > 0
            && path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
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
    /// and the keyboard belongs on the row; a click elsewhere means they
    /// have moved on, and the highlight is theirs to place.
    /// </summary>
    [SuppressMessage("ReSharper", "AsyncVoidMethod",
        Justification = "An event handler's tail. RenameFolderAsync reports its own failures; the rest runs on the dispatcher, where an exception lands in App.HookCrashLogging.")]
    private async void CommitInlineRename(TextBox box, bool takeFocus) {
        // Taking the editor down is itself a loss of focus, and that
        // arrives here too: only the editor still up is committed.
        if (!ReferenceEquals(box, _renameBox) || _renameTree is not { } tree) {
            return;
        }

        var node = (TreeNodeViewModel)box.DataContext;
        string newName = box.Text;
        HideRenameEditor();
        if (takeFocus) {
            FocusTree(tree);
        }
        if (string.IsNullOrWhiteSpace(newName) || newName == node.Name) {
            return;
        }

        string? renamed = await Vm.RenameFolderAsync(node.FullPath, newName);
        if (renamed is null || !takeFocus) {
            return;
        }

        // The row is back under its new name - the same one in the drives
        // tree, a rebuilt one in the bookmarks panel - and is what the next
        // operation is about.
        var panel = ReferenceEquals(tree, BookmarksTree) ? NavigationSource.Bookmark : NavigationSource.Drives;
        Vm.Trees.RevealIn(panel, renamed);
        tree.UpdateLayout();
        TargetTreeNode(tree);
        FocusTree(tree);
    }

    private void CancelInlineRename() {
        var tree = _renameTree;
        HideRenameEditor();
        if (tree is not null) {
            FocusTree(tree);
        }
    }

    /// <summary>Takes the editor down. Idempotent, and quiet about focus: the caller decides where the keyboard goes.</summary>
    private void HideRenameEditor() {
        _renameBox = null;
        _renameTree = null;
        if (_renameAdorner is not { } adorner) {
            return;
        }

        _renameAdorner = null;
        _renameLayer?.Remove(adorner);
        _renameLayer = null;
    }


    // --- Drop target ----------------------------------------------------
    //
    // Where a drop would land, whether it is allowed and what it would do is
    // DropTargetController's answer — one instance for every surface, shared
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
    //  • Drop ON an existing bookmark item that is a real filesystem folder
    //    → copy/move into that folder. We forward the event to the standard
    //    OnDragOver/OnDrop pair, which re-resolves the target via the tree's
    //    DataContext and shares all the same self-drop / effect-choice /
    //    highlight machinery the drives tree uses.
    //  • Drop on the header, empty tree area, or a shell-namespace bookmark
    //    (Recycle Bin can't accept drops) → register the dragged folders
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
            // Defer to the standard handler — same effect, same highlight,
            // same self-drop protection as the drives tree.
            OnDragOver(sender, e);
            return;
        }

        bool acceptable = CanAcceptBookmarkDrop(e);
        e.Effects = acceptable ? DragDropEffects.Link : DragDropEffects.None;
        // Clear any leftover highlight from a previous in-folder hover so
        // empty-area drops don't look like they're targeting something.
        _drops.SetHighlight(null);
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
    // Click adds the folder that is open; a drop adds what was dropped.
    // The parent BookmarksPanel still accepts drops on its empty area, so
    // users who learned that gesture are not forced to aim at the strip.

    private void BookmarkDropZone_DragEnter(object sender, DragEventArgs e) {
        if (!CanAcceptBookmarkDrop(e)) {
            return;
        }
        SetBookmarkDropZoneActive(true);
    }

    private void BookmarkDropZone_DragOver(object sender, DragEventArgs e) {
        if (!CanAcceptBookmarkDrop(e)) {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }
        // Link cursor (arrow with curved-arrow overlay) reads as "make a
        // reference here" — closest stock cursor to "bookmark".
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
    /// none — the strip and the empty area below the bookmarks answer a
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
    /// is not bookmarked already — dropping a folder that is in the list
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
    /// bookmarks. This is the strip's only reactive state — it is not a
    /// button, so an idle mouse passing over it changes nothing.
    /// </summary>
    private void SetBookmarkDropZoneActive(bool active) {
        _drops.IsBookmarkTarget = active;
        BookmarkDropZone.Background = active ? Palette.DropZoneActiveFill : Palette.DropZoneFill;
        BookmarkDropZoneGlyph.Foreground = active ? Palette.DropZoneActiveGlyph : Palette.DropZoneGlyph;
        BookmarkDropZoneGlyph.FontWeight = active ? FontWeights.Bold : FontWeights.Normal;
        _drag.UpdateForCurrentTarget();
    }

}


/// <summary>
/// A folder panel asking for the context menu of one folder, placed at
/// <paramref name="Host"/>.
/// </summary>
public sealed record FolderMenuRequest(FrameworkElement Host, string Folder);


/// <summary>
/// A folder panel asking for the menu of a right-button drop, placed at
/// <paramref name="Host"/>.
/// </summary>
public sealed record DropMenuRequest(FrameworkElement Host, DropPlan Plan);
