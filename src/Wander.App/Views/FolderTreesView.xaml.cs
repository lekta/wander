using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Wander.App.DragPreview;
using Wander.App.Resources;
using Wander.App.Util;
using Wander.App.ViewModels;
using Wander.Core.Navigation;


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


    public FolderTreesView() {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }


    /// <summary>
    /// Hands over the two window-level collaborators of drag &amp; drop.
    /// Not constructor arguments because the control is built by XAML;
    /// called once, from <c>MainWindow.OnLoaded</c>, where both exist.
    /// </summary>
    public void Connect(DropTargetController drops, OutgoingDrag drag) {
        _drops = drops;
        _drag = drag;
    }


    private MainViewModel Vm => (MainViewModel)DataContext;


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
        if (e.PropertyName == nameof(MainViewModel.IsBookmarksExpanded)) {
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
    /// </summary>
    public void RevealAndFocus(NavigationSource pane) {
        if (pane == NavigationSource.Bookmark) {
            // Put away, the tree is Collapsed and cannot take focus; a
            // shortcut that silently did nothing would read as broken.
            Vm.IsBookmarksExpanded = true;
            UpdateLayout();
        }

        var tree = pane == NavigationSource.Bookmark ? BookmarksTree : Tree;
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
    /// The keyboard belongs on a row, not on the tree: a TreeView with focus
    /// and no focused item resumes the arrow keys from wherever the cursor
    /// happened to be. Same reasoning as FileListView.FocusList.
    /// </summary>
    private static bool FocusTree(TreeView tree) {
        if (tree.SelectedItem is { } selected && ContainerFor(tree, selected) is { } container) {
            return container.Focus();
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
        OnTreeSelectionChanged(sender, e.NewValue, NavigationSource.Drives);
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
    private void OnTreeSelectionChanged(object sender, object? item, NavigationSource source) {
        if (item is not TreeNodeViewModel node || string.IsNullOrEmpty(node.FullPath)) {
            return;
        }

        if (_treeClickNavigates) {
            NavigateFromTree(node.FullPath, source);

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
        }
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
    /// to the list. Both have to be caught here: the window's own bindings
    /// would otherwise open whatever the <em>file list</em> has selected and
    /// clear its selection, neither of which is what the user is pointing at.
    /// </summary>
    private void Tree_PreviewKeyDown(object sender, KeyEventArgs e) {
        if (sender is not TreeView tree) {
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

    /// <summary>The same menu, from the right mouse button.</summary>
    private void BookmarksTree_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e) {
        if (NodeAt(e.OriginalSource) is { } node && sender is FrameworkElement host) {
            ShowBookmarkMenu(host, node);
            e.Handled = true;
        }
    }

    private void Bookmarks_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e) {
        OnTreeSelectionChanged(sender, e.NewValue, NavigationSource.Bookmark);
    }


    // --- Tree: custom expand/collapse semantics ------------------------

    private void Tree_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
        // Cleared first: every path out of this handler that is not "the
        // user pressed a row" must leave the selection change silent.
        _treeClickNavigates = false;

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
        if (_treeDragNode is not { } node || e.LeftButton != MouseButtonState.Pressed) {
            return;
        }

        var pos = e.GetPosition(this);
        if (Math.Abs(pos.X - _treeDragOrigin.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(pos.Y - _treeDragOrigin.Y) < SystemParameters.MinimumVerticalDragDistance) {
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
    /// Right-clicking a folder in the drives tree targets that folder —
    /// without navigating to it, the way Explorer behaves. The tree is a
    /// second selection source: the clicked node becomes the selection the
    /// menu (and Ctrl+C, Delete, Alt+Enter after it) operates on, so the
    /// file list's own selection is dropped first to keep exactly one
    /// highlighted set on screen.
    /// </summary>
    private void Tree_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e) {
        _treeMenuNode = NodeAt(e.OriginalSource);
        if (_treeMenuNode is null) {
            return;
        }

        FolderTargeted?.Invoke(this, _treeMenuNode.FullPath);
        e.Handled = true;
    }

    private void Tree_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e) {
        if (_treeMenuNode is not { } node || sender is not FrameworkElement host) {
            return;
        }

        // The folder is the clicked one rather than the open one: "Paste"
        // and "New folder" in this menu mean "into what I right-clicked".
        ContextMenuRequested?.Invoke(this, new FolderMenuRequest(host, node.FullPath));
        e.Handled = true;
    }


    /// <summary>The tree node a hit belongs to, if it has a real path.</summary>
    private static TreeNodeViewModel? NodeAt(object originalSource) {
        foreach (var hit in ListVisuals.Ancestors(originalSource)) {
            if (hit is FrameworkElement fe && fe.DataContext is TreeNodeViewModel node) {
                return string.IsNullOrEmpty(node.FullPath) || node.FullPath.StartsWith("shell:", StringComparison.OrdinalIgnoreCase)
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
        _drops.Execute(e, plan => Vm.HandleDrop(plan.Paths, plan.Target, plan.Effect));
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
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) {
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

        var paths = (string[])e.Data.GetData(DataFormats.FileDrop);
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
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) {
            return false;
        }
        var paths = (string[])e.Data.GetData(DataFormats.FileDrop);

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
