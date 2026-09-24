using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Wander.App.Controllers;
using Wander.App.Dialogs;
using Wander.App.DragPreview;
using Wander.App.Menu;
using Wander.App.Resources;
using Wander.App.Util;
using Wander.App.ViewModels;
using Wander.App.Views;
using Wander.Core;
using Wander.Core.Actions;
using Wander.Core.FileSystem;
using Wander.Core.Layout;
using Wander.Core.Logging;
using Wander.Core.Menu;
using Wander.Core.Navigation;
using Wander.Core.Operations;
using Wander.Core.Panels;
using Wander.Core.Persistence;
using Wander.Core.Preview;
using Wander.Core.Shell;
using Wander.Core.Workspace;


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


    static MainWindow() {
        // The action trace: a menu is a window of its own, and a click in it
        // never reaches this window's mouse handlers.
        EventManager.RegisterClassHandler(typeof(MenuItem), MenuItem.ClickEvent, new RoutedEventHandler(TraceMenuItem));
    }

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
        // has to come back to this window anyway. The panels read their open
        // levels again then as well (P-24).
        Activated += (_, _) => {
            _justActivated = true;
            if (DataContext is MainViewModel vm) {
                vm.SyncClipboardFromSystem();
                vm.NoteWindowActivated();
            }
            MoveKeyboardOnceActive();
        };
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
        Log.Info($"Startup: first frame {ms:F0} ms after process start");
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
        // Operations still running: asked about, stopped and waited for
        // before anything below happens, with the window still up. A
        // Shutdown cannot be put off - WPF closes whatever the handler says
        // - so there they are stopped on the spot and the close goes on.
        if (Vm.HasActiveOperations && !_operationsStopped) {
            if (App.IsShuttingDown) {
                Log.Info($"Shutdown with {Vm.Operations.Count} operation(s) running: cancelled without waiting");
                App.AbandonOperations();
            } else {
                e.Cancel = true;
                if (!_stoppingOperations && ConfirmExitWithOperations()) {
                    StopOperationsThenClose();
                }

                return;
            }
        }

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
        // The handle on the folder on screen, and the browser processes of
        // both preview panes: none of them may outlive the window.
        ServiceLocator.TryGet<IDirectoryWatcher>()?.Watch(null);
        Preview.ReleaseWebView();
        _previewSecond?.ReleaseWebView();
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

    // --- Exit with operations running ----------------------------------

    /// <summary>How long an exit waits for cancelled operations to let go of their files.</summary>
    private static readonly TimeSpan _exitWait = TimeSpan.FromSeconds(10);

    /// <summary>The user said yes and the operations are winding down; another close changes nothing.</summary>
    private bool _stoppingOperations;

    /// <summary>The wait is over: the next close goes through without a question.</summary>
    private bool _operationsStopped;


    private bool ConfirmExitWithOperations() {
        return ServiceLocator.Get<IDialogs>().Ask(new DialogRequest(
            DialogKind.ExitWithOperations,
            Strings.ExitWithOperationsTitle,
            string.Format(Strings.ExitWithOperationsMessage, Vm.Operations.Count),
            DialogButtons.OkCancel,
            DialogIcon.Warning));
    }

    /// <summary>
    /// Every operation is cancelled and given <see cref="_exitWait"/> to
    /// finish, the window and the operation windows still on screen, so
    /// their cancelling state is seen. Whatever is still running after that
    /// has the programs it started killed. Then the close is made again.
    /// </summary>
    [SuppressMessage("ReSharper", "AsyncVoidMethod",
        Justification = "Runs off the Closing handler, which cannot await; every exception is caught and logged, and the close goes on.")]
    private async void StopOperationsThenClose() {
        _stoppingOperations = true;
        var log = Log.Current;
        try {
            var tracker = ServiceLocator.Get<OperationTracker>();
            var watch = System.Diagnostics.Stopwatch.StartNew();
            int count = Vm.CancelAllOperations();
            log.Info($"Exit requested with {count} operation(s) running");
            if (await tracker.WhenIdleAsync(_exitWait)) {
                log.Info($"Exit: operations idle after {watch.ElapsedMilliseconds} ms");
            } else {
                log.Warn($"Exit: {tracker.Snapshot().Count} operation(s) still running after {_exitWait.TotalSeconds:F0} s");
                ServiceLocator.Get<IProcessRunner>().KillAll();
            }
        } catch (Exception ex) {
            // async void: anything thrown here would be an unhandled
            // dispatcher exception on the way out. The close goes on.
            log.Error("Exit: stopping the operations failed", ex);
        } finally {
            _operationsStopped = true;
            // Posted: with nothing left to wait for, this is still inside
            // the Closing handler, where WPF refuses a Close. And a close
            // the user made meanwhile may already have gone through.
            _ = Dispatcher.BeginInvoke(new Action(() => {
                if (!_closingForReal) {
                    Close();
                }
            }));
        }
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
        _drops.HoverOpened += OnDragHoverOpened;
        _outgoing = new OutgoingDrag(_drops, () => FolderTrees.ClearBookmarkTarget());
        FolderTrees.Connect(_drops, _outgoing);
        // A drag leaving the window: the hover and the edge scroll stop.
        // DragLeave bubbles up from every element the cursor leaves inside
        // the window as well; only a point outside it is the drag gone.
        DragLeave += (_, e) => {
            var at = e.GetPosition(this);
            if (at.X < 0 || at.Y < 0 || at.X >= ActualWidth || at.Y >= ActualHeight) {
                _drops.EndHover();
            }
        };
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
        var log = Log.Current;
        Wander.Core.Diagnostics.PerfLog.Start(log);
        Diagnostics.PerfCounters.Start(log);
        Diagnostics.SystemVitals.Start(log);
        Diagnostics.UiStallWatch.Start(Dispatcher);
        // Bubbling, so it sees focus landing anywhere in the window.
        GotKeyboardFocus += OnZoneFocusChanged;
        Vm.Workspace.ViewEffectRequested += OnViewEffectRequested;
        if (App.IsSmokeRun) {
            StartSmokeCountdown();
        }
        // Here rather than in the view model's constructor: a saved pane
        // size is a share of the window it was saved from, and this is the
        // first moment there is a window with a size to compare against.
        Vm.RestorePaneSizes(ActualWidth, ActualHeight);
        SizeChanged += (_, _) => Vm.NoteWindowSize(ActualWidth, ActualHeight);
        // And once more after the first paint, against the size the window
        // has settled at: at Loaded a window restored maximized is still at
        // its normal bounds (1762x700 there, 2062x1118 here, by the two
        // "Pane sizes" lines in the log), and the panes scaled to those
        // stayed short. Recomputed from the saved pair, so for the same
        // size it is the same answer.
        ContentRendered += (_, _) => Vm.RestorePaneSizes(ActualWidth, ActualHeight);
        ApplyPreviewLayout();
        ApplyFoldersLayout();
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
            App.ShutdownWithoutAsking(0);
        };
        timer.Start();
    }


    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e) {
        switch (e.PropertyName) {
            case nameof(MainViewModel.IsPreviewVisible):
            case nameof(MainViewModel.PreviewWidth):
                ApplyPreviewLayout();
                break;

            case nameof(MainViewModel.IsPreviewSplit):
                ApplyPreviewSplit();
                break;

            case nameof(MainViewModel.PreviewPairShapes):
                // Raised for each new pair once its shapes are in.
                _zoomLink?.Reset();
                ApplyPreviewSplitOrientation();
                break;

            case nameof(MainViewModel.IsFoldersVisible):
            case nameof(MainViewModel.FoldersWidth):
                ApplyFoldersLayout();
                break;
        }
    }

    /// <summary>
    /// The folders pane: whether it is on screen, and how wide. Put away,
    /// both its column and the divider's go to zero and the control is
    /// collapsed, so neither Tab nor a drop can land in it; the width it
    /// had waits in the view model for the next time it is shown.
    /// </summary>
    private void ApplyFoldersLayout() {
        if (Vm.IsFoldersVisible) {
            FoldersColumn.Width = new GridLength(Vm.FoldersWidth);
            FoldersSplitterColumn.Width = new GridLength(4);
            FolderTrees.Visibility = Visibility.Visible;
        } else {
            // The keyboard must not vanish with the pane: a collapsed
            // element cannot hold focus, and WPF would drop it on the
            // window, where the next arrow key does nothing. Told before
            // the pane goes; the model sends the keyboard on (K-8).
            Vm.Workspace.Post(new PaneHidden(new[] { WindowZone.Bookmarks, WindowZone.Drives }));
            FoldersColumn.Width = new GridLength(0);
            FoldersSplitterColumn.Width = new GridLength(0);
            FolderTrees.Visibility = Visibility.Collapsed;
        }
    }

    private void FoldersSplitter_DragCompleted(object sender, DragCompletedEventArgs e) {
        Vm.FoldersWidth = FoldersColumn.ActualWidth;
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
            PreviewSplit.Visibility = Visibility.Visible;
        } else {
            PreviewSplitterColumn.Width = new GridLength(0);
            PreviewColumn.Width = new GridLength(0);
            // Collapsed as well as zero-width: a WebView2 in a column of
            // no width still composes, a collapsed one does not.
            PreviewSplit.Visibility = Visibility.Collapsed;
        }
    }

    private void PreviewSplitter_DragCompleted(object sender, DragCompletedEventArgs e) {
        Vm.PreviewWidth = PreviewColumn.ActualWidth;
    }


    // --- Split preview -----------------------------------------------------

    private Views.PreviewPane? _previewSecond;

    /// <summary>The two halves' held-button zoom, tied; a new pair lines up afresh.</summary>
    private ZoomLink? _zoomLink;

    /// <summary>How the split on screen is turned; null while there is none - see <see cref="ApplyPreviewSplitOrientation"/>.</summary>
    private bool? _splitStacked;

    /// <summary>
    /// Two panes or one. The second control is made on the first pair and
    /// kept for the session; between pairs it is collapsed, and its
    /// controller has already let go of the file.
    /// </summary>
    private void ApplyPreviewSplit() {
        // Another pair, or none: whatever lined the last one up is theirs.
        _zoomLink?.Reset();
        if (!Vm.IsPreviewSplit) {
            Preview.ShowSecond(null, stacked: true);
            _splitStacked = null;

            return;
        }

        if (_previewSecond is null) {
            _previewSecond = new Views.PreviewPane { DataContext = Vm.PreviewSecond };
            // Zoom on either half looks at the same place of the other
            // picture, while the split is on screen.
            _zoomLink = Views.PreviewPane.Link(Preview, _previewSecond, () => Vm.IsPreviewSplit);
        }
        ApplyPreviewSplitOrientation();
    }

    /// <summary>
    /// One above the other or side by side, whichever shows the two pictures
    /// bigger in the room they share - the pane above the footer
    /// (SplitOrientation, 2026-09-23). Two photographs across a 280-px strip
    /// are two thumbnails; one above the other they are two photographs -
    /// and two portraits in a pane dragged wide are bigger side by side.
    /// Pictures of unknown shape count as square: the old rule, stacked
    /// while the pane is taller than it is wide. A split on screen turns
    /// over only when the other way is clearly bigger.
    /// </summary>
    private void ApplyPreviewSplitOrientation() {
        if (!Vm.IsPreviewSplit || _previewSecond is null) {
            return;
        }

        var room = Preview.PairArea();
        var (first, second) = Vm.PreviewPairShapes;
        bool stacked = SplitOrientation.Stacked(room.Width, room.Height, first, second, _splitStacked);
        _splitStacked = stacked;
        Preview.ShowSecond(_previewSecond, stacked);
    }

    private void PreviewSplit_SizeChanged(object sender, SizeChangedEventArgs e) {
        ApplyPreviewSplitOrientation();
    }

    /// <summary>Text selected in either half of the preview, whichever has the keyboard.</summary>
    private int? TryCopyPreviewText() {
        return Preview.TryCopySelectedText() ?? _previewSecond?.TryCopySelectedText();
    }

    /// <summary>The keyboard is in a code viewer - either pane's.</summary>
    private bool IsCodeEditorFocused =>
        Preview.IsCodeEditorFocused || _previewSecond?.IsCodeEditorFocused == true;


    // --- Full screen (PLAN Q5) ----------------------------------------------

    /// <summary>
    /// Enter or Space on pictures in the gallery (<see cref="FullscreenPlan"/>).
    /// The window walks on its own; closed, it leaves the keyboard back in
    /// the list - on the picture a single one ended on; a pair or a walked
    /// selection leaves the selection as it was.
    /// </summary>
    private void FileList_FullscreenRequested(object? sender, FullscreenPlan plan) {
        var window = Views.FullscreenWindow.Open(plan, Vm, this);
        window.Closed += (_, _) => {
            if (plan.Mode == FullscreenMode.Single
                && !string.Equals(window.Current.FullPath, plan.Start.FullPath, StringComparison.OrdinalIgnoreCase)) {
                Vm.RevealPath(window.Current.FullPath);
            }
            FileList.FocusList();
        };
    }


    // --- Global hotkeys not bound to commands ---------------------------

    // Alt is held and the review helpers are off the picture meanwhile.
    private bool _peeking;


    protected override void OnPreviewKeyUp(KeyEventArgs e) {
        base.OnPreviewKeyUp(e);
        if (_peeking && e.Key == Key.System && e.SystemKey is Key.LeftAlt or Key.RightAlt) {
            StopPeeking();
            // Handled, or letting go would put the window into menu mode:
            // the toolbar holds a real Menu, and Alt alone is its key.
            e.Handled = true;
        }
    }


    /// <summary>Alt+Tab with Alt held: the key-up lands in another window, so the marks would stay off.</summary>
    protected override void OnDeactivated(EventArgs e) {
        base.OnDeactivated(e);
        StopPeeking();
        (DataContext as MainViewModel)?.NoteWindowDeactivated();
    }


    private void StopPeeking() {
        if (_peeking) {
            _peeking = false;
            Vm.Helpers.SetPeek(false);
        }
    }


    protected override void OnPreviewKeyDown(KeyEventArgs e) {
        base.OnPreviewKeyDown(e);
        TraceKey(e);
        if (e.Handled) {
            return;
        }

        // While a name is being edited - in the list or in a folder panel -
        // the editor owns the keyboard, and the window's own shortcuts do
        // not apply, the same as anywhere else in Windows while a text
        // field has focus. This is a tunnelling handler, so without the
        // guard it runs *before* the editor's: Esc cleared the whole
        // selection here and only then reached the editor to cancel.
        if (Vm.RenamingPath is not null || FolderTrees.IsRenaming) {
            return;
        }

        // Ctrl+C with the keyboard in the preview pane and text selected
        // there copies the text, not the file. Handled here rather than
        // left to the text controls so that the status bar can say which of
        // the two happened: the pane and the list are one keystroke apart,
        // and a user who copied a paragraph and pasted a file would have no
        // way of knowing where it went wrong.
        if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control
            && TryCopyPreviewText() is { } copied) {
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

        // Alt by itself, with a review helper on: the marks come off the
        // picture for as long as it is held - "and how does it look without
        // them". Not handled here, so the chords below still work; the
        // release is (see OnPreviewKeyUp).
        if (e.Key == Key.System && e.SystemKey is Key.LeftAlt or Key.RightAlt
            && !_peeking && Vm.Helpers.AnyOn) {
            _peeking = true;
            Vm.Helpers.SetPeek(true);
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

        // Ctrl+Shift+F: the search window, with its own criteria - skipped
        // inside the code preview.
        // Ctrl+F: the find field of the preview when the keyboard is in a
        // pane showing text (the code preview included); otherwise the box
        // in the toolbar, which is the quick filter.
        if (e.Key == Key.F && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift)) {
            if (!IsCodeEditorFocused) {
                OpenSearchWindow();
                e.Handled = true;

                return;
            }
        }

        if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control) {
            // The keyboard in a pane showing text: find in that text
            // (PLAN B6), not a filter over the list.
            if (Preview.OpenFind() || _previewSecond?.OpenFind() == true) {
                e.Handled = true;

                return;
            }
            if (!IsCodeEditorFocused) {
                SearchBox.Focus();
                SearchBox.SelectAll();
                e.Handled = true;

                return;
            }
        }

        if (e.Key == Key.F2 && CanStartRename(null)) {
            StartRename(null);
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
    /// Why the keyboard is about to move, when the window moves it itself -
    /// Tab, Ctrl+1, Ctrl+Shift+E, the model sending it somewhere: set just
    /// before the focus call, taken by the focus change it causes
    /// (<see cref="ReasonFor"/>).
    /// </summary>
    private ZoneReason? _pendingReason;

    /// <summary>The window was just activated: the next focus change is WPF putting the keyboard back.</summary>
    private bool _justActivated;

    /// <summary>
    /// The last keyboard move the model asked for while the window was not
    /// the active one - carried out once it is (K-2): a window in the
    /// background cannot take the keyboard, and WPF puts back what it had
    /// when it comes to the front.
    /// </summary>
    private WorkspaceEffect? _moveWhenActive;


    /// <summary>
    /// What the model asks of the controls: the keyboard into a zone or onto
    /// a line (KeyboardRules) - its arrival told with the model's reason, a
    /// dialog's return or the application putting it there; the list's
    /// selection put back after its rows landed; the name editor on a row.
    /// </summary>
    private void OnViewEffectRequested(WorkspaceEffect effect) {
        switch (effect) {
            case ApplyListSelection apply:
                FileList.ApplySelection(apply.List, apply.Scroll);
                break;
            case OpenEditor editor:
                FileList.OpenEditor(editor.Path);
                break;
            // Not headless: the harness's window is never the active one, and
            // the keyboard moving inside it is what its steps look at.
            case Core.Workspace.FocusZone or FocusRow when !IsActive && !App.Headless:
                _moveWhenActive = effect;
                break;
            case FocusZone zone:
                _pendingReason = zone.Reason;
                FocusZone(zone.Zone);
                _pendingReason = null;
                break;
            case FocusRow row:
                _pendingReason = ZoneReason.Programmatic;
                if (row.Surface == WindowZone.FileList) {
                    FileList.FocusRow(row.Path, row.Scroll);
                } else {
                    FolderTrees.FocusRow(row.Surface == WindowZone.Bookmarks ? Pane.Bookmarks : Pane.Drives, row.Path);
                }
                _pendingReason = null;
                break;
        }
    }

    /// <summary>
    /// The keyboard move that waited for the window to come to the front -
    /// after WPF's own putting back of what the keyboard had, which would
    /// otherwise land on top of it. Brought to the front by a click, the
    /// click says where the keyboard goes, and the move is dropped.
    /// </summary>
    private void MoveKeyboardOnceActive() {
        if (_moveWhenActive is not { } waiting) {
            return;
        }

        _moveWhenActive = null;
        if (Mouse.LeftButton == MouseButtonState.Pressed || Mouse.RightButton == MouseButtonState.Pressed) {
            return;
        }

        Dispatcher.BeginInvoke(DispatcherPriority.Input, () => OnViewEffectRequested(waiting));
    }


    /// <summary>Which zone an element belongs to, or null for the chrome between them. Internal: the harness asks it too (assert-focus).</summary>
    internal WindowZone? ZoneOf(object? source) {
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
            _pendingReason = ZoneReason.Tab;
            bool took = FocusZone(zone);
            _pendingReason = null;
            if (took) {
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
        // Put away, the pane cannot take the keyboard, and a shortcut that
        // silently did nothing would read as broken - so it comes back
        // first, the way collapsed bookmarks unfold for the same key.
        if (!Vm.IsFoldersVisible) {
            Vm.IsFoldersVisible = true;
            UpdateLayout();
        }

        var target = WindowZones.FolderPane(
            toggle,
            ZoneOf(Keyboard.FocusedElement),
            PaneZone(Vm.Nav.CurrentSource),
            _lastFolderPane,
            FolderTrees.HasBookmarks);

        _lastFolderPane = target;
        // Where the panel's cursor lands is the model's answer to the
        // keyboard arriving for this reason. The keyboard may not move at
        // all - Ctrl+Shift+E pressed in the panel it opens - and then the
        // reason is told without a focus change to carry it.
        var reason = toggle ? ZoneReason.PanelKey : ZoneReason.RevealKey;
        _pendingReason = reason;
        FolderTrees.RevealAndFocus(PaneSource(target));
        if (_pendingReason is not null) {
            _pendingReason = null;
            Vm.NoteKeyboardZone(target, reason);
        }
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
    /// Repaints the "you are here" outline and tells the view model where
    /// the keyboard is. Hung off the window rather than the individual
    /// controls because focus can land anywhere, including on chrome that
    /// belongs to no zone at all.
    ///
    /// <para>
    /// The zone is one of the facts the operation target is read from
    /// (TargetRules), never a trigger that moves it: the keyboard arriving
    /// in a panel takes nothing away from the list, and coming back from a
    /// context menu changes nothing - the menu's items run on the menu's
    /// own snapshot (MenuContext), whenever WPF gets round to running them.
    /// </para>
    /// </summary>
    private void OnZoneFocusChanged(object sender, KeyboardFocusChangedEventArgs e) {
        var zone = ZoneOf(e.NewFocus);
        if (Log.Details && ZoneOf(e.OldFocus) is var was && was != zone) {
            Log.Detail($"Focus: {ZoneName(was, e.OldFocus)} -> {ZoneName(zone, e.NewFocus)}");
        }
        FileListZone.BorderBrush = zone == WindowZone.FileList ? Palette.FocusOutline : Brushes.Transparent;
        FolderTrees.ShowFocusOutline(
            zone is WindowZone.Bookmarks or WindowZone.Drives ? PaneSource(zone.Value) : null,
            Palette.FocusOutline);

        if (zone is WindowZone.Bookmarks or WindowZone.Drives) {
            _lastFolderPane = zone.Value;
        }

        // A move inside one zone - the arrows of a panel, a line focused as
        // the cursor is drawn - is nothing new to the model; an arrival is,
        // and so is the window moving the keyboard for a reason of its own.
        var reason = ReasonFor(e);
        if (zone != Vm.Workspace.State.Keyboard.Zone
            || reason is ZoneReason.Tab or ZoneReason.PanelKey or ZoneReason.RevealKey or ZoneReason.FocusFell) {
            Vm.NoteKeyboardZone(zone, reason);
        }
    }

    /// <summary>
    /// Why the keyboard came to where it is (REDESIGN 4.4): the reason the
    /// window gave before moving it; the element that had it taken away -
    /// WPF hands the keyboard to the nearest focusable thing that is left,
    /// the list around a row, or the window; a mouse button down - a click;
    /// coming out of a menu; the window's activation putting it back.
    /// Anything else is unknown, and an unknown reason moves nothing in the
    /// model.
    /// </summary>
    private ZoneReason ReasonFor(KeyboardFocusChangedEventArgs e) {
        bool activated = _justActivated;
        _justActivated = false;
        if (_pendingReason is { } pending) {
            _pendingReason = null;

            return pending;
        }
        if (e.NewFocus is Window || (e.OldFocus is Visual old && PresentationSource.FromVisual(old) is null)) {
            return ZoneReason.FocusFell;
        }
        if (Mouse.LeftButton == MouseButtonState.Pressed || Mouse.RightButton == MouseButtonState.Pressed) {
            return ZoneReason.Click;
        }
        if (ListVisuals.Ancestors(e.OldFocus).Any(hit => hit is System.Windows.Controls.ContextMenu or MenuItem)) {
            return ZoneReason.MenuReturn;
        }

        return activated ? ZoneReason.Activation : ZoneReason.Unknown;
    }


    // --- Address bar ----------------------------------------------------

    /// <summary>
    /// Switches the address strip from breadcrumbs to the editable path and
    /// puts the caret in it. The TextBox is Collapsed until the flag flips;
    /// the binding shows it at once, and laid out here it takes the keyboard
    /// at once too - a Tab, Alt+D and the model all see where it went. Only
    /// a box that still refuses it gets it once the layout has run.
    /// </summary>
    private void BeginAddressEdit() {
        Vm.Nav.IsEditingAddress = true;
        AddressBox.UpdateLayout();
        if (AddressBox.Focus()) {
            AddressBox.SelectAll();

            return;
        }

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
        ShowContextMenu(e.Host, e.Placement, Vm.MenuContextForList(e.IsBackground));
    }

    /// <summary>
    /// Builds and opens a context menu about <paramref name="context"/> -
    /// the snapshot taken as it opens. Every item runs with that snapshot as
    /// its parameter, and while the menu is open its subject is the target
    /// the rest of the window describes - a panel row it is about is framed.
    /// </summary>
    private void ShowContextMenu(FrameworkElement host, PlacementMode placement, MenuContext context) {
        if (_contextMenus is null) {
            return;
        }

        var vm = Vm;
        var settings = vm.MenuSettings;
        var target = MenuTarget(context);
        bool isBackground = context.Subject.Kind == TargetKind.Background;
        string? primary = context.Subject.Primary?.FullPath;

        // Remember the file type that was right-clicked. The "Добавить"
        // picker in settings leads with these — of the eight hundred
        // registered extensions, the five you were just working in are the
        // only ones with any claim to being first.
        // Only real file types: the picker's list is a list of extensions,
        // and "фон папки" is already one of the scopes the table always has.
        if (!isBackground) {
            vm.Settings.NoteMenuScope(ShellScopes.ExtensionOf(primary));
        }

        var session = QueryShellMenu(target, settings);
        if (session is not null) {
            // Opening a menu is the only way we learn which of the installed
            // handlers actually draw anything, so this is where the settings
            // table's "встречали" mark comes from. Keyed the same way the
            // blocklist is — verb first, label as the fallback.
            string scope = isBackground
                ? ShellScopes.DirectoryBackground
                : ShellScopes.ExtensionOf(primary) ?? ShellScopes.Directory;

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

        var menu = _contextMenus.Build(model, session, context);
        menu.DataContext = vm;
        menu.PlacementTarget = host;
        menu.Placement = placement;
        menu.Opened += (_, _) => vm.NoteMenuOpened(context);
        menu.Closed += (_, _) => vm.NoteMenuClosed(context);
        menu.IsOpen = true;
    }

    /// <summary>What a menu about <paramref name="context"/> is built from: the snapshot, plus the settings it does not hold.</summary>
    private ContextMenuTarget MenuTarget(MenuContext context, MenuPlace place = MenuPlace.Context) {
        var vm = Vm;

        return context.ToMenuTarget(vm.CurrentPath, place) with {
            Actions = vm.Settings.Actions,
            MissingTools = vm.MissingTools,
            ShowDebug = vm.Settings.ShowDebugMenu,
        };
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
    /// <c>F2</c> and "Rename", from the key or a menu: on the surface the
    /// target is on (TargetRules.Rename) - the list's row, a panel's row
    /// under its cursor or right-clicked. One item is edited in place; two
    /// or more go to the batch window, which refuses a mix of files and
    /// folders on its own.
    /// </summary>
    private void StartRename(object? parameter) {
        var vm = Vm;
        var target = vm.ResolveTarget(parameter).Target;
        switch (TargetRules.Rename(target)) {
            case RenameRoute.ListBatch:
                vm.BatchRenameCommand.Execute(parameter);
                break;
            case RenameRoute.ListRow:
                FileList.StartRename();
                break;
            case RenameRoute.PanelRow:
                FolderTrees.StartRename(target.Pane, target.Folder!);
                break;
        }
    }

    private bool CanStartRename(object? parameter) {
        var (target, place) = Vm.ResolveTarget(parameter);
        if (place.IsReadOnly) {
            return false;
        }

        return TargetRules.Rename(target) switch {
            RenameRoute.ListRow or RenameRoute.ListBatch => true,
            RenameRoute.PanelRow => FolderTrees.CanRename(target.Pane, target.Folder!),
            _ => false,
        };
    }


    /// <summary>
    /// Maps every built-in menu id onto the command that runs it. Most come
    /// straight off the ViewModel; Rename is the exception - it opens an
    /// editor in the view, over a row of the list or of a folder panel
    /// (<see cref="StartRename"/>). A menu hands each command its snapshot
    /// (MenuCall); a key runs the same command with none.
    /// </summary>
    private Dictionary<MenuCommandId, MenuBinding> BuildMenuBindings() {
        var rename = new RelayCommand(StartRename, CanStartRename);
        var vm = Vm;

        return new Dictionary<MenuCommandId, MenuBinding> {
            [MenuCommandId.Open] = new(vm.OpenCommand),
            [MenuCommandId.OpenWith] = new(vm.OpenWithCommand),
            [MenuCommandId.OpenInTerminal] = new(vm.OpenInTerminalCommand),

            [MenuCommandId.Cut] = new(vm.CutCommand),
            [MenuCommandId.Copy] = new(vm.CopyCommand),
            // A menu's Paste goes into the one folder its row is, when it
            // is one (TargetRules.PasteFolder); Ctrl+V into the open folder.
            [MenuCommandId.Paste] = new(vm.PasteCommand),
            [MenuCommandId.CopyPath] = new(vm.CopyPathCommand),
            [MenuCommandId.CopyName] = new(vm.CopyNameCommand),
            [MenuCommandId.CreateShortcut] = new(vm.CreateShortcutCommand),

            [MenuCommandId.Rename] = new(rename),
            [MenuCommandId.BatchRename] = new(vm.BatchRenameCommand),
            [MenuCommandId.Delete] = new(vm.DeleteCommand),
            [MenuCommandId.NewFolder] = new(vm.NewFolderCommand),

            [MenuCommandId.Extract] = new(vm.ExtractCommand),
            [MenuCommandId.ExtractHere] = new(vm.ExtractHereCommand),

            [MenuCommandId.RestoreFromRecycleBin] = new(vm.RestoreFromRecycleBinCommand),

            [MenuCommandId.Properties] = new(vm.PropertiesCommand),

            // The row carries the action's id as its argument.
            [MenuCommandId.RunAction] = new(vm.RunActionCommand),
            [MenuCommandId.RunActionTo] = new(vm.RunActionToFolderCommand),
            [MenuCommandId.ConfigureActions] = new(vm.ConfigureActionsCommand),
        };
    }


    // --- Operations menu --------------------------------------------------
    // The header's "Operations": the context menu's builder in its header
    // shape, so a row hidden in settings is gone from both.

    private void OperationsMenu_SubmenuOpened(object sender, RoutedEventArgs e) {
        // Its own submenus raise the same event on the way up, and
        // rebuilding then would pull the rows out from under the cursor.
        if (!ReferenceEquals(e.OriginalSource, OperationsMenu)) {
            return;
        }

        RebuildOperationsMenu();
    }

    /// <summary>
    /// Fills the menu for the target as it is at the moment it opens - the
    /// list's rows, or a panel's row when the keyboard was there; the open
    /// folder when there is nothing. The shell is not asked: the header
    /// offers Wander's own verbs only.
    /// </summary>
    private void RebuildOperationsMenu() {
        if (_contextMenus is null) {
            return;
        }

        var vm = Vm;
        var context = vm.MenuContextFor(vm.Target);
        _contextMenus.Populate(OperationsMenu.Items, ContextMenuBuilder.Build(MenuTarget(context, MenuPlace.Header), vm.MenuSettings), context);
    }






    // --- Drag source ----------------------------------------------------
    // The gesture that starts a drag belongs to whatever the user grabbed —
    // the file list or a tree row. Running it does not: see OutgoingDrag.

    private void FileList_DragStartRequested(object? sender, FileListDragRequest e) {
        _outgoing.Run(e.Source, e.Paths, e.Payload, e.RightButton);
    }


    private void FolderTrees_FocusListRequested(object? sender, EventArgs e) {
        FileList.FocusList();
    }

    private void FolderTrees_ContextMenuRequested(object? sender, FolderMenuRequest e) {
        ShowContextMenu(e.Host, e.Placement, Vm.MenuContextFor(Target.OfPanelRow(e.Pane, e.Folder)));
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
        _drops.Execute(
            e,
            plan => Vm.HandleDrop(plan.Paths, plan.Target, plan.Effect),
            plan => ShowDropMenu((FrameworkElement)sender, plan));
    }

    private void FolderTrees_DropMenuRequested(object? sender, DropMenuRequest e) {
        ShowDropMenu(e.Host, e.Plan);
    }

    /// <summary>
    /// A drag held long enough over a place to go deeper (U1): a closed panel
    /// line opens - the model's own chevron - and a folder of the list is
    /// gone into, as a double click there would.
    /// </summary>
    private void OnDragHoverOpened(DragHoverDecision decision, FrameworkElement element) {
        Log.Detail($"Drag: held over {decision.Path} - {decision.Outcome}");
        switch (decision.Outcome) {
            case DragHoverOutcome.Expand when FolderTrees.PaneOf(element) is { } source:
                Vm.Workspace.Post(new ChevronToggled(
                    source == NavigationSource.Bookmark ? Pane.Bookmarks : Pane.Drives, decision.Path!, Open: true, All: false));
                break;
            case DragHoverOutcome.Enter:
                Vm.NavigateTo(decision.Path!, NavigationSource.RightPane);
                break;
        }
    }

    /// <summary>
    /// The menu of a drop held by the right mouse button, Explorer's
    /// gesture: copy, move or a shortcut - the one a left-button drop would
    /// have done in bold - and the catalog actions with an output, sent
    /// into the folder dropped on. Opened once the drag loop has returned:
    /// a popup raised from inside the drop callback competes with the drag
    /// for the mouse. Built fresh per drop, because its rows close over
    /// that drop's paths and folder.
    /// </summary>
    [SuppressMessage("ReSharper", "AsyncVoidMethod",
        Justification = "Posted from the drop callback, nothing awaits it. It runs on the dispatcher, so an exception lands in App.HookCrashLogging (DispatcherUnhandledException): logged and offered as a report, not fatal.")]
    private async void ShowDropMenu(FrameworkElement host, DropPlan plan) {
        var target = await Vm.DescribeDropAsync(plan.Paths, plan.Target, plan.Effect == DropEffect.Move);
        var bindings = new Dictionary<MenuCommandId, MenuBinding> {
            [MenuCommandId.DropCopyHere] = new(new RelayCommand(() => Vm.HandleDrop(plan.Paths, plan.Target, DropEffect.Copy))),
            [MenuCommandId.DropMoveHere] = new(new RelayCommand(() => Vm.HandleDrop(plan.Paths, plan.Target, DropEffect.Move))),
            [MenuCommandId.DropLinkHere] = new(new RelayCommand(() => Vm.HandleDrop(plan.Paths, plan.Target, DropEffect.Link))),
            // Over the primaries the menu was built for, not the payload:
            // a RAW's sidecars go along with a copy, never into a convert.
            [MenuCommandId.RunActionTo] = new(new RelayCommand(id => Vm.RunActionOnDropped(id as string, target.Paths, plan.Target))),
            [MenuCommandId.DropCancel] = new(new RelayCommand(() => { })),
        };

        var menu = new ContextMenuFactory(bindings, () => { }).Build(DropMenuBuilder.Build(target), session: null);
        menu.DataContext = Vm;
        menu.PlacementTarget = host;
        menu.Placement = PlacementMode.MousePoint;
        menu.IsOpen = true;
    }


    // --- Action trace (AppSettings.LogActions) ---------------------------
    // Keys, clicks, menu items and the keyboard moving between zones, into
    // the session log - for chasing a bug by what the user did. Nothing
    // here decides anything; with the switch off it is one flag read per
    // press. Paths in the lines are masked like every other (Log).

    protected override void OnPreviewMouseDown(MouseButtonEventArgs e) {
        base.OnPreviewMouseDown(e);
        if (Log.Details) {
            string times = e.ClickCount > 1 ? $" x{e.ClickCount}" : "";
            Log.Detail($"Click: {e.ChangedButton}{times} in {ZoneName(ZoneOf(e.OriginalSource), e.OriginalSource)} on {ClickTarget(e.OriginalSource)}");
        }
    }


    /// <summary>
    /// A key: the chord and where the keyboard is. A held key once, not at
    /// the repeat rate; a lone modifier not at all. What is typed - into a
    /// field, or a letter jumping to a name in the list - is only "typed"
    /// unless real paths are on, because it spells names; a digit on the
    /// list is a rating and says which.
    /// </summary>
    private void TraceKey(KeyEventArgs e) {
        if (!Log.Details || e.IsRepeat) {
            return;
        }

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt
            or Key.LWin or Key.RWin or Key.ImeProcessed or Key.DeadCharProcessed) {
            return;
        }

        var focused = Keyboard.FocusedElement;
        bool inField = focused is TextBoxBase;
        bool plain = (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Windows)) == 0;
        bool letter = key is (>= Key.A and <= Key.Z) or (>= Key.Oem1 and <= Key.Oem102) or Key.Space;
        bool digit = key is (>= Key.D0 and <= Key.D9) or (>= Key.NumPad0 and <= Key.NumPad9);
        string name = !Log.RevealPaths && plain && (letter || (digit && inField)) ? "(typed)" : key.ToString();

        Log.Detail($"Key: {Chord(Keyboard.Modifiers)}{name} in {ZoneName(ZoneOf(focused), focused)}{(inField ? ", text field" : "")}");
    }

    private static void TraceMenuItem(object sender, RoutedEventArgs e) {
        if (Log.Details && sender is MenuItem item) {
            Log.Detail($"Menu: {item.Header as string ?? (item.Header as TextBlock)?.Text ?? Named(item)}");
        }
    }

    private static string Chord(ModifierKeys modifiers) {
        return (modifiers.HasFlag(ModifierKeys.Control) ? "Ctrl+" : "")
            + (modifiers.HasFlag(ModifierKeys.Shift) ? "Shift+" : "")
            + (modifiers.HasFlag(ModifierKeys.Alt) ? "Alt+" : "")
            + (modifiers.HasFlag(ModifierKeys.Windows) ? "Win+" : "");
    }

    /// <summary>A zone by name, or the kind of element the keyboard is on outside every zone - a menu, the window itself.</summary>
    private static string ZoneName(WindowZone? zone, object? element) {
        return zone?.ToString() ?? element?.GetType().Name ?? "nowhere";
    }

    /// <summary>
    /// What a click landed on: a row of the list or a folder of a panel by
    /// its path, a button or a field (and whose row it is in), otherwise
    /// the nearest named element.
    /// </summary>
    private static string ClickTarget(object source) {
        FrameworkElement? control = null;
        FrameworkElement? named = null;
        object? item = null;
        foreach (var hit in ListVisuals.Ancestors(source)) {
            if (hit is ScrollBar) {
                return "scroll bar";
            }
            if (control is null && hit is ButtonBase or MenuItem or TextBoxBase) {
                control = (FrameworkElement)hit;
            }
            if (item is null && hit is FrameworkElement { DataContext: FileSystemEntry or TreeNodeViewModel } row) {
                item = row.DataContext;
            }
            if (named is null && hit is FrameworkElement { Name.Length: > 0 } element && !element.Name.StartsWith("PART_", StringComparison.Ordinal)) {
                named = element;
            }
        }

        string? what = control switch {
            TextBoxBase => "text field",
            { } button => $"button {Named(button)}",
            null => null,
        };
        string? where = item switch {
            FileSystemEntry entry => $"row {entry.FullPath}",
            TreeNodeViewModel folder => $"folder {folder.FullPath}",
            _ => null,
        };

        if (what is not null) {
            return where is null ? what : $"{what} of {where}";
        }

        return where ?? named?.Name ?? source.GetType().Name;
    }

    /// <summary>A control in a word: its name, else its tooltip, else its command, else its kind.</summary>
    private static string Named(FrameworkElement control) {
        if (control.Name.Length > 0) {
            return control.Name;
        }
        if (control.ToolTip is string tip) {
            return tip;
        }

        return (control as ICommandSource)?.Command is RoutedCommand command ? command.Name : control.GetType().Name;
    }
}
