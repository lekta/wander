using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit.Highlighting;
using Wander.App.Controls;
using Wander.App.Converters;
using Wander.App.DragPreview;
using Wander.App.Util;
using Wander.App.ViewModels;
using Wander.Core;
using Wander.Core.FileSystem;
using Wander.Core.Icons;
using Wander.Core.Persistence;
using Wander.Core.Shell;
// Disambiguate from System.Windows.DragAction (used by QueryContinueDrag).
using DragAction = Wander.App.DragPreview.DragAction;

namespace Wander.App;

public partial class MainWindow : Window {
    // --- Tree expand/collapse gesture state -----------------------------
    private bool _userClickedExpander;
    private bool _altWasHeld;

    // --- Drag source state ---------------------------------------------
    private Point _dragOrigin;
    private bool _dragArmed;
    private int _dragPathCount;
    private string? _dragFirstName;

    // --- Selection gestures (deferred collapse, active-list clear) ----
    private readonly SelectionController _selection = new();

    // --- Rubber-band (marquee) selection state ------------------------
    // Active when the user clicks empty space in a list and drags. Tracks
    // the host control, the starting selection (so Ctrl-additive can layer
    // on top), and the adorner used to paint the marquee.
    private bool _rubberBandActive;
    private ItemsControl? _rubberBandHost;
    private AdornerLayer? _rubberBandLayer;
    private RubberBandAdorner? _rubberBandAdorner;
    private HashSet<FileSystemEntry>? _rubberBandBaseSelection;
    private Point _rubberBandOrigin;

    // --- Drag preview + drop indicator state ---------------------------
    private DragPreviewWindow? _dragPreview;
    private DropTargetAdorner? _dropAdorner;
    private AdornerLayer? _dropAdornerLayer;
    private string? _currentDropTarget;
    private DragDropEffects _currentDragEffect;
    private SelfDropReason _currentSelfDropReason;
    private string? _currentSelfDropOffender;


    public MainWindow() {
        InitializeComponent();
        Loaded += OnLoaded;
    }


    // --- Window geometry persistence -----------------------------------

    private void OnSourceInitialized(object? sender, EventArgs e) {
        RestoreWindowGeometry();
    }

    private void OnClosing(object? sender, CancelEventArgs e) {
        SaveWindowGeometry();
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


    // --- Preview pane layout + content wiring --------------------------

    private bool _webInitialized;


    private void OnLoaded(object sender, RoutedEventArgs e) {
        if (DataContext is MainViewModel vm) {
            vm.PropertyChanged += OnVmPropertyChanged;
            vm.Preview.PropertyChanged += OnPreviewPropertyChanged;
        }
        ApplyPreviewLayout();
        UpdateCodeEditor();
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
        }
    }

    private async void OnPreviewPropertyChanged(object? sender, PropertyChangedEventArgs e) {
        switch (e.PropertyName) {
            case nameof(PreviewController.CodeText):
            case nameof(PreviewController.CodeExtension):
                UpdateCodeEditor();
                break;

            case nameof(PreviewController.WebUri):
                if (Vm.Preview.WebUri is { } uri) {
                    await EnsureWebViewReadyAsync();
                    try { WebPreview.Source = uri; } catch { /* webview not ready */ }
                }
                break;

            case nameof(PreviewController.WebHtml):
                if (Vm.Preview.WebHtml is { } html) {
                    await EnsureWebViewReadyAsync();
                    try { WebPreview.NavigateToString(html); } catch { /* webview not ready */ }
                }
                break;

            case nameof(PreviewController.Kind):
                // Bail out of any in-flight image-zoom state when the user
                // switches to a different file (e.g., RMB held when changing
                // selection). Also reset the video transport so a freshly
                // opened video starts paused with the play button correct.
                ExitImageZoom();
                ResetVideoTransport();
                break;

            case nameof(PreviewController.VideoUri):
                // MediaElement reloads on Source change via the binding; we
                // just reset the slider / play button so the UI matches.
                ResetVideoTransport();
                break;
        }
    }

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

    private void UpdateCodeEditor() {
        if (string.IsNullOrEmpty(Vm.Preview.CodeText)) {
            CodeEditor.Clear();
            CodeEditor.SyntaxHighlighting = null;
            return;
        }

        string ext = Vm.Preview.CodeExtension ?? "";
        // AvalonEdit ships highlighting for: C#, C++, Java, JS, TS, CSS, HTML, XML, JSON, Python, PHP, SQL, Markdown, ...
        CodeEditor.SyntaxHighlighting = HighlightingManager.Instance.GetDefinitionByExtension(ext);
        CodeEditor.Text = Vm.Preview.CodeText;
    }

    private async Task EnsureWebViewReadyAsync() {
        if (_webInitialized) {
            return;
        }
        try {
            await WebPreview.EnsureCoreWebView2Async();
            if (WebPreview.CoreWebView2 is not null) {
                WebPreview.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                WebPreview.CoreWebView2.Settings.AreDevToolsEnabled = false;
            }
            _webInitialized = true;
        } catch {
            // WebView2 runtime not installed — the pane will stay blank
            // for PDF / HTML / Markdown previews. Other previews are unaffected.
        }
    }


    // --- Global hotkeys not bound to commands ---------------------------

