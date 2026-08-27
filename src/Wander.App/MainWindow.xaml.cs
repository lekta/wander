using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit.Highlighting;
using Microsoft.Web.WebView2.Core;
using Wander.App.Controls;
using Wander.App.Converters;
using Wander.App.DragPreview;
using Wander.App.Menu;
using Wander.App.Resources;
using Wander.App.Util;
using Wander.App.ViewModels;
using Wander.App.Views;
using Wander.Core;
using Wander.Core.FileSystem;
using Wander.Core.Icons;
using Wander.Core.Menu;
using Wander.Core.Navigation;
using Wander.Core.Persistence;
using Wander.Core.Shell;
// Disambiguate from System.Windows.DragAction (used by QueryContinueDrag).
using DragAction = Wander.App.DragPreview.DragAction;

namespace Wander.App;

public partial class MainWindow : Window {
    // --- Tree expand/collapse gesture state -----------------------------
    private bool _userClickedExpander;
    private bool _altWasHeld;

    // --- Tree as drag source / operation target -------------------------
    private TreeNodeViewModel? _treeDragNode;
    private Point _treeDragOrigin;
    private TreeNodeViewModel? _treeMenuNode;

    // --- Drag source state ---------------------------------------------
    private int _dragPathCount;
    private string? _dragFirstName;

    // --- Drag preview ---------------------------------------------------
    private DragPreviewWindow? _dragPreview;

    /// <summary>
    /// Where a drop would land and what it would do — see
    /// <see cref="DropTargetController"/>. The plaque that follows the
    /// cursor is drawn here from what it reports.
    /// </summary>
    private DropTargetController _drops = null!;


    public MainWindow() {
        InitializeComponent();
        Loaded += OnLoaded;
        // The OS clipboard is re-read here rather than watched: reading it is
        // a cross-process call on an exclusively-opened resource, and
        // PasteCommand.CanExecute runs dozens of times a second. Activation
        // is the one moment the answer has to be right — to paste, the user
        // has to come back to this window anyway.
        Activated += (_, _) => (DataContext as MainViewModel)?.SyncClipboardFromSystem();
    }


    // --- Window geometry persistence -----------------------------------

    private void OnSourceInitialized(object? sender, EventArgs e) {
        RestoreWindowGeometry();
    }

    private void OnClosing(object? sender, CancelEventArgs e) {
        SaveWindowGeometry();
        // Releases the cached IContextMenu, and with it the third-party
        // handler DLLs it keeps referenced.
        _shellMenus.Dispose();
        // Whatever the last, unfinished second of measurements holds.
        Wander.Core.Diagnostics.PerfLog.Flush();
    }

    private void RestoreWindowGeometry() {
        if (!ServiceLocator.IsRegistered<IAppStateStore>()) {
            return;
        }
        var state = ServiceLocator.Get<IAppStateStore>().Load();
        if (state.Window is not { } geom) {
            return;
        }

        // Restore size first — keeping a sane minimum so a previous truncation
        // can't wedge the window down to a few pixels.
        if (geom.Width >= 320 && geom.Height >= 240) {
            Width = geom.Width;
            Height = geom.Height;
        }

        // Restore position, clamped to the virtual screen. This handles the
        // "saved on a monitor that is no longer connected" case without
        // dropping the window off-screen.
        double vsLeft = SystemParameters.VirtualScreenLeft;
        double vsTop = SystemParameters.VirtualScreenTop;
        double vsRight = vsLeft + SystemParameters.VirtualScreenWidth;
        double vsBottom = vsTop + SystemParameters.VirtualScreenHeight;

        // Keep at least 100 px of titlebar visible so the user can grab it.
        double minLeft = vsLeft - Width + 100;
        double maxLeft = vsRight - 100;
        double minTop = vsTop;
        double maxTop = vsBottom - 60;

        Left = Math.Min(Math.Max(geom.Left, minLeft), maxLeft);
        Top = Math.Min(Math.Max(geom.Top, minTop), maxTop);
        WindowStartupLocation = WindowStartupLocation.Manual;

        if (geom.Maximized) {
            WindowState = WindowState.Maximized;
        }
    }

