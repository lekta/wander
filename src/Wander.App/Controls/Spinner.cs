using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Wander.App.Controls;

/// <summary>
/// The "still working" ring: a dashed circle turning once a
/// <see cref="Period"/>. One element in place of the twenty lines of
/// Ellipse, RotateTransform and Storyboard each spinner used to carry.
///
/// <para>
/// It turns only while it is on screen. A storyboard started from a
/// Loaded trigger runs for the life of the window, collapsed or not, and
/// five of those ticked at the frame rate under a window that showed none
/// of them. Here the animation starts when the element becomes visible -
/// its own Visibility or an ancestor's - and stops when it goes.
/// </para>
/// </summary>
public sealed class Spinner : FrameworkElement {
    /// <summary>The ring's brush.</summary>
    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke), typeof(Brush), typeof(Spinner),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>How thick the ring is; the dashes are three of these long with a gap of two.</summary>
    public static readonly DependencyProperty StrokeThicknessProperty = DependencyProperty.Register(
        nameof(StrokeThickness), typeof(double), typeof(Spinner),
        new FrameworkPropertyMetadata(3.0, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>What the ring is drawn over, when it needs a plate of its own; null for nothing.</summary>
    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(
        nameof(Fill), typeof(Brush), typeof(Spinner),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>One turn takes this long.</summary>
    public static readonly DependencyProperty PeriodProperty = DependencyProperty.Register(
        nameof(Period), typeof(TimeSpan), typeof(Spinner),
        new PropertyMetadata(TimeSpan.FromSeconds(1)));

    private static readonly DoubleCollection _dashes = [3, 2];

    private readonly RotateTransform _turn = new();


    public Spinner() {
        RenderTransform = _turn;
        RenderTransformOrigin = new Point(0.5, 0.5);
        // A spinner is never clicked; what is under it is.
        IsHitTestVisible = false;
        IsVisibleChanged += OnIsVisibleChanged;
    }


    public Brush? Stroke {
        get => (Brush?)GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public double StrokeThickness {
        get => (double)GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    public Brush? Fill {
        get => (Brush?)GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    public TimeSpan Period {
        get => (TimeSpan)GetValue(PeriodProperty);
        set => SetValue(PeriodProperty, value);
    }


    protected override void OnRender(DrawingContext drawingContext) {
        double thickness = StrokeThickness;
        double radius = Math.Min(ActualWidth, ActualHeight) / 2 - thickness / 2;
        if (radius <= 0) {
            return;
        }

        var pen = new Pen(Stroke, thickness) { DashStyle = new DashStyle(_dashes, 0) };
        pen.Freeze();
        drawingContext.DrawEllipse(Fill, pen, new Point(ActualWidth / 2, ActualHeight / 2), radius, radius);
    }


    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e) {
        if (e.NewValue is true) {
            _turn.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 360, Period) {
                RepeatBehavior = RepeatBehavior.Forever,
            });
        } else {
            _turn.BeginAnimation(RotateTransform.AngleProperty, null);
        }
    }
}
