using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace Wander.App.DragPreview;

/// <summary>
/// Renders a translucent rounded outline over the adorned element to mark it
/// as the current drop target. Hit-test transparent so it doesn't interfere
/// with the drag-over event flow.
/// </summary>
public sealed class DropTargetAdorner : Adorner {
    private static readonly Brush _fill;
    private static readonly Pen _stroke;


    static DropTargetAdorner() {
        _fill = new SolidColorBrush(Color.FromArgb(60, 0, 120, 215));
        _fill.Freeze();
        var strokeBrush = new SolidColorBrush(Color.FromArgb(220, 0, 120, 215));
        strokeBrush.Freeze();
        _stroke = new Pen(strokeBrush, 2);
        _stroke.Freeze();
    }


    public DropTargetAdorner(UIElement adornedElement) : base(adornedElement) {
        IsHitTestVisible = false;
    }


    protected override void OnRender(DrawingContext drawingContext) {
        var rect = new Rect(AdornedElement.RenderSize);
        drawingContext.DrawRoundedRectangle(_fill, _stroke, rect, 3, 3);
    }
}