    private void SaveWindowGeometry() {
        if (!ServiceLocator.IsRegistered<IAppStateStore>()) {
            return;
        }
        var store = ServiceLocator.Get<IAppStateStore>();
        var existing = store.Load();

        // When the window is currently Maximized, Left/Top/Width/Height
        // report the maximized rectangle. RestoreBounds gives the geometry
        // the window had before being maximized — that's what we want to
        // remember so a future Restore lands at the same size and position.
        Rect bounds = WindowState == WindowState.Normal
            ? new Rect(Left, Top, Width, Height)
            : RestoreBounds;

        store.Save(existing with {
            Window = new WindowGeometry {
                Left = bounds.Left,
                Top = bounds.Top,
                Width = bounds.Width,
                Height = bounds.Height,
                Maximized = WindowState == WindowState.Maximized,
            },
        });
    }

    private MainViewModel Vm => (MainViewModel)DataContext;


    // --- Preview pane layout --------------------------------------------


    private void OnLoaded(object sender, RoutedEventArgs e) {
        if (DataContext is MainViewModel vm) {
            vm.PropertyChanged += OnVmPropertyChanged;
            vm.Nav.PropertyChanged += OnNavPropertyChanged;
        }
        // Built here rather than in the constructor: the folder it falls
        // back to is the one the view model is listing, and there is no view
        // model yet when the window is constructed.
        _drops = new DropTargetController(() => Vm.CurrentPath);
        // A third-party command can create, rename or delete behind our
        // back, so a successful one invalidates both the listing and the
        // cached shell answer.
        _contextMenus = new ContextMenuFactory(BuildMenuBindings(), () => {
            _shellMenus.Invalidate();
            Vm.RefreshCommand.Execute(null);
        });
        // Any re-listing — navigation, refresh, an operation finishing —
        // means the cached shell answer describes a folder that has moved on.
        Vm.Entries.CollectionChanged += (_, _) => _shellMenus.Invalidate();
        // Quiet unless something is slow: what the UI thread spends time
        // on lands in the session log — see Core/Diagnostics/PerfLog.
        if (ServiceLocator.IsRegistered<Wander.Core.Logging.ILogger>()) {
            Wander.Core.Diagnostics.PerfLog.Start(ServiceLocator.Get<Wander.Core.Logging.ILogger>());
        }
        Diagnostics.UiStallWatch.Start(Dispatcher);
        ApplyPreviewLayout();
        ApplyBookmarksLayout();
        // Native-size cap (so small images don't stretch above 100 %) is
        // now done in XAML via BitmapPixelSizeConverter on MaxWidth/MaxHeight
        // — synchronous with WPF's measure pass instead of an async
        // DependencyPropertyDescriptor callback that races layout.
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e) {
        switch (e.PropertyName) {
            case nameof(MainViewModel.IsPreviewVisible):
            case nameof(MainViewModel.PreviewWidth):
                ApplyPreviewLayout();
                break;

            case nameof(MainViewModel.IsBookmarksExpanded):
                ApplyBookmarksLayout();
                break;
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

    /// <summary>
    /// Layout only — how much room the preview pane gets, and whether it
    /// gets any. What is drawn inside it belongs to
    /// <see cref="Views.PreviewPane"/>.
    /// </summary>
    private void ApplyPreviewLayout() {
        if (Vm.IsPreviewVisible) {
            PreviewSplitterColumn.Width = new GridLength(4);
            PreviewColumn.Width = new GridLength(Vm.PreviewWidth);
        } else {
            PreviewSplitterColumn.Width = new GridLength(0);
            PreviewColumn.Width = new GridLength(0);
        }
    }

    private void PreviewSplitter_DragCompleted(object sender, DragCompletedEventArgs e) {
        Vm.PreviewWidth = PreviewColumn.ActualWidth;
    }


    // --- Global hotkeys not bound to commands ---------------------------

    protected override void OnPreviewKeyDown(KeyEventArgs e) {
        base.OnPreviewKeyDown(e);
        if (e.Handled) {
            return;
        }

        // While a name is being edited the editor owns the keyboard, and the
        // window's own shortcuts do not apply — the same as anywhere else in
        // Windows while a text field has focus. This is a tunnelling handler,
        // so without the guard it runs *before* the editor's: Esc cleared the
        // whole selection here and only then reached the editor to cancel.
        if (Vm.RenamingPath is not null) {
            return;
        }

        // Ctrl+L: focus the address bar (parity with browsers / Explorer).
        if (e.Key == Key.L && Keyboard.Modifiers == ModifierKeys.Control) {
            BeginAddressEdit();
            e.Handled = true;
            return;
        }

        // F4: drop down the recently visited folders (Explorer parity).
        if (e.Key == Key.F4) {
            RecentToggle.IsChecked = RecentToggle.IsChecked != true;
            e.Handled = true;
            return;
        }

        // Ctrl+F: focus the search box. Skip when the user is typing inside
        // the code preview — AvalonEdit owns Ctrl+F there for its own search
        // panel, and stealing it would be surprising.
        if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control) {
            if (!Preview.IsCodeEditorFocused) {
                SearchBox.Focus();
                SearchBox.SelectAll();
                e.Handled = true;
                return;
            }
        }

        // F2: rename the primary selected entry.
        if (e.Key == Key.F2 && Vm.SelectedEntry is FileSystemEntry) {
            FileList.StartRename();
            e.Handled = true;
            return;
        }

        // Esc: clear selection in whichever right-pane list is active.
        if (e.Key == Key.Escape) {
            FileList.ClearSelection();
            // Don't mark handled — the search box and the address bar want
            // Esc too. The rename editor is handled by the guard above,
            // because clearing the selection first would be destructive.
        }
    }

    private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e) {
        // Esc: clear the filter on the first press, then hand focus back to
        // the active file list on the second press (so the box doesn't trap
        // the user). Enter also hands focus to the list so arrow keys work
        // straight away on the filtered results.
        if (e.Key == Key.Escape) {
            if (!string.IsNullOrEmpty(Vm.SearchQuery)) {
                Vm.SearchQuery = "";
            } else {
                FileList.FocusList();
            }
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Enter) {
            FileList.FocusList();
            e.Handled = true;
        }
    }

