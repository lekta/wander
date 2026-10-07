using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Wander.App.Controls;

/// <summary>
/// The whole text as a tooltip on a <see cref="TextBlock"/> that is cut with
/// an ellipsis, and no tooltip at all when nothing is cut (2026-09-28): a tip
/// that repeats what is under the cursor is noise.
///
/// <para>
/// Decided when the mouse comes in, when the width the text got is known -
/// a table column, a path beside a button. With no tip of its own the text
/// lets its row's tip show through, which is what a table of settings keeps
/// its descriptions on.
/// </para>
/// </summary>
public static class TrimmedToolTip {
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
        "Enabled", typeof(bool), typeof(TrimmedToolTip), new PropertyMetadata(false, OnEnabledChanged));

    public static void SetEnabled(DependencyObject element, bool value) {
        element.SetValue(EnabledProperty, value);
    }

    public static bool GetEnabled(DependencyObject element) {
        return (bool)element.GetValue(EnabledProperty);
    }


    private static void OnEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
        if (d is not TextBlock text) {
            return;
        }

        text.MouseEnter -= OnMouseEnter;
        if ((bool)e.NewValue) {
            text.MouseEnter += OnMouseEnter;
        }
    }

    private static void OnMouseEnter(object sender, MouseEventArgs e) {
        var text = (TextBlock)sender;
        if (IsCut(text)) {
            text.ToolTip = text.Text;
        } else {
            text.ClearValue(FrameworkElement.ToolTipProperty);
        }
    }

    /// <summary>
    /// Whether <paramref name="text"/> shows less than all of itself: one
    /// line, cut with an ellipsis. Wrapped text is taken as whole - the
    /// gallery's selected cell grows to its name.
    /// </summary>
    public static bool IsCut(TextBlock text) {
        if (string.IsNullOrEmpty(text.Text) || text.TextWrapping != TextWrapping.NoWrap) {
            return false;
        }

        var typeface = new Typeface(text.FontFamily, text.FontStyle, text.FontWeight, text.FontStretch);
        var whole = new FormattedText(
            text.Text, CultureInfo.CurrentCulture, text.FlowDirection, typeface, text.FontSize,
            text.Foreground, VisualTreeHelper.GetDpi(text).PixelsPerDip);
        double room = text.ActualWidth - text.Padding.Left - text.Padding.Right;

        // Half a pixel of slack: layout rounding leaves a text that fits a
        // hair wider than the room it was measured into.
        return whole.WidthIncludingTrailingWhitespace > room + 0.5;
    }
}
