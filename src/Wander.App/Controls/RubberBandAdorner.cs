using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace Wander.App.Controls;

/// <summary>
/// Draws the translucent selection rectangle ("rubber band" / "marquee")
/// that users drag across a list to lasso-select multiple items.
///
/// Hosted in the <see cref="AdornerLayer"/> of the list control, so it
/// floats above items without disturbing their layout. The pen / fill
/// colours match the Windows 11 accent for selection.
/// </summary>
internal sealed class RubberBandAdorner : Adorner {

    // Soft fill so item icons remain readable through the marquee; the
    // 1-pixel stroke pins down the rectangle's edges.
    private static readonly Brush _fillBrush;
    private static readonly Pen _strokePen;

    static RubberBandAdorner() {
        _fillBrush = new SolidColorBrush(Color.FromArgb(40, 0, 120, 215));
        _fillBrush.Freeze();
        var strokeBrush = new SolidColorBrush(Color.FromArgb(180, 0, 120, 215));
        strokeBrush.Freeze();
        _strokePen = new Pen(strokeBrush, 1.0);
        _strokePen.Freeze();
    }


    public Point StartPoint { get; set; }
    public Point CurrentPoint { get; set; }


    public RubberBandAdorner(UIElement adornedElement) : base(adornedElement) {
        IsHitTestVisible = false;       // never block clicks through the marquee
    }


    /// <summary>Normalised rectangle between Start and Current, in the adorned element's coords.</summary>
    public Rect GetRect() {
        return new Rect(StartPoint, CurrentPoint);
    }


    protected override void OnRender(DrawingContext dc) {
        var rect = GetRect();
        if (rect.Width < 1 || rect.Height < 1) {
            return;
        }
        dc.DrawRectangle(_fillBrush, _strokePen, rect);
    }
}
