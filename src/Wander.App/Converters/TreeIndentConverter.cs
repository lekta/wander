using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace Wander.App.Converters;

/// <summary>
/// Depth of a <see cref="TreeViewItem"/> as a left margin.
///
/// <para>
/// WPF's stock TreeViewItem template indents by nesting: each level puts
/// its children inside a grid column that starts further right, so the
/// selection highlight starts further right too. Explorer highlights the
/// whole row edge to edge, which is what the eye follows when scanning a
/// deep tree — and it is what makes the entire row clickable rather than
/// just the label.
/// </para>
///
/// <para>
/// Wander's template therefore lets the row background span the full
/// width and indents only the content inside it. That content needs to
/// know how deep it sits, and only the visual tree knows: this walks up
/// the chain of TreeViewItem parents and counts.
/// </para>
/// </summary>
public sealed class TreeIndentConverter : IValueConverter {
    /// <summary>Pixels per level. Matches the stock template's step.</summary>
    private const double Step = 16;


    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture) {
        int depth = 0;
        var parent = value is DependencyObject start ? VisualTreeHelper.GetParent(start) : null;
        while (parent is not null) {
            if (parent is TreeViewItem) {
                depth++;
            } else if (parent is TreeView) {
                break;
            }
            parent = VisualTreeHelper.GetParent(parent);
        }

        return new Thickness(depth * Step, 0, 0, 0);
    }


    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) {
        throw new NotSupportedException();
    }
}
