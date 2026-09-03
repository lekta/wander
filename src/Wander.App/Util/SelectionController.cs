using System.Windows.Controls;
using System.Windows.Input;
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
    // Deferred-selection state: a press on a row can mean two things, and
    // which one is only known when the button comes back up. Both are
    // deferred so the drag sees the selection the user thinks they are
    // dragging:
    //
    //  • plain click on a row that is already part of a multi-selection —
    //    WPF would collapse the selection to that row on MouseDown, and the
    //    drag would carry one file out of ten;
    //  • Ctrl+click on any row — WPF toggles it on MouseDown, so starting a
    //    Ctrl+drag by grabbing one of the selected files (the habit Ctrl
    //    teaches, since Ctrl is also "copy") dropped that very file out of
    //    the selection.
    //
    // Either way the pending change is committed on MouseUp and abandoned
    // when a drag starts instead.
    private bool _deferred;
    private bool _toggle;
    private FileSystemEntry? _deferredEntry;
    private object? _deferredControl;


    /// <summary>
    /// Decide whether the click that just happened should be deferred. Call
    /// from <c>PreviewMouseLeftButtonDown</c>. Returns <c>true</c> if the
    /// caller should set <c>e.Handled = true</c> to suppress WPF's default
    /// selection change.
    /// </summary>
    public bool TryArmDeferred(object control, FileSystemEntry? clicked, IReadOnlyList<FileSystemEntry> selection, ModifierKeys mods) {
        Reset();
        if (clicked is null) {
            return false;
        }

        bool ctrl = (mods & ModifierKeys.Control) != 0;
        bool shift = (mods & ModifierKeys.Shift) != 0;
        bool clickedSelected = selection.Contains(clicked);
        bool multi = selection.Count > 1;

        // Shift is a range, and a range is decided at the press: there is
        // no drag reading of it to protect.
        if (shift) {
            return false;
        }

        if (ctrl) {
            _deferred = true;
            _toggle = true;
            _deferredEntry = clicked;
            _deferredControl = control;

            return true;
        }

        if (clickedSelected && multi) {
            _deferred = true;
            _deferredEntry = clicked;
            _deferredControl = control;

            return true;
        }

        return false;
    }

    /// <summary>
    /// A drag started before MouseUp — the pending change is not what the
    /// user meant, so it is dropped and the full selection survives into
    /// the drag.
    ///
    /// <para>
    /// One exception: a Ctrl+drag begun on a row that was <em>not</em>
    /// selected. Nothing else would put it in the payload, and dragging a
    /// file that stays behind is worse than the toggle this replaces — so
    /// that one addition is applied rather than dropped, which is also what
    /// Explorer does.
    /// </para>
    /// </summary>
    public void NotifyDragStarted() {
        if (_deferred && _toggle && _deferredEntry is { } entry && !IsSelected(entry)) {
            Add(entry);
        }

        Reset();
    }

    /// <summary>
    /// MouseUp fired without a drag — commit the pending change now: the
    /// collapse to the clicked row, or the Ctrl toggle of it.
    /// </summary>
    public void CommitOnMouseUp() {
        if (_deferred && _deferredEntry is { } entry) {
            if (_toggle) {
                Toggle(entry);
            } else {
                Collapse(entry);
            }
        }
        Reset();
    }

    /// <summary>
    /// Clear the selection in whichever right-pane list is active for the
    /// current view mode. Called from Esc handling in the window.
    /// </summary>
    /// <summary>
    /// Clears the selection in the container that is on screen. Takes the
    /// container rather than the view mode: the caller already knows which
    /// one that is, and a per-mode list here would have to grow a case
    /// every time a view is added — silently doing nothing until somebody
    /// noticed.
    /// </summary>
    public static void ClearActive(ItemsControl? active) {
        switch (active) {
            case DataGrid details:
                details.UnselectAll();
                break;
            case ListBox list:
                list.UnselectAll();
                break;
        }
    }


    private void Collapse(FileSystemEntry entry) {
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

    private void Toggle(FileSystemEntry entry) {
        if (IsSelected(entry)) {
            Selection()?.Remove(entry);
        } else {
            Add(entry);
        }
    }

    private void Add(FileSystemEntry entry) {
        if (!IsSelected(entry)) {
            Selection()?.Add(entry);
        }
    }

    private bool IsSelected(FileSystemEntry entry) {
        return Selection()?.Contains(entry) == true;
    }

    /// <summary>The selected-items list of whichever container was pressed, or null.</summary>
    private System.Collections.IList? Selection() {
        return _deferredControl switch {
            DataGrid dg => dg.SelectedItems,
            ListBox lb => lb.SelectedItems,
            _ => null,
        };
    }

    private void Reset() {
        _deferred = false;
        _toggle = false;
        _deferredEntry = null;
        _deferredControl = null;
    }
}
