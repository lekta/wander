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
using Wander.App.DragPreview;
using Wander.App.Menu;
using Wander.App.Resources;
using Wander.App.Util;
using Wander.App.Views;
using Wander.Core;
using Wander.Core.FileSystem;
using Wander.Core.Layout;
using Wander.Core.Menu;
using Wander.Core.Navigation;
using Wander.Core.Persistence;
using Wander.Core.Shell;


namespace Wander.App;

public partial class MainWindow : Window {

    private MainViewModel Vm => (MainViewModel)DataContext;


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

    /// <summary>
    /// The drag currently leaving Wander — the plaque, the cursor and the
    /// wording. See <see cref="OutgoingDrag"/>; the window keeps only the
    /// gestures that start one.
    /// </summary>
    private OutgoingDrag _outgoing = null!;


    public MainWindow() {
        InitializeComponent();
        // Here rather than anywhere later: the window is shown by
        // StartupUri the moment the constructor returns, and ShowActivated
        // only counts before that. A smoke run must not take the keyboard
        // away from whoever is working on this desktop, and parking it
        // off-screen alone would not stop it doing that.
        App.ParkIfHeadless(this);

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

        using var self = System.Diagnostics.Process.GetCurrentProcess();
        double ms = (DateTime.Now - self.StartTime).TotalMilliseconds;
        ServiceLocator.Get<Wander.Core.Logging.ILogger>()
            .Info($"Startup: first frame {ms:F0} ms after process start");
    }


    // --- Window geometry persistence -----------------------------------

    private void OnSourceInitialized(object? sender, EventArgs e) {
        // A smoke run is parked off-screen on purpose — restoring the saved
        // geometry would drag it onto the desktop, and saving it on the way
        // out would leave the real session pointing at (-32000, -32000).
        if (App.Headless) {
            return;
        }

        RestoreWindowGeometry();
    }

    private void OnClosing(object? sender, CancelEventArgs e) {
        if (!App.Headless) {
            SaveWindowGeometry();
        }
        // The session state is saved on a debounce; whatever it is still
        // holding belongs to this session and goes out with it.
        Vm.FlushState();

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
        var state = ServiceLocator.Get<IAppStateStore>().Load();
        if (state.Window is not { } geom) {
            return;
        }

        // Restore size first: the clamp below is measured against the
        // width the window is about to have, not the one it has now.
        if (WindowPlacement.IsUsableSize(geom.Width, geom.Height)) {
            Width = geom.Width;
            Height = geom.Height;
        }

        var screen = new ScreenRect(
            SystemParameters.VirtualScreenLeft,
            SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth,
            SystemParameters.VirtualScreenHeight);
        (Left, Top) = WindowPlacement.Clamp(
            new ScreenRect(geom.Left, geom.Top, Width, Height), screen);
        WindowStartupLocation = WindowStartupLocation.Manual;

        if (geom.Maximized) {
            WindowState = WindowState.Maximized;
        }
    }

    private void SaveWindowGeometry() {
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
        _outgoing = new OutgoingDrag(_drops, () => FolderTrees.ClearBookmarkTarget());
        FolderTrees.Connect(_drops, _outgoing);
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
        var log = ServiceLocator.Get<Wander.Core.Logging.ILogger>();
        Wander.Core.Diagnostics.PerfLog.Start(log);
        Diagnostics.PerfCounters.Start(log);
        Diagnostics.SystemVitals.Start(log);
        Diagnostics.UiStallWatch.Start(Dispatcher);
        // Bubbling, so it sees focus landing anywhere in the window.
        GotKeyboardFocus += OnZoneFocusChanged;
        if (App.IsSmokeRun) {
            StartSmokeCountdown();
        }
        // Here rather than in the view model's constructor: a saved pane
        // size is a share of the window it was saved from, and this is the
        // first moment there is a window with a size to compare against.
        Vm.RestorePaneSizes(ActualWidth, ActualHeight);
        SizeChanged += (_, _) => Vm.NoteWindowSize(ActualWidth, ActualHeight);
        ApplyPreviewLayout();
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

        }
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