    protected override void OnPreviewKeyDown(KeyEventArgs e) {
        base.OnPreviewKeyDown(e);
        if (e.Handled) {
            return;
        }

        // Ctrl+L: focus the address bar (parity with browsers / Explorer).
        if (e.Key == Key.L && Keyboard.Modifiers == ModifierKeys.Control) {
            AddressBox.Focus();
            AddressBox.SelectAll();
            e.Handled = true;
            return;
        }

        // Ctrl+F: focus the search box. Skip when the user is typing inside
        // the code preview — AvalonEdit owns Ctrl+F there for its own search
        // panel, and stealing it would be surprising.
        if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control) {
            if (!CodeEditor.IsKeyboardFocusWithin) {
                SearchBox.Focus();
                SearchBox.SelectAll();
                e.Handled = true;
                return;
            }
        }

        // F2: rename the primary selected entry.
        if (e.Key == Key.F2 && Vm.SelectedEntry is FileSystemEntry) {
            StartRename();
            e.Handled = true;
            return;
        }

        // Esc: clear selection in whichever right-pane list is active.
        if (e.Key == Key.Escape) {
            ClearActiveSelection();
            // Don't mark handled — let TextBoxes etc. still get Esc if they want it.
        }
    }

    private void ClearActiveSelection() {
        SelectionController.ClearActive(Vm.ViewMode, Grid, TilesView, IconsView);
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
                FocusActiveList();
            }
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Enter) {
            FocusActiveList();
            e.Handled = true;
        }
    }

    private void FocusActiveList() {
        switch (Vm.ViewMode) {
            case ViewMode.Details: Grid.Focus(); break;
            case ViewMode.Tiles: TilesView.Focus(); break;
            case ViewMode.LargeIcons: IconsView.Focus(); break;
        }
    }


    // --- File list selection / opening ---------------------------------

    private void Grid_MouseDoubleClick(object sender, MouseButtonEventArgs e) {
        OpenSelected();
    }

    private void Tiles_MouseDoubleClick(object sender, MouseButtonEventArgs e) {
        OpenSelected();
    }

    private void Rename_Click(object sender, RoutedEventArgs e) {
        StartRename();
    }

    private void List_SelectionChanged(object sender, SelectionChangedEventArgs e) {
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


    private void OpenSelected() {
        if (Vm.SelectedEntry is FileSystemEntry entry) {
            Vm.OpenEntry(entry);
        }
    }

    private void StartRename() {
        if (Vm.SelectedEntry is not FileSystemEntry entry) {
            return;
        }

        string? input = PromptDialog.Show("Rename", "New name:", entry.Name, filenameMode: true);
        if (input is null || input == entry.Name) {
            return;
        }

        Vm.RenameCommand.Execute(input);
    }


    // --- Tree: selection -----------------------------------------------

    private void Tree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e) {
        if (e.NewValue is TreeNodeViewModel node && !string.IsNullOrEmpty(node.FullPath)) {
            Vm.NavigateTo(node.FullPath, Wander.Core.Navigation.NavigationSource.Drives);
        }
    }

    private void Bookmarks_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e) {
        if (e.NewValue is TreeNodeViewModel node && !string.IsNullOrEmpty(node.FullPath)) {
            Vm.NavigateTo(node.FullPath, Wander.Core.Navigation.NavigationSource.Bookmark);
        }
    }


    // --- Tree: custom expand/collapse semantics ------------------------

    private void Tree_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
        if (HitTestExpander(e.OriginalSource as DependencyObject)) {
            _userClickedExpander = true;
            _altWasHeld = (Keyboard.Modifiers & ModifierKeys.Alt) != 0;
        } else {
            _userClickedExpander = false;
            _altWasHeld = false;
        }
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


    private static bool HitTestExpander(DependencyObject? hit) {
        while (hit is not null) {
            if (hit is ToggleButton) {
                return true;
            }
            hit = VisualTreeHelper.GetParent(hit);
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

    private void List_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
        _dragArmed = false;
        _dragOrigin = e.GetPosition(this);

        var clicked = FindEntryAtSource(e.OriginalSource);
        if (clicked is null) {
            // Empty area: start a rubber-band lasso. The drag-source path
            // doesn't apply here (no source items), so we skip its arming
            // and own the gesture end-to-end via MouseMove / MouseUp.
            _selection.TryArmDeferred(sender, null, Vm.SelectedEntries, Keyboard.Modifiers);
            if (sender is ItemsControl host) {
                StartRubberBand(host, e);
            }
            return;
        }
        _dragArmed = true;

        if (_selection.TryArmDeferred(sender, clicked, Vm.SelectedEntries, Keyboard.Modifiers)) {
            e.Handled = true;
        }
    }

    private void List_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e) {
        if (_rubberBandActive) {
            EndRubberBand();
            e.Handled = true;
            return;
        }
        _selection.CommitOnMouseUp();
        _dragArmed = false;
    }

    private static FileSystemEntry? FindEntryAtSource(object originalSource) {
        var hit = originalSource as DependencyObject;
        while (hit is not null) {
            if (hit is FrameworkElement fe && fe.DataContext is FileSystemEntry entry) {
                return entry;
            }
            hit = VisualTreeHelper.GetParent(hit);
        }
        return null;
    }

    private void List_PreviewMouseMove(object sender, MouseEventArgs e) {
        // Rubber-band wins over drag-source: if we started a marquee on
        // empty space, every subsequent mouse-move is selection-update,
        // not drag-arming.
        if (_rubberBandActive && _rubberBandHost == sender) {
            // Defensive: if we missed the MouseUp (capture stolen, window
            // alt-tab, …), bail out cleanly the moment we see LMB up.
            if (e.LeftButton != MouseButtonState.Pressed) {
                EndRubberBand();
                return;
            }
            UpdateRubberBand(e.GetPosition(_rubberBandHost));
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

        StartDrag((DependencyObject)sender, paths);
    }

    private void StartDrag(DependencyObject src, string[] paths) {
        _dragPathCount = paths.Length;
        _dragFirstName = Path.GetFileName(paths[0].TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        _currentDropTarget = null;
        _currentDragEffect = DragDropEffects.None;

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
            var data = new DataObject(DataFormats.FileDrop, paths);
            System.Windows.DragDrop.DoDragDrop(src, data, DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link);
        } catch {
            // drop target may throw on rejection — ignore.
        } finally {
            System.Windows.DragDrop.RemoveGiveFeedbackHandler(src, feedback);
            ClearDropHighlight();
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

        if (_currentDragEffect == DragDropEffects.None || _currentDropTarget is null) {
            // "Forbidden" branch. Two sub-cases:
            //  • Self-drop with a specific reason ("into own subfolder", …) —
            //    we want to surface that reason loudly so the user knows
            //    *why* the drop is refused.
            //  • Nothing useful under the cursor (column header, scrollbar,
            //    splitter) — the system's no-drop cursor is enough, and a
            //    red "Cannot drop here" plaque hovering over a perfectly
            //    valid neighbouring folder is just noise. Hide the preview.
            if (_currentSelfDropReason != SelfDropReason.None && _currentDropTarget is not null) {
                ShowDragPreview();
                action = DragAction.Forbidden;
                desc = PathSafety.FormatReason(_currentSelfDropReason, _currentSelfDropOffender, _currentDropTarget);
            } else {
                HideDragPreview();
                return;
            }
        } else {
            ShowDragPreview();
            action = _currentDragEffect switch {
                DragDropEffects.Move => DragAction.Move,
                DragDropEffects.Link => DragAction.Link,
                _ => DragAction.Copy,
            };
            string verb = action switch {
                DragAction.Move => "Move",
                DragAction.Link => "Create shortcut to",
                _ => "Copy",
            };
            string what = count == 1 ? $"'{_dragFirstName}'" : $"{count} items";
            desc = $"{verb} {what}";
            targetText = "to " + FormatTarget(_currentDropTarget);
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


    // --- Drop target ----------------------------------------------------

    private void OnDragOver(object sender, DragEventArgs e) {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            ResetDropState();
            SetDropHighlight(null);
            return;
        }

        var paths = (string[])e.Data.GetData(DataFormats.FileDrop);
        string? target = ResolveDropTarget(e);

        if (target is null) {
            e.Effects = DragDropEffects.None;
            ResetDropState();
            SetDropHighlight(null);
        } else {
            // Self-drop checks don't apply to Link — Explorer happily makes a
            // shortcut next to the original.
            bool isLink = (Keyboard.Modifiers & ModifierKeys.Alt) != 0;
            var reason = isLink
                ? SelfDropReason.None
                : PathSafety.DetectSelfDrop(paths, target, out _);
            string? offender = null;
            if (!isLink) {
                PathSafety.DetectSelfDrop(paths, target, out offender);
            }

            if (reason != SelfDropReason.None) {
                e.Effects = DragDropEffects.None;
                _currentDragEffect = DragDropEffects.None;
                _currentDropTarget = target;
                _currentSelfDropReason = reason;
                _currentSelfDropOffender = offender;
                SetDropHighlight(null);
            } else {
                e.Effects = ChooseEffect(paths, target);
                _currentDragEffect = e.Effects;
                _currentDropTarget = target;
                _currentSelfDropReason = SelfDropReason.None;
                _currentSelfDropOffender = null;
                SetDropHighlight(FindHighlightElement(e));
            }
        }
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e) {
        try {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) {
                return;
            }

            var paths = ((string[])e.Data.GetData(DataFormats.FileDrop)).ToList();
            string? target = ResolveDropTarget(e);
            if (target is null) {
                return;
            }

            // Self-drop checks don't apply to Link — Explorer happily makes a
            // shortcut next to the original, including into the source folder.
            bool isLink = (Keyboard.Modifiers & ModifierKeys.Alt) != 0;
            if (!isLink) {
                var reason = PathSafety.DetectSelfDrop(paths, target, out _);
                if (reason != SelfDropReason.None) {
                    return;
                }
            }

            var wpfEffect = ChooseEffect(paths, target);
            var effect = wpfEffect switch {
                DragDropEffects.Move => DropEffect.Move,
                DragDropEffects.Link => DropEffect.Link,
                _ => DropEffect.Copy,
            };
            Vm.HandleDrop(paths, target, effect);
            e.Handled = true;
        } finally {
            ClearDropHighlight();
        }
    }

    private void ResetDropState() {
        _currentDragEffect = DragDropEffects.None;
        _currentDropTarget = null;
        _currentSelfDropReason = SelfDropReason.None;
        _currentSelfDropOffender = null;
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

        if (IsOverDroppableBookmarkFolder(e)) {
            // Defer to the standard handler — same effect, same highlight,
            // same self-drop protection as the drives tree.
            OnDragOver(sender, e);
            return;
        }

        var paths = (string[])e.Data.GetData(DataFormats.FileDrop);
        bool anyFolder = paths.Any(p => Directory.Exists(p));
        e.Effects = anyFolder ? DragDropEffects.Link : DragDropEffects.None;
        // Clear any leftover highlight from a previous in-folder hover so
        // empty-area drops don't look like they're targeting something.
        SetDropHighlight(null);
        e.Handled = true;
    }

    private void BookmarksPanel_Drop(object sender, DragEventArgs e) {
        if (IsOverDroppableBookmarkFolder(e)) {
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
                Vm.Status = "В закладки можно перетаскивать только папки.";
            }
            e.Handled = true;
        } finally {
            ClearDropHighlight();
        }
    }

    // --- Bookmark drop-zone (explicit affordance at top of panel) ------
    //
    // Sits between the bookmarks header and the tree. Always visible when
    // the panel is expanded; styled subtly when idle, prominently when a
    // file-drag is hovering. Drops here always mean "add to bookmarks";
    // the parent BookmarksPanel still accepts drops on its empty area so
    // users who learn the gesture aren't forced to aim at the strip.

    private static readonly SolidColorBrush _dropZoneIdleBg = Brushes.Transparent;
    private static readonly SolidColorBrush _dropZoneIdleBorder = new(Color.FromRgb(0xDD, 0xDD, 0xDD));
    private static readonly SolidColorBrush _dropZoneIdleText = new(Color.FromRgb(0x88, 0x88, 0x88));
    private static readonly SolidColorBrush _dropZoneActiveBg = new(Color.FromRgb(0xE5, 0xF1, 0xFB));
    private static readonly SolidColorBrush _dropZoneActiveBorder = new(Color.FromRgb(0x00, 0x78, 0xD7));
    private static readonly SolidColorBrush _dropZoneActiveText = new(Color.FromRgb(0x00, 0x55, 0xA8));

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
                Vm.Status = "В закладки можно перетаскивать только папки.";
            }
            e.Handled = true;
        } finally {
            SetBookmarkDropZoneActive(false);
        }
    }

    private static bool CanAcceptBookmarkDrop(DragEventArgs e) {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) {
            return false;
        }
        var paths = (string[])e.Data.GetData(DataFormats.FileDrop);
        return paths.Any(Directory.Exists);
    }

    private void SetBookmarkDropZoneActive(bool active) {
        if (active) {
            BookmarkDropZone.Background = _dropZoneActiveBg;
            BookmarkDropZone.BorderBrush = _dropZoneActiveBorder;
            BookmarkDropZone.BorderThickness = new Thickness(1);
            BookmarkDropZoneText.Foreground = _dropZoneActiveText;
            BookmarkDropZoneText.FontWeight = FontWeights.SemiBold;
            BookmarkDropZoneText.Text = "+  Добавить в закладки";
        } else {
            BookmarkDropZone.Background = _dropZoneIdleBg;
            BookmarkDropZone.BorderBrush = _dropZoneIdleBorder;
            BookmarkDropZone.BorderThickness = new Thickness(0, 0, 0, 1);
            BookmarkDropZoneText.Foreground = _dropZoneIdleText;
            BookmarkDropZoneText.FontWeight = FontWeights.Normal;
            BookmarkDropZoneText.Text = "+  Перетащите папку сюда";
        }
    }

    /// <summary>
    /// True when the drag is hovering over a TreeViewItem in the bookmarks
    /// tree whose DataContext is a real on-disk folder bookmark (not a
    /// shell-namespace sentinel like "shell:RecycleBinFolder", which has
    /// no backing directory to copy into).
    /// </summary>
    private static bool IsOverDroppableBookmarkFolder(DragEventArgs e) {
        var hit = e.OriginalSource as DependencyObject;
        while (hit is not null) {
            if (hit is FrameworkElement fe && fe.DataContext is TreeNodeViewModel node) {
                if (string.IsNullOrEmpty(node.FullPath)) {
                    return false;
                }
                if (node.FullPath.StartsWith("shell:", StringComparison.OrdinalIgnoreCase)) {
                    return false;
                }
                return true;
            }
            hit = VisualTreeHelper.GetParent(hit);
        }
        return false;
    }


    private string? ResolveDropTarget(DragEventArgs e) {
        var hit = e.OriginalSource as DependencyObject;
        while (hit is not null) {
            if (hit is FrameworkElement fe) {
                if (fe.DataContext is FileSystemEntry entry) {
                    if (entry.Kind == EntryKind.Directory) {
                        return entry.FullPath;
                    }
                    // .lnk pointing at a directory = drop into the real folder.
                    if (entry.FullPath.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) {
                        string? resolved = ResolveShortcutTarget(entry.FullPath);
                        if (resolved is not null && Directory.Exists(resolved)) {
                            return resolved;
                        }
                    }
                }
                if (fe.DataContext is TreeNodeViewModel node && !string.IsNullOrEmpty(node.FullPath)) {
                    return node.FullPath;
                }
            }
            hit = VisualTreeHelper.GetParent(hit);
        }
        return Vm.CurrentPath;
    }

    private static string? ResolveShortcutTarget(string lnkPath) {
        if (!ServiceLocator.IsRegistered<IShortcutService>()) {
            return null;
        }
        try {
            return ServiceLocator.Get<IShortcutService>().Resolve(lnkPath);
        } catch {
            return null;
        }
    }

    private static UIElement? FindHighlightElement(DragEventArgs e) {
        var hit = e.OriginalSource as DependencyObject;
        while (hit is not null) {
            if (hit is TreeViewItem tvi && tvi.DataContext is TreeNodeViewModel) {
                // RenderSize of a TreeViewItem includes its expanded children —
                // adorning that would paint the highlight over the whole subtree.
                // The default WPF template names the row container "Bd" (Aero2);
                // adorn that if available, otherwise fall back to the row itself.
                if (tvi.Template?.FindName("Bd", tvi) is UIElement header) {
                    return header;
                }
                return tvi;
            }
            if (hit is ListBoxItem lbi && lbi.DataContext is FileSystemEntry fe1 && fe1.Kind == EntryKind.Directory) {
                return lbi;
            }
            if (hit is DataGridRow dgr && dgr.DataContext is FileSystemEntry fe2 && fe2.Kind == EntryKind.Directory) {
                return dgr;
            }
            hit = VisualTreeHelper.GetParent(hit);
        }
        return null;
    }

    private void SetDropHighlight(UIElement? target) {
        if (_dropAdorner is not null && _dropAdornerLayer is not null) {
            _dropAdornerLayer.Remove(_dropAdorner);
            _dropAdorner = null;
            _dropAdornerLayer = null;
        }

        if (target is null) {
            return;
        }

        var layer = AdornerLayer.GetAdornerLayer(target);
        if (layer is null) {
            return;
        }

        _dropAdorner = new DropTargetAdorner(target);
        _dropAdornerLayer = layer;
        layer.Add(_dropAdorner);
    }

    private void ClearDropHighlight() {
        SetDropHighlight(null);
        ResetDropState();
    }


    private static DragDropEffects ChooseEffect(IReadOnlyList<string> paths, string target) {
        var mods = Keyboard.Modifiers;
        // Alt → make a shortcut (Explorer parity).
        if (mods.HasFlag(ModifierKeys.Alt)) {
            return DragDropEffects.Link;
        }
        if (mods.HasFlag(ModifierKeys.Shift)) {
            return DragDropEffects.Move;
        }
        if (mods.HasFlag(ModifierKeys.Control)) {
            return DragDropEffects.Copy;
        }
        return paths.Count > 0 && IsSameDrive(paths[0], target)
            ? DragDropEffects.Move
            : DragDropEffects.Copy;
    }

    private static bool IsSameDrive(string a, string b) {
        string? ra = Path.GetPathRoot(a);
        string? rb = Path.GetPathRoot(b);
        return string.Equals(ra, rb, StringComparison.OrdinalIgnoreCase);
    }


    // ======================================================================
    // Preview pane: image zoom (FastStone-style RMB-hold pan zoom).
    // ======================================================================
    //
    // When the previewed image is downscaled to fit the pane:
    //   • the cursor turns into a magnifier glyph,
    //   • holding the right mouse button shows the image at native 1:1 with
    //     the pixel under the cursor anchored to the cursor's screen position,
    //   • moving the mouse pans the 1:1 view — release RMB to return.
    //
    // Geometry: as the cursor moves from (0,0) to (host.W, host.H) we map
    // linearly onto (0,0)..(src.W, src.H) image-pixel space, then position
    // the 1:1 image so that mapped pixel sits under the cursor. This
    // matches FastStone / IrfanView "navigator" zoom.

    private bool _imageZoomActive;

    private bool IsImageDownscaled() {
        if (ImgFit.Source is not BitmapSource src) {
            return false;
        }
        // A few pixels of slop avoid jitter exactly at break-even. If the
        // source is already smaller than the available render area there's
        // nothing useful to zoom into, so we don't switch the cursor.
        return src.PixelWidth > ImgFit.ActualWidth + 1
            || src.PixelHeight > ImgFit.ActualHeight + 1;
    }

    private void UpdateImageCursor() {
        ImagePreviewHost.Cursor = IsImageDownscaled() ? MagnifierCursor.Instance : null;
    }

    private void ImgFit_SizeChanged(object sender, SizeChangedEventArgs e) {
        // The fitted image's rendered size changes when the user resizes
        // the pane or selects a differently-sized image. Refresh the
        // cursor decision accordingly.
        UpdateImageCursor();
    }

    private void ImageZoom_MouseEnter(object sender, MouseEventArgs e) {
        UpdateImageCursor();
    }

    private void ImageZoom_MouseLeave(object sender, MouseEventArgs e) {
        // Don't kill an active zoom on Leave — Mouse.Capture means we keep
        // getting events anyway, and the user is probably panning to an
        // image edge. Just restore the cursor.
        if (!_imageZoomActive) {
            ImagePreviewHost.Cursor = null;
        }
    }

    private void ImageZoom_LmbDown(object sender, MouseButtonEventArgs e) {
        if (!IsImageDownscaled()) {
            return;
        }
        if (ImgFit.Source is not BitmapSource src) {
            return;
        }

        _imageZoomActive = true;
        // 1 DIP = 1 image pixel (no DPI compensation — matches FastStone's
        // "100 %" semantics on the user's currently configured DPI).
        ImgZoom.Width = src.PixelWidth;
        ImgZoom.Height = src.PixelHeight;
        ImgZoomCanvas.Visibility = Visibility.Visible;
        UpdateZoomPosition(e.GetPosition(ImagePreviewHost));
        // Capture so we still get the LMB-up if the user lifts the button
        // outside the host (e.g., over the splitter). LostMouseCapture is
        // our cleanup path.
        ImagePreviewHost.CaptureMouse();
        e.Handled = true;
    }

    private void ImageZoom_LmbUp(object sender, MouseButtonEventArgs e) {
        ExitImageZoom();
        e.Handled = true;
    }

    private void ImageZoom_MouseMove(object sender, MouseEventArgs e) {
        if (!_imageZoomActive) {
            return;
        }
        // Defensive: if LMB was released while we missed an event (e.g.,
        // capture got stolen), drop out of zoom.
        if (e.LeftButton != MouseButtonState.Pressed) {
            ExitImageZoom();
            return;
        }
        UpdateZoomPosition(e.GetPosition(ImagePreviewHost));
    }

    private void ImageZoom_LostCapture(object sender, MouseEventArgs e) {
        ExitImageZoom();
    }

    // Mirror of the Margin attribute on ImgFit (8,12,8,8) so the zoom view
    // can match the fit view's placement on the non-panning axis. Keep in
    // sync if the XAML margin ever changes.
    private const double PreviewImageMarginTop = 12;
    // Left/right margins are symmetric — the centering math (hw - srcW)/2
    // produces the same X whether you account for them or not, so no
    // constant needed for X.

    /// <summary>
    /// Positions the 1:1 zoom image so that the image-pixel under the
    /// cursor stays under the cursor. Pan is per-axis: only the dimension
    /// that doesn't fit the pane scrolls with the cursor. The other one
    /// is aligned to match how ImgFit (the fit-mode view) lays it out —
    /// centred horizontally, top-anchored vertically — so toggling zoom
    /// on doesn't visually jump the image to the middle.
    ///
    /// Mouse coordinates are clamped to the pane rectangle. The mouse
    /// capture during zoom lets the cursor travel outside the host (e.g.
    /// over the splitter); without clamping, the formula would extrapolate
    /// and shove the image past the edge it should be pinned to.
    /// </summary>
    private void UpdateZoomPosition(Point mouse) {
        if (ImgFit.Source is not BitmapSource src) {
            return;
        }
        double hw = ImagePreviewHost.ActualWidth;
        double hh = ImagePreviewHost.ActualHeight;
        if (hw <= 0 || hh <= 0) {
            return;
        }

        double srcW = src.PixelWidth;
        double srcH = src.PixelHeight;

        // Clamp to pane interior so leaving the pane doesn't scroll past
        // the image edges. At mouse.X == 0 we show the image's left edge;
        // at mouse.X == hw, the right edge.
        double mx = Math.Clamp(mouse.X, 0, hw);
        double my = Math.Clamp(mouse.Y, 0, hh);

        // X axis: pan only if image is wider than the pane.
        // When it fits, centre horizontally — matches ImgFit's
        // HorizontalAlignment="Center" with symmetric L/R margins.
        double x = srcW > hw
            ? mx - (mx / hw) * srcW
            : (hw - srcW) / 2;

        // Y axis: pan only if image is taller than the pane.
        // When it fits, anchor to the top with the same margin ImgFit
        // uses — ImgFit has VerticalAlignment="Top" + Margin="8,12,8,8",
        // so the fit view places the image at y=12. Centring vertically
        // here would visibly jump the image down when the user holds LMB.
        double y = srcH > hh
            ? my - (my / hh) * srcH
            : PreviewImageMarginTop;

        Canvas.SetLeft(ImgZoom, x);
        Canvas.SetTop(ImgZoom, y);
    }

    private void ExitImageZoom() {
        if (!_imageZoomActive) {
            return;
        }
        _imageZoomActive = false;
        ImgZoomCanvas.Visibility = Visibility.Collapsed;
        if (ImagePreviewHost.IsMouseCaptured) {
            ImagePreviewHost.ReleaseMouseCapture();
        }
        UpdateImageCursor();
    }


    // ======================================================================
    // Preview pane: video transport (MediaElement + Play/Pause + seek).
    // ======================================================================

    private DispatcherTimer? _videoTimer;
    private bool _videoIsPlaying;
    private bool _videoSliderDragging;
    private bool _suppressVideoSliderChanged;

    private void VideoPreview_MediaOpened(object sender, RoutedEventArgs e) {
        // Cap the video preview to native pixel size — same rationale as
        // for images: a 320×240 clip shouldn't stretch to fill a giant
        // preview pane. Done here because NaturalVideoWidth/Height aren't
        // known until MediaElement has actually opened the file.
        if (VideoPreview.NaturalVideoWidth > 0 && VideoPreview.NaturalVideoHeight > 0) {
            VideoPreview.MaxWidth = VideoPreview.NaturalVideoWidth;
            VideoPreview.MaxHeight = VideoPreview.NaturalVideoHeight;
        } else {
            VideoPreview.MaxWidth = double.PositiveInfinity;
            VideoPreview.MaxHeight = double.PositiveInfinity;
        }

        if (!VideoPreview.NaturalDuration.HasTimeSpan) {
            return;
        }
        double total = VideoPreview.NaturalDuration.TimeSpan.TotalSeconds;
        _suppressVideoSliderChanged = true;
        VideoSlider.Maximum = total;
        VideoSlider.Value = 0;
        _suppressVideoSliderChanged = false;

        UpdateVideoTimeText();
        EnsureVideoTimer();
    }

    private void VideoPreview_MediaEnded(object sender, RoutedEventArgs e) {
        // Rewind to start, leave paused — same convention as Explorer's
        // preview pane and most desktop video viewers.
        VideoPreview.Position = TimeSpan.Zero;
        VideoPreview.Pause();
        _videoIsPlaying = false;
        VideoPlayPauseButton.Content = "▶";
    }

    private void VideoPreview_MediaFailed(object sender, ExceptionRoutedEventArgs e) {
        // Codec not installed (e.g. .webm without the Web Media Extensions)
        // or corrupt file. Surface a minimal hint in the slider area.
        VideoTimeText.Text = "Воспроизведение недоступно";
    }

    private void EnsureVideoTimer() {
        if (_videoTimer is not null) {
            return;
        }
        // 200 ms is responsive enough for a progress bar and cheap on CPU.
        _videoTimer = new DispatcherTimer(DispatcherPriority.Background) {
            Interval = TimeSpan.FromMilliseconds(200),
        };
        _videoTimer.Tick += VideoTimer_Tick;
        _videoTimer.Start();
    }

    private void VideoTimer_Tick(object? sender, EventArgs e) {
        if (_videoSliderDragging) {
            return;
        }
        if (!VideoPreview.NaturalDuration.HasTimeSpan) {
            return;
        }
        // Avoid feedback: setting Slider.Value programmatically would
        // otherwise re-fire ValueChanged and try to seek us back.
        _suppressVideoSliderChanged = true;
        VideoSlider.Value = VideoPreview.Position.TotalSeconds;
        _suppressVideoSliderChanged = false;
        UpdateVideoTimeText();
    }

    private void VideoPlayPause_Click(object sender, RoutedEventArgs e) {
        if (_videoIsPlaying) {
            VideoPreview.Pause();
            _videoIsPlaying = false;
            VideoPlayPauseButton.Content = "▶";
        } else {
            VideoPreview.Play();
            _videoIsPlaying = true;
            VideoPlayPauseButton.Content = "⏸";
        }
    }

    private void VideoSlider_PreviewMouseDown(object sender, MouseButtonEventArgs e) {
        _videoSliderDragging = true;
    }

    private void VideoSlider_PreviewMouseUp(object sender, MouseButtonEventArgs e) {
        _videoSliderDragging = false;
        // Final seek to the slider's resting value — ValueChanged during the
        // drag already kept Position roughly synced with ScrubbingEnabled,
        // but a final commit handles the last pointer position cleanly.
        if (VideoPreview.NaturalDuration.HasTimeSpan) {
            VideoPreview.Position = TimeSpan.FromSeconds(VideoSlider.Value);
            UpdateVideoTimeText();
        }
    }

    private void VideoSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) {
        if (_suppressVideoSliderChanged) {
            return;
        }
        if (!VideoPreview.NaturalDuration.HasTimeSpan) {
            return;
        }
        // ScrubbingEnabled lets MediaElement show frames while we seek
        // mid-drag, so we apply Position on every tick — feels responsive.
        VideoPreview.Position = TimeSpan.FromSeconds(e.NewValue);
        UpdateVideoTimeText();
    }

    private void UpdateVideoTimeText() {
        TimeSpan pos = VideoPreview.Position;
        TimeSpan dur = VideoPreview.NaturalDuration.HasTimeSpan
            ? VideoPreview.NaturalDuration.TimeSpan
            : TimeSpan.Zero;
        VideoTimeText.Text = $"{FormatTimecode(pos)} / {FormatTimecode(dur)}";
    }

    private static string FormatTimecode(TimeSpan t) {
        return t.TotalHours >= 1
            ? $"{(int)t.TotalHours}:{t.Minutes:D2}:{t.Seconds:D2}"
            : $"{t.Minutes}:{t.Seconds:D2}";
    }

    private void ResetVideoTransport() {
        // Explicitly pause: WPF's Visibility=Collapsed doesn't tear the
        // MediaElement down, so audio would otherwise keep playing in the
        // background after the user selects another file.
        try { VideoPreview.Pause(); } catch { /* not yet loaded */ }
        _videoIsPlaying = false;
        VideoPlayPauseButton.Content = "▶";
        _suppressVideoSliderChanged = true;
        try {
            VideoSlider.Value = 0;
            VideoSlider.Maximum = 1;
        } finally {
            _suppressVideoSliderChanged = false;
        }
        VideoTimeText.Text = "0:00 / 0:00";
        // Drop the native-size cap so a fresh video isn't constrained by
        // the previous clip's resolution until MediaOpened reconfigures it.
        VideoPreview.MaxWidth = double.PositiveInfinity;
        VideoPreview.MaxHeight = double.PositiveInfinity;
    }


    // ======================================================================
    // Rubber-band / marquee selection in the file list.
    // ======================================================================
    //
    // Gesture: click on empty space in the right-pane list and drag. A
    // translucent rectangle follows the cursor; every item whose container
    // bounding box intersects the rectangle becomes selected. With Ctrl
    // held, items in the rectangle are added to the existing selection
    // (Explorer parity); without Ctrl the rectangle replaces selection.
    //
    // Implementation notes:
    //   • The marquee is a single Adorner painted on the host's AdornerLayer
    //     (RubberBandAdorner). InvalidateVisual on each mouse move repaints.
    //   • Hit-testing iterates the host's items; for each one we ask
    //     ItemContainerGenerator for the realised container and transform
    //     its bounds back into the host's coordinate system. Virtualised
    //     (not-yet-realised) items are skipped — they're off-screen anyway
    //     and can't be intersected by a visible rectangle.
    //   • Mouse capture on the host ensures we receive MouseUp even if the
    //     cursor leaves the control mid-drag; LostMouseCapture is the
    //     cleanup safety net.

    private void StartRubberBand(ItemsControl host, MouseButtonEventArgs e) {
        // If a previous gesture didn't clean up (shouldn't happen, but be
        // robust), drop it first.
        EndRubberBand();

        bool additive = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        _rubberBandBaseSelection = additive
            ? new HashSet<FileSystemEntry>(Vm.SelectedEntries)
            : new HashSet<FileSystemEntry>();
        if (!additive) {
            ClearListSelection(host);
        }

        _rubberBandHost = host;
        _rubberBandOrigin = e.GetPosition(host);
        _rubberBandLayer = AdornerLayer.GetAdornerLayer(host);
        if (_rubberBandLayer is null) {
            // No adorner layer (extremely rare) — proceed without visuals,
            // hit-testing still works.
            _rubberBandAdorner = null;
        } else {
            _rubberBandAdorner = new RubberBandAdorner(host) {
                StartPoint = _rubberBandOrigin,
                CurrentPoint = _rubberBandOrigin,
            };
            _rubberBandLayer.Add(_rubberBandAdorner);
        }

        _rubberBandActive = true;
        host.CaptureMouse();
        host.LostMouseCapture += RubberBandHost_LostCapture;
        e.Handled = true;
    }

    private void RubberBandHost_LostCapture(object sender, MouseEventArgs e) {
        // Some other element grabbed the mouse — wrap things up so we don't
        // leave a phantom marquee on screen.
        EndRubberBand();
    }

    private void UpdateRubberBand(Point current) {
        if (_rubberBandHost is null || _rubberBandBaseSelection is null) {
            return;
        }

        if (_rubberBandAdorner is not null) {
            _rubberBandAdorner.CurrentPoint = current;
            _rubberBandAdorner.InvalidateVisual();
        }
        var rect = new Rect(_rubberBandOrigin, current);

        // Build the new selection: base (already-selected at gesture start,
        // empty in non-additive mode) ∪ items intersecting the rectangle.
        var newSelection = new HashSet<FileSystemEntry>(_rubberBandBaseSelection);
        foreach (var entry in Vm.Entries) {
            if (TryGetContainerRect(_rubberBandHost, entry, out Rect itemRect)
                && rect.IntersectsWith(itemRect)) {
                newSelection.Add(entry);
            }
        }

        SetListSelection(_rubberBandHost, newSelection);
    }

    private void EndRubberBand() {
        if (!_rubberBandActive) {
            return;
        }
        _rubberBandActive = false;

        if (_rubberBandHost is { } host) {
            host.LostMouseCapture -= RubberBandHost_LostCapture;
            if (host.IsMouseCaptured) {
                host.ReleaseMouseCapture();
            }
        }
        if (_rubberBandAdorner is not null && _rubberBandLayer is not null) {
            _rubberBandLayer.Remove(_rubberBandAdorner);
        }
        _rubberBandHost = null;
        _rubberBandLayer = null;
        _rubberBandAdorner = null;
        _rubberBandBaseSelection = null;
    }

    /// <summary>
    /// Returns the on-screen rectangle of the given entry's item container
    /// in the host's coordinate space, or false if the item isn't realised
    /// (virtualised away off-screen).
    /// </summary>
    private static bool TryGetContainerRect(ItemsControl host, FileSystemEntry entry, out Rect rect) {
        rect = default;
        if (host.ItemContainerGenerator.ContainerFromItem(entry) is not FrameworkElement fe) {
            return false;
        }
        if (fe.ActualWidth <= 0 || fe.ActualHeight <= 0) {
            return false;
        }
        try {
            var transform = fe.TransformToAncestor(host);
            var topLeft = transform.Transform(new Point(0, 0));
            rect = new Rect(topLeft, new Size(fe.ActualWidth, fe.ActualHeight));
            return true;
        } catch {
            // TransformToAncestor throws if the container has been detached
            // from the visual tree mid-iteration; skip.
            return false;
        }
    }

    private static void ClearListSelection(ItemsControl host) {
        if (host is ListBox lb) {
            lb.UnselectAll();
        } else if (host is DataGrid dg) {
            dg.UnselectAll();
        }
    }

    private static void SetListSelection(ItemsControl host, IEnumerable<FileSystemEntry> items) {
        // Set the selection by delta — clearing+adding everything would
        // collapse and re-expand the control's selection, causing visible
        // flicker on ListBox and unnecessary SelectionChanged churn.
        if (host is ListBox lb) {
            ApplyDelta(lb.SelectedItems, items);
        } else if (host is DataGrid dg) {
            ApplyDelta(dg.SelectedItems, items);
        }
    }

    private static void ApplyDelta(System.Collections.IList currentSelection, IEnumerable<FileSystemEntry> targetItems) {
        var target = new HashSet<FileSystemEntry>(targetItems);
        // Remove anything no longer in the target set.
        for (int i = currentSelection.Count - 1; i >= 0; i--) {
            if (currentSelection[i] is FileSystemEntry existing && !target.Contains(existing)) {
                currentSelection.RemoveAt(i);
            }
        }
        // Add anything missing.
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
}
