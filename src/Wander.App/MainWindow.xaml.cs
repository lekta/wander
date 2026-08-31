using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Wander.App.Controllers;
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
    /// <summary>
    /// Set for the duration of a click on a tree row: only a click opens the
    /// folder it lands on. See <see cref="OnTreeSelectionChanged"/>.
    /// </summary>
    private bool _treeClickNavigates;

    private TreeNodeViewModel? _treeDragNode;
    private Point _treeDragOrigin;
    private TreeNodeViewModel? _treeMenuNode;

    // --- Drag source state ---------------------------------------------
    private int _dragPathCount;
    private string? _dragFirstName;

    // --- Drag preview ---------------------------------------------------
    private DragPreviewWindow? _dragPreview;

    // --- Search window ---------------------------------------------------
    /// <summary>
    /// The search criteria window. Created on first use and hidden rather
    /// than destroyed afterwards, so reopening it finds the last query
    /// still in it.
    /// </summary>
    private SearchWindow? _searchWindow;

    /// <summary>
    /// Set while the application really is shutting down, so the search
    /// window's own Closing handler stops cancelling the close.
    /// </summary>
    private bool _closingForReal;

    /// <summary>
    /// Where a drop would land and what it would do — see
    /// <see cref="DropTargetController"/>. The plaque that follows the
    /// cursor is drawn here from what it reports.
    /// </summary>
    private DropTargetController _drops = null!;


    public MainWindow() {
        InitializeComponent();
        // Here rather than anywhere later: the window is shown by
        // StartupUri the moment the constructor returns, and ShowActivated
        // only counts before that. A smoke run must not take the keyboard
        // away from whoever is working on this desktop, and parking it
        // off-screen alone would not stop it doing that.
        if (App.IsSmokeRun) {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = -32000;
            Top = -32000;
            ShowActivated = false;
            ShowInTaskbar = false;
        }

        Loaded += OnLoaded;
        ContentRendered += OnFirstFrame;
        // The OS clipboard is re-read here rather than watched: reading it is
        // a cross-process call on an exclusively-opened resource, and
        // PasteCommand.CanExecute runs dozens of times a second. Activation
        // is the one moment the answer has to be right — to paste, the user
        // has to come back to this window anyway.
        Activated += (_, _) => (DataContext as MainViewModel)?.SyncClipboardFromSystem();
    }


    /// <summary>
    /// The first frame on screen — the only startup number that matches what
    /// the user feels, and the one a session log could not answer before.
    /// Measured from process start, so it counts the runtime bootstrap that
    /// happens before any of our code runs: for a compressed single-file
    /// build that is a third of the total.
    /// </summary>
    private void OnFirstFrame(object? sender, EventArgs e) {
        ContentRendered -= OnFirstFrame;
        if (ServiceLocator.TryGet<Wander.Core.Logging.ILogger>() is not { } log) {
            return;
        }

        using var self = System.Diagnostics.Process.GetCurrentProcess();
        double ms = (DateTime.Now - self.StartTime).TotalMilliseconds;
        log.Info($"Startup: first frame {ms:F0} ms after process start");
    }


    // --- Window geometry persistence -----------------------------------

    private void OnSourceInitialized(object? sender, EventArgs e) {
        // A smoke run is parked off-screen on purpose — restoring the saved
        // geometry would drag it onto the desktop, and saving it on the way
        // out would leave the real session pointing at (-32000, -32000).
        if (App.IsSmokeRun) {
            return;
        }

        RestoreWindowGeometry();
    }

    private void OnClosing(object? sender, CancelEventArgs e) {
        if (!App.IsSmokeRun) {
            SaveWindowGeometry();
        }

        // The search window refuses ordinary closes so it can be reopened
        // with its contents intact; this is the one close it must not
        // refuse, or the process would outlive its window.
        _closingForReal = true;
        _searchWindow?.Close();

        // Off the screen before the slow part, and only after the geometry
        // has been read off it. Releasing the cached IContextMenu runs
        // third-party shell-extension code, and the runtime spends about
        // another second on its own teardown after this handler returns —
        // all of it with the window still painted, which is what made
        // closing Wander read as a freeze. Nothing below needs the window.
        Hide();

        // Releases the cached IContextMenu, and with it the third-party
        // handler DLLs it keeps referenced.
        _shellMenus.Dispose();
        // Whatever the last, unfinished second of measurements holds.
        Wander.Core.Diagnostics.PerfLog.Flush();
    }

    private void RestoreWindowGeometry() {
        if (ServiceLocator.TryGet<IAppStateStore>() is not { } store) {
            return;
        }
        var state = store.Load();
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
        if (ServiceLocator.TryGet<IAppStateStore>() is not { } store) {
            return;
        }
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
        if (ServiceLocator.TryGet<Wander.Core.Logging.ILogger>() is { } perfLog) {
            Wander.Core.Diagnostics.PerfLog.Start(perfLog);
        }
        Diagnostics.UiStallWatch.Start(Dispatcher);
        // Bubbling, so it sees focus landing anywhere in the window.
        GotKeyboardFocus += OnZoneFocusChanged;
        if (App.IsSmokeRun) {
            StartSmokeCountdown();
        }
        ApplyPreviewLayout();
        ApplyBookmarksLayout();
        // Native-size cap (so small images don't stretch above 100 %) is
        // now done in XAML via BitmapPixelSizeConverter on MaxWidth/MaxHeight
        // — synchronous with WPF's measure pass instead of an async
        // DependencyPropertyDescriptor callback that races layout.
    }

    /// <summary>
    /// Ends a <c>--smoke</c> run. The delay is not a guess at how long
    /// startup takes — the window is already loaded by the time this is
    /// armed — it is room for the work startup hands off: the first folder
    /// listing, the first icons, the watchers. Whatever throws in that
    /// window still reaches the crash hook, and the exit code still says so.
    /// </summary>
    private void StartSmokeCountdown() {
        var timer = new DispatcherTimer(DispatcherPriority.Background) {
            Interval = TimeSpan.FromSeconds(2),
        };
        timer.Tick += (_, _) => {
            timer.Stop();
            Application.Current.Shutdown(0);
        };
        timer.Start();
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

        // Alt+D: the same thing under the name the rest of Windows uses for
        // it. An Alt chord arrives as Key.System with the real key parked in
        // SystemKey.
        if (e.Key == Key.System && e.SystemKey == Key.D && Keyboard.Modifiers == ModifierKeys.Alt) {
            BeginAddressEdit();
            e.Handled = true;
            return;
        }

        // The rest of the Alt chords, caught here for the same reason and
        // one more: the toolbar holds a real Menu, so Alt puts the window
        // into menu mode and the chord is spent navigating the menu bar
        // before command routing gets a look. A KeyBinding in
        // Window.InputBindings therefore never fires once the keyboard is
        // anywhere but the toolbar — which is what left Back dead in the
        // file list. Tunnelling from the window is ahead of both.
        if (e.Key == Key.System && Keyboard.Modifiers == ModifierKeys.Alt) {
            var chord = e.SystemKey switch {
                Key.Left => Vm.BackCommand,
                Key.Right => Vm.ForwardCommand,
                Key.Up => Vm.UpCommand,
                Key.Enter => Vm.PropertiesCommand,
                _ => null,
            };
            if (chord is not null) {
                if (chord.CanExecute(null)) {
                    chord.Execute(null);
                }
                // Handled either way: the chord is ours, and letting a
                // disabled Back fall through to menu mode would open the
                // view menu instead of doing nothing.
                e.Handled = true;
                return;
            }
        }

        // Tab / Shift+Tab move between zones, not between the controls
        // inside them — see CycleZone.
        if (e.Key == Key.Tab) {
            CycleZone(Keyboard.Modifiers == ModifierKeys.Shift ? -1 : 1);
            e.Handled = true;
            return;
        }

        // Ctrl+1: the folder panel, on the current folder's own node.
        // Pressed again, the other panel. The digits follow the screen:
        // the folder panels are to the left of the list, so they are 1.
        if (e.Key == Key.D1 && Keyboard.Modifiers == ModifierKeys.Control) {
            FocusFolderPane(toggle: true);
            e.Handled = true;
            return;
        }

        // Ctrl+2: back to the list, from wherever the keyboard wandered off.
        if (e.Key == Key.D2 && Keyboard.Modifiers == ModifierKeys.Control) {
            FocusZone(Zone.FileList);
            e.Handled = true;
            return;
        }

        // Ctrl+Shift+E: the same reveal without the toggle — always the
        // panel the current folder was opened from (Explorer parity).
        if (e.Key == Key.E && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift)) {
            FocusFolderPane(toggle: false);
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
        // Ctrl+Shift+F: the search window, with its own criteria.
        // Ctrl+F: the box in the toolbar, which is the quick filter.
        // Both skipped while the user is typing inside the code preview —
        // AvalonEdit owns Ctrl+F there for its own search panel, and
        // stealing it would be surprising.
        if (e.Key == Key.F && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift)) {
            if (!Preview.IsCodeEditorFocused) {
                OpenSearchWindow();
                e.Handled = true;

                return;
            }
        }

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

        // Esc anywhere in the address strip hands the keyboard back to the
        // list. Not just inside the text box: Tab and a click can leave the
        // focus on a breadcrumb button, and Esc there used to do nothing at
        // all — the strip kept the keyboard with no way out but the mouse.
        if (e.Key == Key.Escape && ZoneOf(Keyboard.FocusedElement) == Zone.Address) {
            Vm.AddressText = Vm.CurrentPath ?? "";
            Vm.Nav.IsEditingAddress = false;
            FileList.FocusList();
            e.Handled = true;

            return;
        }

        // Esc: clear the selection — but only with the keyboard actually in
        // the list. Everywhere else Esc means "leave this zone", and each
        // zone handles its own: the search box, the address bar, the trees.
        // Don't mark handled — those handlers run after this one. The rename
        // editor is handled by the guard above, because clearing the
        // selection first would be destructive.
        if (e.Key == Key.Escape && ZoneOf(Keyboard.FocusedElement) is Zone.FileList or null) {
            FileList.ClearSelection();
        }
    }

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
            FileList.FocusList();
            e.Handled = true;

            return;
        }

        // Enter: the box is the shallow half — the filter has already been
        // applied letter by letter — so Enter only moves the keyboard to
        // the results. A deep search is set up in the search window, and
        // Enter belongs to it there.
        if (e.Key == Key.Enter) {
            FileList.FocusList();
            e.Handled = true;
        }
    }


    private void SearchOptions_Click(object sender, RoutedEventArgs e) {
        OpenSearchWindow();
    }


    /// <summary>
    /// Raises the search window, creating it the first time. Kept alive
    /// across closes so reopening finds the last query still in it, and so
    /// the criteria in the view model have exactly one editor.
    /// </summary>
    private void OpenSearchWindow() {
        if (_searchWindow is null) {
            _searchWindow = new SearchWindow {
                Owner = this,
                DataContext = Vm,
            };
            // Hidden rather than destroyed: closing is "put it away", and
            // the window is cheap to keep. Cancelling the close is also
            // what keeps Owner and DataContext wired.
            _searchWindow.Closing += (_, args) => {
                if (_closingForReal) {
                    return;
                }
                args.Cancel = true;
                _searchWindow!.Hide();
                Vm.IsSearchWindowOpen = false;
            };
            // Whatever made the window go away, the keyboard has to land
            // somewhere the user can act. Without this, Esc left it on a
            // window that was no longer there and the arrow keys did
            // nothing.
            _searchWindow.Dismissed += (_, _) => {
                if (!_closingForReal) {
                    Activate();
                    FileList.FocusList();
                }
            };
        }

        Vm.IsSearchWindowOpen = true;
        _searchWindow.ShowAndFocus();
    }


    // --- Keyboard zones --------------------------------------------------
    // Tab walks the window a zone at a time rather than a control at a time:
    // one press lands on the toolbar, arrows pick the button. The zones are
    // listed in reading order — the top strip left to right, then the left
    // pane top to bottom, then the list. The preview pane is deliberately
    // not among them: it has no keyboard behaviour yet, so a stop there
    // would be a dead end (see BACKLOG, "клавиатура в панели просмотра").

    private enum Zone { Toolbar, Address, Search, Bookmarks, Drives, FileList }

    private static readonly Zone[] _zoneOrder = {
        Zone.Toolbar, Zone.Address, Zone.Search, Zone.Bookmarks, Zone.Drives, Zone.FileList,
    };

    /// <summary>
    /// The outline that says where the keyboard is. Muted grey rather than
    /// the system accent: it is on screen the whole time, and a bright frame
    /// around whatever you are working in reads as an alarm.
    /// </summary>
    private static readonly Brush _activeZoneBrush = new SolidColorBrush(Color.FromArgb(0x99, 0x8A, 0x8A, 0x8A));

    /// <summary>
    /// Which folder panel Ctrl+1 opens when the current folder came from
    /// neither of them — the address bar, a double click, a restored
    /// session. The last one the keyboard was in wins.
    /// </summary>
    private NavigationSource _lastFolderPane = NavigationSource.Drives;


    /// <summary>Which zone an element belongs to, or null for the chrome between them.</summary>
    private Zone? ZoneOf(object? source) {
        foreach (var hit in ListVisuals.Ancestors(source)) {
            if (ReferenceEquals(hit, NavToolbar)) {
                return Zone.Toolbar;
            }
            if (ReferenceEquals(hit, AddressBar)) {
                return Zone.Address;
            }
            if (ReferenceEquals(hit, SearchBox)) {
                return Zone.Search;
            }
            if (ReferenceEquals(hit, BookmarksTree)) {
                return Zone.Bookmarks;
            }
            if (ReferenceEquals(hit, Tree)) {
                return Zone.Drives;
            }
            if (ReferenceEquals(hit, FileListZone)) {
                return Zone.FileList;
            }
        }

        return null;
    }


    /// <summary>
    /// Moves the keyboard one zone on, skipping the ones that are not on
    /// screen (collapsed bookmarks) or have nothing to focus (all three
    /// toolbar buttons disabled on a fresh start).
    /// </summary>
    private void CycleZone(int delta) {
        int from = Array.IndexOf(_zoneOrder, ZoneOf(Keyboard.FocusedElement) ?? Zone.FileList);
        int count = _zoneOrder.Length;
        for (int step = 1; step <= count; step++) {
            int next = ((from + (delta * step)) % count + count) % count;
            if (FocusZone(_zoneOrder[next])) {
                return;
            }
        }
    }


    /// <summary>
    /// Puts the keyboard back in the file list. Called after a modal dialog
    /// closes: WPF hands focus to the owner window and then picks whatever
    /// focusable element it finds first, which is nowhere the user was.
    /// </summary>
    public void FocusWorkArea() {
        FocusZone(Zone.FileList);
    }


    /// <summary>Puts the keyboard in one zone. False when the zone cannot take it.</summary>
    private bool FocusZone(Zone zone) {
        switch (zone) {
            case Zone.Toolbar:
                foreach (UIElement child in NavToolbar.Children) {
                    if (child.Focusable && child.IsEnabled && child.Focus()) {
                        return true;
                    }
                }

                return false;

            case Zone.Address:
                // The strip turns into the editable path with everything
                // selected — what Explorer does on the same stop, and what
                // makes it useful rather than decorative.
                BeginAddressEdit();

                return true;

            case Zone.Search:
                return SearchBox.Focus();

            case Zone.Bookmarks:
                return Vm.IsBookmarksExpanded && BookmarksTree.HasItems && FocusTree(BookmarksTree);

            case Zone.Drives:
                return FocusTree(Tree);

            case Zone.FileList:
                FileList.FocusList();

                return true;

            default:
                return false;
        }
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


    /// <summary>
    /// Ctrl+1 and Ctrl+Shift+E. Both expand a folder panel down to the
    /// folder on screen and put the keyboard on its node — so the shortcut
    /// answers "where am I" as well as "take me there".
    ///
    /// <para>
    /// <paramref name="toggle"/> is what separates them: Ctrl+1 pressed
    /// while already in a panel swaps to the other one, which is the whole
    /// point of one key for two panels. Ctrl+Shift+E always lands in the
    /// panel the current folder was opened from.
    /// </para>
    /// </summary>
    private void FocusFolderPane(bool toggle) {
        var owner = Vm.Nav.CurrentSource == NavigationSource.Bookmark
            ? NavigationSource.Bookmark
            : NavigationSource.Drives;

        var target = owner;
        if (toggle) {
            target = ZoneOf(Keyboard.FocusedElement) switch {
                Zone.Bookmarks => NavigationSource.Drives,
                Zone.Drives => NavigationSource.Bookmark,
                // From anywhere else: the panel the folder was opened from,
                // falling back to the last panel the keyboard was in when it
                // was opened from neither.
                _ => Vm.Nav.CurrentSource is NavigationSource.Bookmark or NavigationSource.Drives
                    ? owner
                    : _lastFolderPane,
            };
        }

        // A panel with nothing in it (every default bookmark switched off,
        // and none added) is not somewhere to send the keyboard.
        if (target == NavigationSource.Bookmark && !BookmarksTree.HasItems) {
            target = NavigationSource.Drives;
        }

        if (target == NavigationSource.Bookmark) {
            // Put away, the tree is Collapsed and cannot take focus; a
            // shortcut that silently did nothing would read as broken.
            Vm.IsBookmarksExpanded = true;
            UpdateLayout();
        }

        _lastFolderPane = target;
        var tree = target == NavigationSource.Bookmark ? BookmarksTree : Tree;
        Vm.RevealCurrentIn(target);
        tree.UpdateLayout();
        FocusTree(tree);
    }


    /// <summary>
    /// Repaints the "you are here" outline. Hung off the window rather than
    /// the individual controls because focus can land anywhere, including on
    /// chrome that belongs to no zone at all.
    /// </summary>
    private void OnZoneFocusChanged(object sender, KeyboardFocusChangedEventArgs e) {
        var zone = ZoneOf(e.NewFocus);
        BookmarksFrame.BorderBrush = zone == Zone.Bookmarks ? _activeZoneBrush : Brushes.Transparent;
        Tree.BorderBrush = zone == Zone.Drives ? _activeZoneBrush : Brushes.Transparent;
        FileListZone.BorderBrush = zone == Zone.FileList ? _activeZoneBrush : Brushes.Transparent;

        // Arriving in a folder panel moves the operation target with the
        // keyboard, so the first Delete after Tab is about the folder the
        // user is looking at and not about the list they just left.
        if (zone == Zone.Bookmarks) {
            _lastFolderPane = NavigationSource.Bookmark;
            TargetTreeNode(BookmarksTree);
        } else if (zone == Zone.Drives) {
            _lastFolderPane = NavigationSource.Drives;
            TargetTreeNode(Tree);
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
        // Esc is not here: it abandons the edit for the whole strip, so it
        // lives in OnPreviewKeyDown, which tunnels through this box on its
        // way down and covers the breadcrumb buttons too.

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
        };

        // Remember the file type that was right-clicked. The "Добавить"
        // picker in settings leads with these — of the eight hundred
        // registered extensions, the five you were just working in are the
        // only ones with any claim to being first.
        // Only real file types: the picker's list is a list of extensions,
        // and "фон папки" is already one of the scopes the table always has.
        if (!isBackground) {
            vm.Settings.NoteMenuScope(ShellScopes.ExtensionOf(vm.SelectedEntry?.FullPath));
        }

        var session = QueryShellMenu(target, settings);
        if (session is not null) {
            // Opening a menu is the only way we learn which of the installed
            // handlers actually draw anything, so this is where the settings
            // table's "встречали" mark comes from. Keyed the same way the
            // blocklist is — verb first, label as the fallback.
            string scope = isBackground
                ? ShellScopes.DirectoryBackground
                : ShellScopes.ExtensionOf(vm.SelectedEntry?.FullPath) ?? ShellScopes.Directory;

            vm.Settings.NoteShellExtensions(session.Items
                .Where(item => !item.IsSeparator)
                .Select(item => new KnownShellEntry {
                    Key = ShellEntryKey.For(item.Verb, item.Header),
                    Title = ShellEntryKey.Normalize(item.Header),
                    Help = item.Help,
                    Scope = scope,
                }));
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

            [MenuCommandId.RestoreFromRecycleBin] = new(vm.RestoreFromRecycleBinCommand),

            [MenuCommandId.Properties] = new(vm.PropertiesCommand),
        };
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

        if (_treeClickNavigates || Vm.Settings.TreeKeyboardNavigates) {
            Vm.NavigateAndSelectFolder(node.FullPath, source);

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

        FileList.ClearSelection();
        Vm.SelectExternalPath(node.FullPath);
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
                Vm.NavigateAndSelectFolder(
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
            FileList.FocusList();
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

            Vm.NavigateAndSelectFolder(
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
        // Keep the system's Copy/Move/None cursor in addition to our
        // preview — except while the cursor is still over the list the drag
        // started in. There the system would draw the "no entry" sign,
        // which is a verdict on a gesture nobody has made yet: let go and
        // the file simply stays where it was. Plain arrow instead.
        if (IsNeutralDropTarget()) {
            e.UseDefaultCursors = false;
            Mouse.SetCursor(Cursors.Arrow);
            e.Handled = true;
        } else {
            e.UseDefaultCursors = true;
        }

        if (_dragPreview is null) {
            return;
        }
        _dragPreview.MoveToCursor();
        UpdatePreviewForCurrentTarget();
    }


    /// <summary>
    /// The cursor is over a drop surface that would refuse, but only
    /// because the target fell back to the folder already being listed —
    /// dragging across a file's own neighbours. Nothing is wrong, nothing
    /// is offered, and neither the cursor nor the plaque should say
    /// otherwise.
    /// </summary>
    private bool IsNeutralDropTarget() {
        return _drops.SelfDropReason != SelfDropReason.None
            && _drops.Target is not null
            && _drops.TargetIsFallback;
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
            // Nothing would happen on release. Three sub-cases:
            //  • Self-drop with a specific reason ("into own subfolder", …)
            //    *and* a folder the user actually aimed at — that reason is
            //    worth saying loudly, because they pointed at that folder.
            //  • The refusal is against the fallback target, i.e. the folder
            //    already being listed. Dragging a file across its own list
            //    passes over its neighbours, not over a drop target, and
            //    "… уже лежит в …" there is scolding someone for a gesture
            //    they have not made yet. The plaque stays — you have to be
            //    able to see what you picked up — but it only names it.
            //  • Nothing useful under the cursor (column header, scrollbar,
            //    splitter) — the system's no-drop cursor is enough, and a
            //    red "Cannot drop here" plaque hovering over a perfectly
            //    valid neighbouring folder is just noise. Hide the preview.
            if (IsNeutralDropTarget()) {
                ShowDragPreview();
                action = DragAction.None;
                desc = DescribeDragged(count);
            } else if (_drops.SelfDropReason != SelfDropReason.None && _drops.Target is not null) {
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
            desc = $"{verb} {DescribeDragged(count)}";
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

    /// <summary>What is in hand: the file's name, or how many of them.</summary>
    private string DescribeDragged(int count) {
        return count == 1
            ? string.Format(Strings.DragOneItem, _dragFirstName)
            : string.Format(Strings.DragItems, count);
    }

    private string FormatBookmarkDesc(int count) {
        return string.Format(Strings.DragAddToBookmarks, DescribeDragged(count));
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
                    Vm.Bookmarks.Add(p);
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
                    Vm.Bookmarks.Add(p);
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

        return paths.Any(p => Directory.Exists(p) && !Vm.Bookmarks.Contains(p));
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