        // Ctrl+C with the keyboard in the preview pane and text selected
        // there copies the text, not the file. Handled here rather than
        // left to the text controls so that the status bar can say which of
        // the two happened: the pane and the list are one keystroke apart,
        // and a user who copied a paragraph and pasted a file would have no
        // way of knowing where it went wrong.
        if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control
            && Preview.TryCopySelectedText() is { } copied) {
            Vm.Status = string.Format(Strings.StatusTextCopied, copied);
            e.Handled = true;

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
            FocusZone(WindowZone.FileList);
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
        if (e.Key == Key.Escape && ZoneOf(Keyboard.FocusedElement) == WindowZone.Address) {
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
        if (e.Key == Key.Escape && ZoneOf(Keyboard.FocusedElement) is WindowZone.FileList or null) {
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
    // Which element in a zone can take the keyboard is the window's own
    // business and lives here; the order Tab walks the zones in and the
    // Ctrl+1 panel policy are in Wander.Core.Layout.WindowZones.

    /// <summary>
    /// Which folder panel Ctrl+1 opens when the current folder came from
    /// neither of them — the address bar, a double click, a restored
    /// session. The last one the keyboard was in wins.
    /// </summary>
    private WindowZone _lastFolderPane = WindowZone.Drives;


    /// <summary>
    /// Puts the keyboard back in the file list. Called after a modal dialog
    /// closes: WPF hands focus to the owner window and then picks whatever
    /// focusable element it finds first, which is nowhere the user was.
    /// </summary>
    public void FocusWorkArea() {
        FocusZone(WindowZone.FileList);
    }


    /// <summary>Which zone an element belongs to, or null for the chrome between them.</summary>
    private WindowZone? ZoneOf(object? source) {
        // The folder panels answer for themselves — which of the two a row
        // belongs to is the control's business, not the window's.
        if (FolderTrees.PaneOf(source) is { } pane) {
            return pane == NavigationSource.Bookmark ? WindowZone.Bookmarks : WindowZone.Drives;
        }

        foreach (var hit in ListVisuals.Ancestors(source)) {
            if (ReferenceEquals(hit, NavToolbar)) {
                return WindowZone.Toolbar;
            }
            if (ReferenceEquals(hit, AddressBar)) {
                return WindowZone.Address;
            }
            if (ReferenceEquals(hit, SearchBox)) {
                return WindowZone.Search;
            }
            if (ReferenceEquals(hit, FileListZone)) {
                return WindowZone.FileList;
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
        var from = ZoneOf(Keyboard.FocusedElement) ?? WindowZone.FileList;
        foreach (var zone in WindowZones.Ring(from, delta)) {
            if (FocusZone(zone)) {
                return;
            }
        }
    }


    /// <summary>Puts the keyboard in one zone. False when the zone cannot take it.</summary>
    private bool FocusZone(WindowZone zone) {
        switch (zone) {
            case WindowZone.Toolbar:
                foreach (UIElement child in NavToolbar.Children) {
                    if (child.Focusable && child.IsEnabled && child.Focus()) {
                        return true;
                    }
                }

                return false;

            case WindowZone.Address:
                // The strip turns into the editable path with everything
                // selected — what Explorer does on the same stop, and what
                // makes it useful rather than decorative.
                BeginAddressEdit();

                return true;

            case WindowZone.Search:
                return SearchBox.Focus();

            case WindowZone.Bookmarks:
                return FolderTrees.FocusBookmarks();

            case WindowZone.Drives:
                return FolderTrees.FocusDrives();

            case WindowZone.FileList:
                FileList.FocusList();

                return true;

            default:
                return false;
        }
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
        var target = WindowZones.FolderPane(
            toggle,
            ZoneOf(Keyboard.FocusedElement),
            PaneZone(Vm.Nav.CurrentSource),
            _lastFolderPane,
            FolderTrees.HasBookmarks);

        _lastFolderPane = target;
        FolderTrees.RevealAndFocus(PaneSource(target));
    }


    /// <summary>The zone a navigation came from, or null when it came from neither panel.</summary>
    private static WindowZone? PaneZone(NavigationSource? source) {
        return source switch {
            NavigationSource.Bookmark => WindowZone.Bookmarks,
            NavigationSource.Drives => WindowZone.Drives,
            _ => null,
        };
    }

    private static NavigationSource PaneSource(WindowZone zone) {
        return zone == WindowZone.Bookmarks ? NavigationSource.Bookmark : NavigationSource.Drives;
    }


    /// <summary>
    /// Repaints the "you are here" outline. Hung off the window rather than
    /// the individual controls because focus can land anywhere, including on
    /// chrome that belongs to no zone at all.
    /// </summary>
    private void OnZoneFocusChanged(object sender, KeyboardFocusChangedEventArgs e) {
        var zone = ZoneOf(e.NewFocus);
        FileListZone.BorderBrush = zone == WindowZone.FileList ? Palette.FocusOutline : Brushes.Transparent;
        FolderTrees.ShowFocusOutline(
            zone is WindowZone.Bookmarks or WindowZone.Drives ? PaneSource(zone.Value) : null,
            Palette.FocusOutline);

        // Arriving in a folder panel moves the operation target with the
        // keyboard, so the first Delete after Tab is about the folder the
        // user is looking at and not about the list they just left.
        if (zone is WindowZone.Bookmarks or WindowZone.Drives) {
            _lastFolderPane = zone.Value;
            FolderTrees.TargetSelected(PaneSource(zone.Value));
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
        if (Vm.Nav.IsEditingAddress || ListVisuals.IsInsideControl(e.OriginalSource)) {
            return;
        }

        BeginAddressEdit();
        e.Handled = true;
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
            IsArchive = vm.CurrentArchive is not null,
            // "Every one of them is an archive", not "one of them is": the
            // row extracts what is selected, and a mixed selection has no
            // single answer to what that would mean.
            SelectionIsArchive = !isBackground && vm.SelectionIsArchive,
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

            [MenuCommandId.Extract] = new(vm.ExtractCommand),

            [MenuCommandId.RestoreFromRecycleBin] = new(vm.RestoreFromRecycleBinCommand),

            [MenuCommandId.Properties] = new(vm.PropertiesCommand),
        };
    }






    // --- Drag source ----------------------------------------------------
    // The gesture that starts a drag belongs to whatever the user grabbed —
    // the file list or a tree row. Running it does not: see OutgoingDrag.

    private void FileList_DragStartRequested(object? sender, FileListDragRequest e) {
        _outgoing.Run(e.Source, e.Paths, e.Payload);
    }


    /// <summary>
    /// A folder panel targeted a folder: the row under the keyboard cursor,
    /// or the one that was right-clicked. The list gives up its selection
    /// for it, so exactly one highlighted set is on screen — that is what
    /// tells the user which of the two the next Delete is about.
    /// </summary>
    private void FolderTrees_FolderTargeted(object? sender, string path) {
        FileList.ClearSelection();
        Vm.SelectExternalPath(path);
    }

    private void FolderTrees_FocusListRequested(object? sender, EventArgs e) {
        FileList.FocusList();
    }

    private void FolderTrees_ContextMenuRequested(object? sender, FolderMenuRequest e) {
        ShowContextMenu(e.Host, PlacementMode.MousePoint, isBackground: false, folderPath: e.Folder);
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
        _drops.Execute(e, plan => Vm.HandleDrop(plan.Paths, plan.Target, plan.Effect));
    }



}
