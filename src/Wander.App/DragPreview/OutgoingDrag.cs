using System.IO;
using System.Windows;
using System.Windows.Input;
using Wander.App.Converters;
using Wander.App.Resources;
using Wander.App.Util;
using Wander.Core.FileSystem;
using Wander.Core.Icons;

namespace Wander.App.DragPreview;

/// <summary>
/// A drag that started in Wander, for as long as it lasts: the plaque that
/// follows the cursor, the cursor itself, and what both of them say about
/// where the drop would land.
///
/// <para>
/// The gesture that <em>starts</em> a drag belongs to whatever the user
/// grabbed — the file list or a tree row — and stays there. Running the
/// drag does not: the preview window, the effect calculation and the
/// wording are one pipeline shared by every source, and a window is not
/// where it belongs.
/// </para>
///
/// <para>
/// Where a drop would land is <see cref="DropTargetController"/>'s answer,
/// not ours: this type only reads it. The one thing it cannot see for
/// itself is the bookmarks strip lighting up, which the window owns and
/// reports through <c>refreshOnFinish</c>.
/// </para>
/// </summary>
public sealed class OutgoingDrag {
    private readonly DropTargetController _drops;
    private readonly Action _clearBookmarkTarget;

    private DragPreviewWindow? _preview;
    private int _pathCount;
    private string? _firstName;


    /// <param name="drops">Where a drop would land, and what it would do.</param>
    /// <param name="clearBookmarkTarget">
    /// Puts the bookmarks strip back to idle when the drag ends. Owned by
    /// the window, because the strip is part of its chrome.
    /// </param>
    public OutgoingDrag(DropTargetController drops, Action clearBookmarkTarget) {
        _drops = drops;
        _clearBookmarkTarget = clearBookmarkTarget;
    }


    /// <summary>
    /// Runs one drag from <paramref name="src"/> and returns when the user
    /// has let go. Blocking is not ours to choose — WPF's
    /// <c>DoDragDrop</c> pumps its own message loop until the drop.
    /// </summary>
    public void Run(DependencyObject src, string[] paths, string[] payload) {
        _pathCount = paths.Length;
        _firstName = Path.GetFileName(paths[0].TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        _drops.Clear();

        var preview = new DragPreviewWindow();
        preview.SetIcon(IconConverter.Load(paths[0], IconSize.Normal));
        preview.SetCount(paths.Length);
        // What is in hand, and nothing about what would happen to it: the
        // cursor has not been anywhere yet. The first GiveFeedback re-words
        // this within a frame, but the plaque is on screen before then —
        // long enough to be read on the first drag of a session, when the
        // window and the icon are still being made.
        preview.SetAction(DragAction.Forbidden, DescribeDragged(paths.Length), null);
        preview.Show();
        preview.MoveToCursor();
        _preview = preview;

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
            _clearBookmarkTarget();
            preview.Close();
            _preview = null;
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

        if (_preview is null) {
            return;
        }
        _preview.MoveToCursor();
        UpdateForCurrentTarget();
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


    /// <summary>
    /// Re-reads the target and re-words the plaque. Called on every mouse
    /// move during the drag, and by the window whenever the bookmarks strip
    /// takes or gives up the drag — that is a change of target the mouse
    /// does not report.
    /// </summary>
    public void UpdateForCurrentTarget() {
        if (_preview is null) {
            return;
        }

        DragAction action;
        string desc;
        string? targetText = null;
        int count = _pathCount;

        // Dropping into the bookmarks region is not a file operation at all,
        // so none of the copy/move/link vocabulary applies. Without this the
        // plaque kept showing whatever the last real target had offered —
        // "Переместить … в Downloads" while hovering the bookmarks strip.
        if (_drops.IsBookmarkTarget) {
            Show();
            _preview.SetAction(DragAction.Link, FormatBookmarkDesc(count), null);

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
                Show();
                action = DragAction.None;
                desc = DescribeDragged(count);
            } else if (_drops.SelfDropReason != SelfDropReason.None && _drops.Target is not null) {
                Show();
                action = DragAction.Forbidden;
                desc = PathSafety.FormatReason(_drops.SelfDropReason, _drops.SelfDropOffender, _drops.Target);
            } else {
                Hide();

                return;
            }
        } else {
            Show();
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

        _preview.SetAction(action, desc, targetText);
    }


    private void Show() {
        if (_preview is { Visibility: not Visibility.Visible } p) {
            p.Visibility = Visibility.Visible;
        }
    }

    private void Hide() {
        if (_preview is { Visibility: Visibility.Visible } p) {
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
            ? string.Format(Strings.DragOneItem, _firstName)
            : string.Format(Strings.DragItems, count);
    }


    private string FormatBookmarkDesc(int count) {
        return string.Format(Strings.DragAddToBookmarks, DescribeDragged(count));
    }
}
