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
    /// <summary>The entry a click landed on, or null when it missed every row.</summary>
    public static FileSystemEntry? EntryAt(object originalSource) {
        var hit = originalSource as DependencyObject;
        while (hit is not null) {
            if (hit is FrameworkElement fe && fe.DataContext is FileSystemEntry entry) {
                return entry;
            }
            hit = VisualTreeHelper.GetParent(hit);
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
        var hit = originalSource as DependencyObject;
        while (hit is not null) {
            switch (hit) {
                case ScrollBar:
                case DataGridColumnHeader:
                case DataGridColumnHeadersPresenter:
                case Thumb:
                    return true;
            }
            hit = VisualTreeHelper.GetParent(hit);
        }

        return false;
    }


    /// <summary>True when the click landed inside the inline rename editor.</summary>
    public static bool IsInsideTextBox(object originalSource) {
        var hit = originalSource as DependencyObject;
        while (hit is not null) {
            if (hit is TextBox) {
                return true;
            }
            hit = VisualTreeHelper.GetParent(hit);
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
