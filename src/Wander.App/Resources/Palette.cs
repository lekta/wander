using System.Windows;
using System.Windows.Media;


namespace Wander.App.Resources;

/// <summary>
/// The code-behind's way into <c>Resources/Palette.xaml</c>.
///
/// <para>
/// Most colours reach the screen through <c>{StaticResource}</c> and never
/// come near C#. The ones here cannot: an adorner draws with a
/// <see cref="Pen"/>, a plaque picks its colour from a drag verb, a focus
/// outline is switched on and off from a focus handler. They still come out
/// of the same dictionary - a brush built in C# is exactly the corner that
/// stays light when the rest of the window goes dark.
/// </para>
///
/// <para>
/// Every field is <c>static readonly</c> on one class on purpose: touching
/// any one of them resolves all of them, so a mistyped key fails loudly on
/// the first repaint - which the <c>--smoke</c> run performs - rather than
/// on the one gesture nobody tried.
/// </para>
/// </summary>
internal static class Palette {
    /// <summary>Where the keyboard is: the outline round the active zone.</summary>
    public static readonly Brush FocusOutline = Find("FocusOutline");

    /// <summary>The "drop a folder here" strip under the bookmarks, idle...</summary>
    public static readonly Brush DropZoneFill = Find("DropZoneFill");

    public static readonly Brush DropZoneGlyph = Find("DropZoneGlyph");

    /// <summary>...and with a drag held over it.</summary>
    public static readonly Brush DropZoneActiveFill = Find("DropZoneActiveFill");

    public static readonly Brush DropZoneActiveGlyph = Find("DropZoneActiveGlyph");

    /// <summary>The lasso dragged across the list.</summary>
    public static readonly Brush MarqueeFill = Find("MarqueeFill");

    public static readonly Brush MarqueeStroke = Find("MarqueeStroke");

    /// <summary>The outline round whatever a drop would land on.</summary>
    public static readonly Brush DropTargetFill = Find("DropTargetFill");

    public static readonly Brush DropTargetStroke = Find("DropTargetStroke");

    /// <summary>What the drag leaving Wander would do, on the plaque under the cursor.</summary>
    public static readonly Brush DragMove = Find("DragMove");

    public static readonly Brush DragCopy = Find("DragCopy");

    public static readonly Brush DragLink = Find("DragLink");

    public static readonly Brush DragForbidden = Find("DragForbidden");

    /// <summary>A rejected name in the rename prompt, and the box it was typed into.</summary>
    public static readonly Brush TextError = Find("TextError");

    public static readonly Brush InputBorderError = Find("InputBorderError");

    /// <summary>The five colour labels, in the index order both sidecar formats use.</summary>
    public static readonly Brush ColorLabel1 = Find("ColorLabel1");

    public static readonly Brush ColorLabel2 = Find("ColorLabel2");

    public static readonly Brush ColorLabel3 = Find("ColorLabel3");

    public static readonly Brush ColorLabel4 = Find("ColorLabel4");

    public static readonly Brush ColorLabel5 = Find("ColorLabel5");

    /// <summary>How full the drive is, as the capacity bar fills up.</summary>
    public static readonly Brush VolumeBarNormal = Find("VolumeBarNormal");

    public static readonly Brush VolumeBarFilling = Find("VolumeBarFilling");

    public static readonly Brush VolumeBarFull = Find("VolumeBarFull");


    /// <summary>
    /// A frozen pen from a palette brush - what the adorners draw their
    /// outlines with. Frozen because nothing about it ever changes, and an
    /// unfrozen pen costs a change subscription on every render pass.
    /// </summary>
    public static Pen Stroke(Brush brush, double thickness) {
        var pen = new Pen(brush, thickness);
        pen.Freeze();

        return pen;
    }


    private static Brush Find(string key) {
        return (Brush)Application.Current.FindResource(key);
    }
}