    // --- Address bar ----------------------------------------------------

    /// <summary>
    /// Switches the address strip from breadcrumbs to the editable path and
    /// puts the caret in it. The TextBox is Collapsed until the flag flips
    /// and a collapsed element cannot take focus, hence the queued
    /// Focus/SelectAll.
    /// </summary>
    private void BeginAddressEdit() {
        Vm.Nav.IsEditingAddress = true;
        Dispatcher.BeginInvoke(new Action(() => {
            AddressBox.Focus();
            AddressBox.SelectAll();
        }), DispatcherPriority.Input);
    }

    /// <summary>
    /// A click anywhere on the address strip that is not a control switches
    /// it to the editable path. Tunnelling, so the empty space around the
    /// crumbs counts too — the buttons, the chevron and the text box itself
    /// are excluded by hit test rather than by relying on them to swallow
    /// the event, which is what left most of the strip inert before.
    /// </summary>
    private void AddressBar_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
        if (Vm.Nav.IsEditingAddress || IsInsideControl(e.OriginalSource)) {
            return;
        }

        BeginAddressEdit();
        e.Handled = true;
    }

    private static bool IsInsideControl(object originalSource) {
        // Through ListVisuals.Ancestors, not VisualTreeHelper directly: a
        // click can land on a Run, which is not a visual and throws when
        // asked for its visual parent. Same for the three walks below.
        foreach (var hit in ListVisuals.Ancestors(originalSource)) {
            if (hit is ButtonBase or TextBoxBase) {
                return true;
            }
        }

        return false;
    }

    private void AddressBox_PreviewKeyDown(object sender, KeyEventArgs e) {
        // Esc: abandon the edit, restore the real path, hand focus back to
        // the file list so the box does not trap the user.
        if (e.Key == Key.Escape) {
            Vm.AddressText = Vm.CurrentPath ?? "";
            Vm.Nav.IsEditingAddress = false;
            FileList.FocusList();
            e.Handled = true;
            return;
        }

        // Enter: navigate. A successful navigation drops edit mode on its
        // own (NavigationController), so a still-editing strip afterwards
        // means the path was rejected — stay put and let the user fix it.
        if (e.Key == Key.Enter) {
            Vm.NavigateCommand.Execute(null);
            if (!Vm.Nav.IsEditingAddress) {
                FileList.FocusList();
            }
            e.Handled = true;
        }
    }

    private void AddressBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) {
        Vm.AddressText = Vm.CurrentPath ?? "";
        Vm.Nav.IsEditingAddress = false;
    }

    private void RecentList_SelectionChanged(object sender, SelectionChangedEventArgs e) {
        if (RecentList.SelectedItem is not string path) {
            return;
        }

        // Clearing the selection re-enters this handler with a null item
        // (bailed out above) and lets the same entry be picked next time.
        RecentList.SelectedItem = null;
        RecentToggle.IsChecked = false;
        Vm.NavigateTo(path, NavigationSource.Address);
    }

    private void OnNavPropertyChanged(object? sender, PropertyChangedEventArgs e) {
        // Deep paths overflow the strip; showing their tail (the folder the
        // user is actually in) beats showing the drive letter.
        if (e.PropertyName == nameof(NavigationController.Breadcrumbs)) {
            Dispatcher.BeginInvoke(new Action(CrumbScroll.ScrollToRightEnd), DispatcherPriority.Loaded);
        }
    }


    // --- Context menu ---------------------------------------------------
    // All three list views share one menu. It is assembled per right-click
    // by ContextMenuBuilder (Core decides the shape) and rendered by
    // ContextMenuFactory, and it is opened by hand instead of through
    // ContextMenuService — the contents depend on *what* was clicked, and
    // the service commits to showing a menu before we can fix the selection.

    private ContextMenuFactory? _contextMenus;
    private readonly ShellMenuCache _shellMenus = new();


    private void FileList_ContextMenuRequested(object? sender, FileListMenuRequest e) {
        ShowContextMenu(e.Host, e.Placement, e.IsBackground);
    }

    private void ShowContextMenu(FrameworkElement host, PlacementMode placement, bool isBackground, string? folderPath = null) {
        if (_contextMenus is null) {
            return;
        }

        var vm = Vm;
        var settings = vm.MenuSettings;
        var target = new ContextMenuTarget {
            Selection = isBackground ? Array.Empty<FileSystemEntry>() : vm.SelectedEntries,
            FolderPath = folderPath ?? vm.CurrentPath,
            IsBackground = isBackground,
            IsReadOnlyLocation = vm.IsCurrentShellNamespace,
            IsRecycleBin = vm.IsCurrentRecycleBin,
            CanPaste = vm.PasteCommand.CanExecute(null),
            CanUndo = vm.UndoCommand.CanExecute(null),
            ViewMode = vm.ViewMode.ToString(),
            SortKey = vm.Settings.SortKey,
            SortAscending = vm.Settings.SortAscending,
            GroupFoldersFirst = vm.Settings.GroupFoldersFirst,
            IsPreviewVisible = vm.IsPreviewVisible,
        };

        var session = QueryShellMenu(target, settings);
        if (session is not null) {
            // Opening a menu is the only way we ever learn which handlers
            // are installed, so this is where the settings dialog's
            // per-extension checkbox list gets its names.
            vm.Settings.NoteShellExtensions(
                session.Items.Where(item => !item.IsSeparator).Select(item => item.Header));
        }

        var model = ContextMenuBuilder.Build(target, settings, session?.Items);
        if (model.Count == 0) {
            return;
        }

        var menu = _contextMenus.Build(model, session);
        menu.DataContext = vm;
        menu.PlacementTarget = host;
        menu.Placement = placement;
        menu.IsOpen = true;
    }

    private IShellContextMenuSession? QueryShellMenu(ContextMenuTarget target, ContextMenuSettings settings) {
        if (!settings.ShellExtensionsEnabled
            || target.IsReadOnlyLocation
            || string.IsNullOrEmpty(target.FolderPath)) {
            return null;
        }

        var paths = target.Selection.Select(entry => entry.FullPath).ToArray();

        return _shellMenus.Acquire(paths, target.FolderPath);
    }

    /// <summary>
    /// Maps every built-in menu id onto the command that runs it. Most come
    /// straight off the ViewModel; Rename is the exception — it needs the
    /// name prompt, which lives here in the view.
    /// </summary>
    private Dictionary<MenuCommandId, MenuBinding> BuildMenuBindings() {
        var vm = Vm;
        var rename = new RelayCommand(_ => FileList.StartRename(), _ => vm.SelectedEntry is not null);

        return new Dictionary<MenuCommandId, MenuBinding> {
            [MenuCommandId.Open] = new(vm.OpenCommand),
            [MenuCommandId.OpenWith] = new(vm.OpenWithCommand),
            [MenuCommandId.OpenInTerminal] = new(vm.OpenInTerminalCommand),

            [MenuCommandId.Cut] = new(vm.CutCommand),
            [MenuCommandId.Copy] = new(vm.CopyCommand),
            [MenuCommandId.Paste] = new(vm.PasteCommand),
            [MenuCommandId.CopyPath] = new(vm.CopyPathCommand),
            [MenuCommandId.CopyName] = new(vm.CopyNameCommand),
            [MenuCommandId.CreateShortcut] = new(vm.CreateShortcutCommand),

            [MenuCommandId.Rename] = new(rename),
            [MenuCommandId.Delete] = new(vm.DeleteCommand),
            [MenuCommandId.NewFolder] = new(vm.NewFolderCommand),

            [MenuCommandId.ViewDetails] = new(vm.SetViewModeCommand, nameof(ViewMode.Details)),
            [MenuCommandId.ViewTiles] = new(vm.SetViewModeCommand, nameof(ViewMode.Tiles)),
            [MenuCommandId.ViewLargeIcons] = new(vm.SetViewModeCommand, nameof(ViewMode.LargeIcons)),
            [MenuCommandId.TogglePreview] = new(vm.TogglePreviewCommand),

            [MenuCommandId.SortByName] = new(vm.SetSortKeyCommand, nameof(SortKey.Name)),
            [MenuCommandId.SortByDate] = new(vm.SetSortKeyCommand, nameof(SortKey.ModifiedDate)),
            [MenuCommandId.SortBySize] = new(vm.SetSortKeyCommand, nameof(SortKey.Size)),
            [MenuCommandId.SortByType] = new(vm.SetSortKeyCommand, nameof(SortKey.Type)),
            [MenuCommandId.SortAscending] = new(vm.ToggleSortAscendingCommand),
            [MenuCommandId.SortFoldersFirst] = new(vm.ToggleGroupFoldersFirstCommand),

            [MenuCommandId.RestoreFromRecycleBin] = new(vm.RestoreFromRecycleBinCommand),

            [MenuCommandId.Refresh] = new(vm.RefreshCommand),
            [MenuCommandId.Undo] = new(vm.UndoCommand),
            [MenuCommandId.Properties] = new(vm.PropertiesCommand),
        };
    }


    // --- Tree: selection -----------------------------------------------

    private void Tree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e) {
        if (e.NewValue is TreeNodeViewModel node && !string.IsNullOrEmpty(node.FullPath)) {
            Vm.NavigateAndSelectFolder(node.FullPath, Wander.Core.Navigation.NavigationSource.Drives);
        }
    }

    /// <summary>
    /// The bookmark row menu — one item, "remove from bookmarks". Built here
    /// rather than declared in the row template: a ContextMenu inside a
    /// DataTemplate gets its own name scope and its own visual tree, so a
    /// binding that reaches out to the window by name silently never
    /// resolves, and the menu item does nothing when clicked.
    /// </summary>
    private void ShowBookmarkMenu(FrameworkElement placement, TreeNodeViewModel node) {
        if (!node.IsRemovableBookmark) {
            return;
        }

        var remove = new MenuItem { Header = Strings.BookmarksRemove };
        remove.Click += (_, _) => Vm.RemoveBookmarkCommand.Execute(node);

        var menu = new ContextMenu {
            PlacementTarget = placement,
            Placement = PlacementMode.Bottom,
        };
        menu.Items.Add(remove);
        menu.IsOpen = true;
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
        if (e.NewValue is TreeNodeViewModel node && !string.IsNullOrEmpty(node.FullPath)) {
            Vm.NavigateAndSelectFolder(node.FullPath, Wander.Core.Navigation.NavigationSource.Bookmark);
        }
    }


    // --- Tree: custom expand/collapse semantics ------------------------

    private void Tree_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
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
        if (IsInsideControl(e.OriginalSource)) {
            _treeDragNode = null;

            return;
        }

        // Arm a drag from the row under the cursor. The tree is a drag
        // source in Explorer and users reach for it — the panel is where
        // the folder you want to move *to* is visible, so it is also where
        // the folder you want to move *from* often is.
        _treeDragNode = NodeAt(e.OriginalSource);
        _treeDragOrigin = e.GetPosition(this);
    }

    private void Tree_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e) {
        _treeDragNode = null;
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
        var paths = new[] { node.FullPath };
        StartDrag((DependencyObject)sender, paths, paths);
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

        FileList.ClearSelection();
        Vm.SelectExternalPath(_treeMenuNode.FullPath);
        e.Handled = true;
    }

    private void Tree_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e) {
        if (_treeMenuNode is not { } node || sender is not FrameworkElement host) {
            return;
        }

        // FolderPath is the clicked folder rather than the open one: "Paste"
        // and "New folder" in this menu mean "into what I right-clicked".
        ShowContextMenu(host, PlacementMode.MousePoint, isBackground: false, folderPath: node.FullPath);
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
            ExpandDirectChildren(node);
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
            CollapseRecursively(node);
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

    private static void ExpandDirectChildren(TreeNodeViewModel node) {
        foreach (var child in node.Children) {
            if (string.IsNullOrEmpty(child.FullPath)) {
                continue;
            }
            child.IsExpanded = true;
        }
    }

    private static void CollapseRecursively(TreeNodeViewModel node) {
        foreach (var child in node.Children) {
            if (child.IsExpanded) {
                CollapseRecursively(child);
                child.IsExpanded = false;
            }
        }
    }


    // --- Drag source ----------------------------------------------------
    // The gesture that starts a drag belongs to whatever the user grabbed —
    // the file list or a tree row. Running the drag does not: the preview
    // window, the drop-target highlight and the effect calculation are one
    // shared pipeline, and it lives here.

    private void FileList_DragStartRequested(object? sender, FileListDragRequest e) {
        StartDrag(e.Source, e.Paths, e.Payload);
    }

    private void StartDrag(DependencyObject src, string[] paths, string[] payload) {
        _dragPathCount = paths.Length;
        _dragFirstName = Path.GetFileName(paths[0].TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        _drops.Clear();

        var preview = new DragPreviewWindow();
        preview.SetIcon(IconConverter.Load(paths[0], IconSize.Normal));
        preview.SetCount(paths.Length);
        string startDesc = paths.Length == 1 ? $"Drag '{_dragFirstName}'" : $"Drag {paths.Length} items";
        preview.SetAction(DragAction.Forbidden, startDesc, null);
        preview.Show();
        preview.MoveToCursor();
        _dragPreview = preview;

        var feedback = new GiveFeedbackEventHandler(OnGiveFeedback);
        System.Windows.DragDrop.AddGiveFeedbackHandler(src, feedback);

        try {
            var data = new DataObject(DataFormats.FileDrop, payload);
            System.Windows.DragDrop.DoDragDrop(src, data, DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link);
        } catch {
            // drop target may throw on rejection — ignore.
        } finally {
            System.Windows.DragDrop.RemoveGiveFeedbackHandler(src, feedback);
            _drops.Clear();
            SetBookmarkDropZoneActive(false);
            preview.Close();
            _dragPreview = null;
        }
    }

    private void OnGiveFeedback(object sender, GiveFeedbackEventArgs e) {
        // Keep the system's Copy/Move/None cursor in addition to our preview.
        e.UseDefaultCursors = true;
        if (_dragPreview is null) {
            return;
        }
        _dragPreview.MoveToCursor();
        UpdatePreviewForCurrentTarget();
    }

    private void UpdatePreviewForCurrentTarget() {
        if (_dragPreview is null) {
            return;
        }

        DragAction action;
        string desc;
        string? targetText = null;
        int count = _dragPathCount;

        // Dropping into the bookmarks region is not a file operation at all,
        // so none of the copy/move/link vocabulary applies. Without this the
        // plaque kept showing whatever the last real target had offered —
        // "Переместить … в Downloads" while hovering the bookmarks strip.
        if (_drops.IsBookmarkTarget) {
            ShowDragPreview();
            _dragPreview.SetAction(DragAction.Link, FormatBookmarkDesc(count), null);

            return;
        }

        if (_drops.Effect == DragDropEffects.None || _drops.Target is null) {
            // "Forbidden" branch. Two sub-cases:
            //  • Self-drop with a specific reason ("into own subfolder", …) —
            //    we want to surface that reason loudly so the user knows
            //    *why* the drop is refused.
            //  • Nothing useful under the cursor (column header, scrollbar,
            //    splitter) — the system's no-drop cursor is enough, and a
            //    red "Cannot drop here" plaque hovering over a perfectly
            //    valid neighbouring folder is just noise. Hide the preview.
            if (_drops.SelfDropReason != SelfDropReason.None && _drops.Target is not null) {
                ShowDragPreview();
                action = DragAction.Forbidden;
                desc = PathSafety.FormatReason(_drops.SelfDropReason, _drops.SelfDropOffender, _drops.Target);
            } else {
                HideDragPreview();
                return;
            }
        } else {
            ShowDragPreview();
            action = _drops.Effect switch {
                DragDropEffects.Move => DragAction.Move,
                DragDropEffects.Link => DragAction.Link,
                _ => DragAction.Copy,
            };
            string verb = action switch {
                DragAction.Move => Strings.DragMove,
                DragAction.Link => Strings.DragLink,
                _ => Strings.DragCopy,
            };
            string what = count == 1
                ? string.Format(Strings.DragOneItem, _dragFirstName)
                : string.Format(Strings.DragItems, count);
            desc = $"{verb} {what}";
            targetText = string.Format(Strings.DragTarget, FormatTarget(_drops.Target));
        }

        _dragPreview.SetAction(action, desc, targetText);
    }

    private void ShowDragPreview() {
        if (_dragPreview is { Visibility: not Visibility.Visible } p) {
            p.Visibility = Visibility.Visible;
        }
    }

    private void HideDragPreview() {
        if (_dragPreview is { Visibility: Visibility.Visible } p) {
            p.Visibility = Visibility.Hidden;
        }
    }

    private static string FormatTarget(string path) {
        string trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string name = Path.GetFileName(trimmed);
        return string.IsNullOrEmpty(name) ? path : name;
    }

    private string FormatBookmarkDesc(int count) {
        string what = count == 1
            ? string.Format(Strings.DragOneItem, _dragFirstName)
            : string.Format(Strings.DragItems, count);

        return string.Format(Strings.DragAddToBookmarks, what);
    }


    // --- Drop target ----------------------------------------------------
    //
    // Working out where a drop would land, whether it is allowed and what it
    // would do lives in DropTargetController; the window is left with the
    // two ends the controller deliberately does not have — the XAML event
    // handlers, and running the plan through the view model.

    private void OnDragOver(object sender, DragEventArgs e) {
        _drops.DragOver(e);
    }

    private void OnDrop(object sender, DragEventArgs e) {
        try {
            if (_drops.PlanDrop(e) is not { } plan) {
                return;
            }

            Vm.HandleDrop(plan.Paths, plan.Target, plan.Effect);
            e.Handled = true;
        } finally {
            _drops.Clear();
        }
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
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) {
                return;
            }
            var paths = (string[])e.Data.GetData(DataFormats.FileDrop);
            int added = 0;
            foreach (string p in paths) {
                if (Directory.Exists(p)) {
                    Vm.AddBookmark(p);
                    added++;
                }
            }
            if (added == 0) {
                Vm.Status = Strings.BookmarksFoldersOnly;
            }
            e.Handled = true;
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

    private static readonly SolidColorBrush _dropZoneIdleFill = new(Color.FromRgb(0xEC, 0xEC, 0xEC));
    private static readonly SolidColorBrush _dropZoneIdleGlyph = new(Color.FromRgb(0x88, 0x88, 0x88));
    private static readonly SolidColorBrush _dropZoneActiveFill = new(Color.FromRgb(0xCC, 0xE8, 0xFF));
    private static readonly SolidColorBrush _dropZoneActiveGlyph = new(Color.FromRgb(0x00, 0x55, 0xA8));

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
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) {
                return;
            }
            var paths = (string[])e.Data.GetData(DataFormats.FileDrop);
            int added = 0;
            foreach (var p in paths) {
                if (Directory.Exists(p)) {
                    Vm.AddBookmark(p);
                    added++;
                }
            }
            if (added == 0) {
                Vm.Status = Strings.BookmarksFoldersOnly;
            }
            e.Handled = true;
        } finally {
            SetBookmarkDropZoneActive(false);
        }
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

        return paths.Any(p => Directory.Exists(p) && !Vm.IsBookmarked(p));
    }

    /// <summary>
    /// Lights the drop strip while a drag it can accept is over the
    /// bookmarks. This is the strip's only reactive state — it is not a
    /// button, so an idle mouse passing over it changes nothing.
    /// </summary>
    private void SetBookmarkDropZoneActive(bool active) {
        _drops.IsBookmarkTarget = active;
        BookmarkDropZone.Background = active ? _dropZoneActiveFill : _dropZoneIdleFill;
        BookmarkDropZoneGlyph.Foreground = active ? _dropZoneActiveGlyph : _dropZoneIdleGlyph;
        BookmarkDropZoneGlyph.FontWeight = active ? FontWeights.Bold : FontWeights.Normal;
        UpdatePreviewForCurrentTarget();
    }

}
