using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace Wander.App.Controls;

/// <summary>
/// The inline rename editor, laid over the name label of the row being
/// renamed.
///
/// <para>
/// One TextBox for the whole list instead of a collapsed one in every row.
/// A TextBox is the most expensive control a row template can carry - its
/// own ScrollViewer, caret, undo stack, input bindings - and every tile
/// and every table row used to instantiate one for the single row per
/// session that actually gets edited. Measured at 21 visuals per tile with
/// it and 5 without (PLAN R, session log of 2026-09-02).
/// </para>
///
/// <para>
/// An adorner rather than an overlay positioned by hand: it lives in the
/// scroll viewport's adorner layer, so it follows the row when the list
/// scrolls and is clipped with it. The editor's box is the label's box -
/// the name is edited where it is read - grown to the editor's own height
/// when the label's line is shorter than a TextBox needs.
/// </para>
/// </summary>
public sealed class RenameAdorner : Adorner {
    private readonly TextBox _editor;


    public RenameAdorner(UIElement label, TextBox editor) : base(label) {
        _editor = editor;
        AddVisualChild(editor);
        AddLogicalChild(editor);
    }


    protected override int VisualChildrenCount => 1;


    protected override Visual GetVisualChild(int index) {
        return _editor;
    }


    protected override Size MeasureOverride(Size constraint) {
        var box = AdornedElement.RenderSize;
        _editor.Measure(new Size(box.Width, double.PositiveInfinity));

        return new Size(box.Width, Math.Max(box.Height, _editor.DesiredSize.Height));
    }


    protected override Size ArrangeOverride(Size finalSize) {
        _editor.Arrange(new Rect(finalSize));

        return finalSize;
    }
}
