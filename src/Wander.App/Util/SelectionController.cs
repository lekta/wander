using System.Windows.Controls;
using System.Windows.Input;
using Wander.App.ViewModels;
using Wander.Core.FileSystem;

namespace Wander.App.Util;

/// <summary>
/// Owns view-side selection gestures for the file list — currently the
/// deferred-collapse used to keep multi-selection alive across a drag, plus
/// the per-mode "clear active list" helper. As more gestures land
/// (click-empty=unselect, rubber-band selection, inline-rename trigger),
/// they live here too so MainWindow.xaml.cs stays focused on hosting and
/// drag/drop coordination.
/// </summary>
public sealed class SelectionController {
    // Deferred-selection state: when the user clicks on an already-selected
    // row without modifiers and might be about to drag, WPF default would
    // collapse the selection to just that row — making the subsequent drag
    // carry only one file. We defer the collapse until MouseUp so a drag
    // can still see the full multi-selection.
    private bool _deferred;
    private FileSystemEntry? _deferredEntry;
    private object? _deferredControl;


    /// <summary>
    /// Decide whether the click that just happened should be deferred. Call
    /// from <c>PreviewMouseLeftButtonDown</c>. Returns <c>true</c> if the
    /// caller should set <c>e.Handled = true</c> to suppress WPF's default
    /// selection-collapse.
    /// </summary>
    public bool TryArmDeferred(object control, FileSystemEntry? clicked, IReadOnlyList<FileSystemEntry> selection, ModifierKeys mods) {
        Reset();
        if (clicked is null) {
            return false;
        }

        bool plainClick = (mods & (ModifierKeys.Control | ModifierKeys.Shift)) == 0;
        bool clickedSelected = selection.Contains(clicked);
        bool multi = selection.Count > 1;

        if (plainClick && clickedSelected && multi) {
            _deferred = true;
            _deferredEntry = clicked;
            _deferredControl = control;
            return true;
        }
        return false;
    }

    /// <summary>
    /// A drag started before MouseUp — drop the pending collapse so the
    /// full selection survives into the drag operation.
    /// </summary>
    public void NotifyDragStarted() => Reset();

    /// <summary>
    /// MouseUp fired without a drag — commit the deferred collapse now
    /// (single-select the clicked row, matching what WPF would have done
    /// immediately on MouseDown).
    /// </summary>
    public void CommitOnMouseUp() {
        if (_deferred && _deferredEntry is { } entry) {
            switch (_deferredControl) {
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
        Reset();
    }

    /// <summary>
    /// Clear the selection in whichever right-pane list is active for the
    /// current view mode. Called from Esc handling in the window.
    /// </summary>
    public static void ClearActive(ViewMode mode, DataGrid details, ListBox tiles, ListBox icons) {
        switch (mode) {
            case ViewMode.Details:
                details.UnselectAll();
                break;
            case ViewMode.Tiles:
                tiles.UnselectAll();
                break;
            case ViewMode.LargeIcons:
                icons.UnselectAll();
                break;
        }
    }


    private void Reset() {
        _deferred = false;
        _deferredEntry = null;
        _deferredControl = null;
    }
}
