using System.Windows;
using System.Windows.Media;


namespace Wander.App.Resources;

/// <summary>
/// The code-behind's way into the palette (<c>Resources/Palette*.xaml</c>).
///
/// <para>
/// Most colours reach the screen through a resource reference in XAML and
/// never come near C#. The ones here cannot: an adorner draws with a
/// <see cref="Pen"/>, a plaque picks its colour from a drag verb, a focus
/// outline is switched on and off from a focus handler. They still come out
/// of the same dictionaries - a brush built in C# is exactly the corner that
/// stays light when the rest of the window goes dark.
/// </para>
///
/// <para>
/// Two kinds. A colour no theme changes (Palette.xaml) is a
/// <see cref="Brush"/>, resolved once. A colour of the theme is a
/// <see cref="ThemeKey"/>: the brush behind it is replaced when the theme
/// switches, so it is painted by reference (<see cref="ThemeKey.Paint"/>)
/// and the element repaints with the rest; copying the brush would keep the
/// old theme on that one element.
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
    // --- The theme's -------------------------------------------------------------------------

    /// <summary>Where the keyboard is: the outline round the active zone.</summary>
    public static readonly ThemeKey FocusOutline = Themed("FocusOutline");

    /// <summary>The "drop a folder here" strip under the bookmarks, idle...</summary>
    public static readonly ThemeKey DropZoneFill = Themed("DropZoneFill");

    public static readonly ThemeKey DropZoneGlyph = Themed("DropZoneGlyph");

    /// <summary>...and with a drag held over it.</summary>
    public static readonly ThemeKey DropZoneActiveFill = Themed("DropZoneActiveFill");

    public static readonly ThemeKey DropZoneActiveGlyph = Themed("DropZoneActiveGlyph");

    /// <summary>A rejected name in the rename prompt, and the box it was typed into.</summary>
    public static readonly ThemeKey TextError = Themed("TextError");

    public static readonly ThemeKey InputBorderError = Themed("InputBorderError");

    /// <summary>A dialog's own surface - what the dialogs built in code are drawn on.</summary>
    public static readonly ThemeKey ChromeBackground = Themed("ChromeBackground");

    /// <summary>What a listing is drawn on - the gallery's background when it follows the window.</summary>
    public static readonly ThemeKey ContentBackground = Themed("ContentBackground");

    /// <summary>The preview pane's surface - what a code colour has to read on.</summary>
    public static readonly ThemeKey PreviewBackground = Themed("PreviewBackground");

    /// <summary>The list's own highlights, which the gallery takes while its background follows the window.</summary>
    public static readonly ThemeKey RowHover = Themed("RowHover");

    public static readonly ThemeKey RowHoverBorder = Themed("RowHoverBorder");

    public static readonly ThemeKey RowSelected = Themed("RowSelected");

    public static readonly ThemeKey RowSelectedBorder = Themed("RowSelectedBorder");

    public static readonly ThemeKey RowSelectedInactive = Themed("RowSelectedInactive");

    public static readonly ThemeKey RowSelectedInactiveBorder = Themed("RowSelectedInactiveBorder");


    // --- No theme changes these --------------------------------------------------------------

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

    /// <summary>What the review helpers mark a photograph with: edges in focus, crushed shadows, autofocus frames.</summary>
    public static readonly Brush ReviewPeaking = Find("ReviewPeaking");

    public static readonly Brush ReviewUnder = Find("ReviewUnder");

    public static readonly Brush ReviewAfFocused = Find("ReviewAfFocused");

    public static readonly Brush ReviewAfOther = Find("ReviewAfOther");

    public static readonly Brush ReviewAfThumb = Find("ReviewAfThumb");

    /// <summary>The clock an icon wears while an operation works on its file (AsyncIcon.ShowsWork), and the plate under it.</summary>
    public static readonly Brush WorkBadgeGlyph = Find("WorkBadgeGlyph");

    public static readonly Brush WorkBadgeBackground = Find("WorkBadgeBackground");


    /// <summary>
    /// A frozen pen from a palette brush - what the adorners draw their
    /// outlines with. Frozen because nothing about it ever changes, and an
    /// unfrozen pen costs a change subscription on every render pass. For
    /// the colours no theme changes: a pen cannot follow a switch.
    /// </summary>
    public static Pen Stroke(Brush brush, double thickness) {
        var pen = new Pen(brush, thickness);
        pen.Freeze();

        return pen;
    }


    private static Brush Find(string key) {
        return (Brush)Application.Current.FindResource(key);
    }

    private static ThemeKey Themed(string key) {
        _ = Find(key);

        return new ThemeKey(key);
    }
}


/// <summary>
/// A colour of the theme, by its key. The brush behind the key is replaced
/// when the theme switches (<c>Util/InterfaceTheme</c>); an element painted
/// with <see cref="Paint"/> holds the key and repaints with the rest of the
/// window, where one handed <see cref="Current"/> would keep the old brush.
/// </summary>
internal readonly record struct ThemeKey(string Name) {
    /// <summary>The brush the key stands for in the theme shown now - for arithmetic on its colour, not for painting.</summary>
    public Brush Current => (Brush)Application.Current.FindResource(Name);

    /// <summary>Paints <paramref name="property"/> of <paramref name="element"/> by reference, following the theme from now on.</summary>
    public void Paint(FrameworkElement element, DependencyProperty property) {
        element.SetResourceReference(property, Name);
    }
}
