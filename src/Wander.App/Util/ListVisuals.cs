using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Wander.Core.FileSystem;

namespace Wander.App.Util;

/// <summary>
/// Visual-tree questions the list views keep asking: what was clicked, is
/// it selectable furniture or the list's own chrome, and where is the
/// scroll viewer. Static because none of it holds state — it is arithmetic
/// on the tree that happens to need a walk.
/// </summary>
public static class ListVisuals {
    /// <summary>
    /// The element a mouse event landed on and everything above it, up to
    /// the root — the walk every question below is built on.
    ///
    /// <para>
    /// The step upwards is not always the visual parent, and that is the
    /// whole reason this exists. A click can land on a <c>Run</c> inside a
    /// <c>TextBlock</c> (the "kind" line of a tile is one), and a Run is a
    /// content element, not a visual: <see cref="VisualTreeHelper.GetParent"/>
    /// throws «"System.Windows.Documents.Run" is not a Visual or Visual3D»
    /// on it rather than returning null. Text elements are walked through
    /// the logical tree instead, which lands on the TextBlock that hosts
    /// them and rejoins the visual tree from there.
    /// </para>
    /// </summary>
    public static IEnumerable<DependencyObject> Ancestors(object? originalSource) {
        var hit = originalSource as DependencyObject;
        while (hit is not null) {
            yield return hit;
            hit = ParentOf(hit);
        }
    }

    private static DependencyObject? ParentOf(DependencyObject node) {
        return node is Visual or System.Windows.Media.Media3D.Visual3D
            ? VisualTreeHelper.GetParent(node)
            : LogicalTreeHelper.GetParent(node);
    }


    /// <summary>The entry a click landed on, or null when it missed every row.</summary>
    public static FileSystemEntry? EntryAt(object originalSource) {
        foreach (var hit in Ancestors(originalSource)) {
            if (hit is FrameworkElement fe && fe.DataContext is FileSystemEntry entry) {
                return entry;
            }
        }

        return null;
    }


    /// <summary>
    /// True when the click landed on the list's own furniture rather than
    /// on its contents — a scroll bar, a column header, a resize grip.
    ///
    /// <para>
    /// This is what separates "the user clicked empty space, start a
    /// marquee" from "the user grabbed the scroll bar". Without it,
    /// dragging the scroll thumb painted a selection rectangle and swept
    /// the selection along with it, and clicking a column header to sort
    /// cleared the selection first.
    /// </para>
    /// </summary>
    public static bool IsChrome(object originalSource) {
        foreach (var hit in Ancestors(originalSource)) {
            switch (hit) {
                case ScrollBar:
                case DataGridColumnHeader:
                case DataGridColumnHeadersPresenter:
                case Thumb:
                    return true;
            }
        }

        return false;
    }


    /// <summary>
    /// True when the click landed on a control rather than on the surface
    /// carrying it — a button, a text box, the "…" on a bookmark row. What
    /// separates "the user pressed this thing" from "the user grabbed the
    /// row it sits on".
    /// </summary>
    public static bool IsInsideControl(object originalSource) {
        foreach (var hit in Ancestors(originalSource)) {
            if (hit is ButtonBase or TextBoxBase) {
                return true;
            }
        }

        return false;
    }


    /// <summary>True when the click landed inside the inline rename editor.</summary>
    public static bool IsInsideTextBox(object originalSource) {
        foreach (var hit in Ancestors(originalSource)) {
            if (hit is TextBox) {
                return true;
            }
        }

        return false;
    }


    public static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++) {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) {
                return match;
            }
            if (FindDescendant<T>(child) is T deeper) {
                return deeper;
            }
        }

        return null;
    }


    /// <summary>
    /// Shift + wheel scrolls sideways — the convention everywhere from
    /// browsers to Explorer, which WPF's ScrollViewer does not implement on
    /// its own. Returns true when it handled the notch, so the caller can
    /// mark the event handled and stop the vertical scroll from also
    /// happening.
    /// </summary>
    public static bool TryShiftScrollHorizontally(DependencyObject scope, MouseWheelEventArgs e) {
        if (Keyboard.Modifiers != ModifierKeys.Shift) {
            return false;
        }

        var viewer = scope as ScrollViewer ?? FindDescendant<ScrollViewer>(scope);
        if (viewer is null || viewer.ScrollableWidth <= 0) {
            return false;
        }

        // Same step the ScrollViewer uses for a vertical notch, so both
        // axes feel the same under the finger.
        viewer.ScrollToHorizontalOffset(viewer.HorizontalOffset - e.Delta);

        return true;
    }
}
