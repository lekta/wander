using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Wander.App.Converters;
using Wander.App.DragPreview;
using Wander.App.Util;
using Wander.App.ViewModels;
using Wander.Core.FileSystem;
using Wander.Core.Icons;
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
    }

    private MainViewModel Vm => (MainViewModel)DataContext;


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
            System.Windows.DragDrop.DoDragDrop(src, data, DragDropEffects.Copy | DragDropEffects.Move);
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
            action = _currentDragEffect == DragDropEffects.Move ? DragAction.Move : DragAction.Copy;
            string verb = action == DragAction.Move ? "Move" : "Copy";
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
            var reason = PathSafety.DetectSelfDrop(paths, target, out string? offender);
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

            var reason = PathSafety.DetectSelfDrop(paths, target, out _);
            if (reason != SelfDropReason.None) {
                return;
            }

            var wpfEffect = ChooseEffect(paths, target);
            var effect = wpfEffect == DragDropEffects.Move ? DropEffect.Move : DropEffect.Copy;
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
                if (fe.DataContext is FileSystemEntry entry && entry.Kind == EntryKind.Directory) {
                    return entry.FullPath;
                }
                if (fe.DataContext is TreeNodeViewModel node && !string.IsNullOrEmpty(node.FullPath)) {
                    return node.FullPath;
                }
            }
            hit = VisualTreeHelper.GetParent(hit);
        }
        return Vm.CurrentPath;
    }

    private static UIElement? FindHighlightElement(DragEventArgs e) {
        var hit = e.OriginalSource as DependencyObject;
        while (hit is not null) {
            if (hit is TreeViewItem tvi && tvi.DataContext is TreeNodeViewModel) {
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
