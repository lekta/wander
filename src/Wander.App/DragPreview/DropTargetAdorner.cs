using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using Wander.App.Resources;

namespace Wander.App.DragPreview;

/// <summary>
/// Renders a translucent rounded outline over the adorned element to mark it
/// as the current drop target. Hit-test transparent so it doesn't interfere
/// with the drag-over event flow.
/// </summary>
public sealed class DropTargetAdorner : Adorner {
    private static readonly Brush _fill = Palette.DropTargetFill;
    private static readonly Pen _stroke = Palette.Stroke(Palette.DropTargetStroke, 2);


    public DropTargetAdorner(UIElement adornedElement) : base(adornedElement) {
        IsHitTestVisible = false;
    }


    protected override void OnRender(DrawingContext drawingContext) {
        var rect = new Rect(AdornedElement.RenderSize);
        drawingContext.DrawRoundedRectangle(_fill, _stroke, rect, 3, 3);
    }
}
