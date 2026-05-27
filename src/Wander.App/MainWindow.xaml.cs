using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Highlighting;
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

    // Deferred-selection guard so that "click one of several selected rows
    // and drag" keeps the full selection, matching Explorer.
    private bool _deferredSelection;
    private FileSystemEntry? _deferredEntry;
    private object? _deferredSenderControl;

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

        // Restore size first — keeping a sane minimum so a previous truncation
        // can't wedge the window down to a few pixels.
        if (state.WindowWidth is double w && w >= 320 &&
            state.WindowHeight is double h && h >= 240) {
            Width = w;
            Height = h;
        }

        // Restore position, clamped to the virtual screen. This handles the
        // "saved on a monitor that is no longer connected" case without
        // dropping the window off-screen.
        if (state.WindowLeft is double l && state.WindowTop is double t) {
            double vsLeft = SystemParameters.VirtualScreenLeft;
            double vsTop = SystemParameters.VirtualScreenTop;
            double vsRight = vsLeft + SystemParameters.VirtualScreenWidth;
            double vsBottom = vsTop + SystemParameters.VirtualScreenHeight;

            // Keep at least 100 px of titlebar visible so the user can grab it.
            double minLeft = vsLeft - Width + 100;
            double maxLeft = vsRight - 100;
            double minTop = vsTop;
            double maxTop = vsBottom - 60;

            Left = Math.Min(Math.Max(l, minLeft), maxLeft);
            Top = Math.Min(Math.Max(t, minTop), maxTop);
            WindowStartupLocation = WindowStartupLocation.Manual;
        }

        if (state.WindowMaximized) {
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
            WindowLeft = bounds.Left,
            WindowTop = bounds.Top,
            WindowWidth = bounds.Width,
            WindowHeight = bounds.Height,
            WindowMaximized = WindowState == WindowState.Maximized,
        });
    }

    private MainViewModel Vm => (MainViewModel)DataContext;


    // --- Preview pane layout + content wiring --------------------------

    private bool _webInitialized;


    private void OnLoaded(object sender, RoutedEventArgs e) {
        if (DataContext is MainViewModel vm) {
            vm.PropertyChanged += OnVmPropertyChanged;
        }
        ApplyPreviewLayout();
        UpdateCodeEditor();
    }

    private async void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e) {
        switch (e.PropertyName) {
            case nameof(MainViewModel.IsPreviewVisible):
            case nameof(MainViewModel.PreviewWidth):
                ApplyPreviewLayout();
                break;

            case nameof(MainViewModel.PreviewCodeText):
            case nameof(MainViewModel.PreviewCodeExtension):
                UpdateCodeEditor();
                break;

            case nameof(MainViewModel.PreviewWebUri):
                if (Vm.PreviewWebUri is { } uri) {
                    await EnsureWebViewReadyAsync();
                    try { WebPreview.Source = uri; } catch { /* webview not ready */ }
                }
                break;

            case nameof(MainViewModel.PreviewWebHtml):
                if (Vm.PreviewWebHtml is { } html) {
                    await EnsureWebViewReadyAsync();
                    try { WebPreview.NavigateToString(html); } catch { /* webview not ready */ }
                }
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
        if (string.IsNullOrEmpty(Vm.PreviewCodeText)) {
            CodeEditor.Clear();
            CodeEditor.SyntaxHighlighting = null;
            return;
        }

        string ext = Vm.PreviewCodeExtension ?? "";
        // AvalonEdit ships highlighting for: C#, C++, Java, JS, TS, CSS, HTML, XML, JSON, Python, PHP, SQL, Markdown, ...
        CodeEditor.SyntaxHighlighting = HighlightingManager.Instance.GetDefinitionByExtension(ext);
        CodeEditor.Text = Vm.PreviewCodeText;
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
        switch (Vm.ViewMode) {
            case ViewMode.Details:
                Grid.UnselectAll();
                break;
            case ViewMode.Tiles:
                TilesView.UnselectAll();
                break;
            case ViewMode.LargeIcons:
                IconsView.UnselectAll();
                break;
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
            Vm.NavigateTo(node.FullPath);
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
        _deferredSelection = false;
        _deferredEntry = null;
        _deferredSenderControl = null;
        _dragOrigin = e.GetPosition(this);

        var clicked = FindEntryAtSource(e.OriginalSource);
        if (clicked is null) {
            return;
        }
        _dragArmed = true;

        // If the user clicks (without Ctrl/Shift) on a row that's already part
        // of a multi-selection, default WPF would collapse to just that row —
        // making the subsequent drag carry only one file. Defer the selection
        // change to mouse-up so we can keep all selected if a drag starts.
        bool plainClick = (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) == 0;
        bool clickedOnSelected = Vm.SelectedEntries.Contains(clicked);
        bool multi = Vm.SelectedEntries.Count > 1;

        if (plainClick && clickedOnSelected && multi) {
            _deferredSelection = true;
            _deferredEntry = clicked;
            _deferredSenderControl = sender;
            e.Handled = true;
        }
    }

    private void List_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e) {
        if (_deferredSelection && _deferredEntry is FileSystemEntry entry) {
            // No drag happened — finalize the click as a plain single-select.
            switch (_deferredSenderControl) {
                case DataGrid dg:
                    dg.SelectedItems.Clear();
                    dg.SelectedItem = entry;
                    break;
                case ListBox lb:
                    lb.SelectedItems.Clear();
                    lb.SelectedItem = entry;
                    break;
            }
        }
        _deferredSelection = false;
        _deferredEntry = null;
        _deferredSenderControl = null;
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
        if (!_dragArmed || e.LeftButton != MouseButtonState.Pressed) {
            return;
        }

        var pos = e.GetPosition(this);
        if (Math.Abs(pos.X - _dragOrigin.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(pos.Y - _dragOrigin.Y) < SystemParameters.MinimumVerticalDragDistance) {
            return;
        }

        _dragArmed = false;
        _deferredSelection = false; // drag started — keep the full selection

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
            action = DragAction.Forbidden;
            if (_currentSelfDropReason != SelfDropReason.None && _currentDropTarget is not null) {
                desc = PathSafety.FormatReason(_currentSelfDropReason, _currentSelfDropOffender, _currentDropTarget);
            } else {
                desc = count == 1
                    ? $"Cannot drop '{_dragFirstName}' here"
                    : $"Cannot drop {count} items here";
            }
        } else {
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

}
