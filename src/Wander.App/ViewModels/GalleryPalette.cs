using System.Windows.Media;
using Wander.App.Resources;
using Wander.Core.Appearance;
using Wander.Core.Persistence;

namespace Wander.App.ViewModels;

/// <summary>
/// Every colour the gallery draws, derived from one choice of background.
///
/// <para>
/// A palette rather than a background colour, because the rest cannot be
/// left behind. A dark surround with the light theme's near-black captions
/// is unreadable; a dark surround with Explorer's pale blue selection is a
/// row of lightboxes shouting over the photographs they are supposed to
/// frame. So the selection, the hover, the caption and the dim text are all
/// computed from the background, and there is no way to set one without the
/// others following.
/// </para>
///
/// <para>
/// The light background stays light in either theme (decision 2026-10-09):
/// the window's own white in the light one, a shade darker in the dark one
/// (<c>GalleryLightBackground</c> in the palette). What else sits in the
/// gallery - its scroll bar, the stars on a cell - follows the surface
/// through <see cref="IsDark"/> (<c>Util/ThemeScope</c>). The window's own
/// surface, whatever the theme, is <see cref="Plain"/>.
/// </para>
/// </summary>
public sealed class GalleryPalette {
    /// <summary>
    /// Contrast the caption tone has to clear. 4.5:1 is the usual floor for
    /// text this size.
    /// </summary>
    private const double PrimaryContrast = 4.5;

    /// <summary>
    /// …and for the quieter tone. Lower on purpose: it is meant to recede,
    /// and holding it to the same figure as the caption makes the two the
    /// same colour on a mid-toned surface, which loses the distinction the
    /// second tone exists for.
    /// </summary>
    private const double SecondaryContrast = 3.4;


    /// <summary>
    /// Explorer's own highlights, for a light surround: the light theme's
    /// row colours, kept here because a light surround is light in either
    /// theme - in the dark one the theme's rows are dark.
    /// </summary>
    private static readonly Color _explorerHover = Color.FromRgb(0xE5, 0xF3, 0xFB);
    private static readonly Color _explorerHoverBorder = Color.FromRgb(0xD2, 0xEC, 0xF8);
    private static readonly Color _explorerSelected = Color.FromRgb(0xCC, 0xE8, 0xFF);
    private static readonly Color _explorerSelectedBorder = Color.FromRgb(0xAF, 0xD9, 0xF2);
    private static readonly Color _explorerInactive = Color.FromRgb(0xE8, 0xE8, 0xE8);
    private static readonly Color _explorerInactiveBorder = Color.FromRgb(0xDC, 0xDC, 0xDC);


    public GalleryPalette(GalleryBackground background, int greyLevel, int darkLevel)
        : this(background, greyLevel, darkLevel, window: false) { }


    private GalleryPalette(GalleryBackground background, int greyLevel, int darkLevel, bool window) {
        Kind = background;

        Rgb surface = window
            ? ToRgb(Palette.ContentBackground.Current)
            : background switch {
                GalleryBackground.Grey => Rgb.Grey(greyLevel),
                GalleryBackground.Dark => Rgb.Grey(darkLevel),
                _ => ToRgb(Palette.GalleryLightBackground.Current),
            };

        Background = Frozen(surface);
        IsDark = Tone.IsDark(surface);

        // Both text tones are measured against the surface rather than
        // picked from a pair of constants. The constants worked at the ends
        // of the range and failed in the middle: on the mid grey — the
        // default, and the one photographers actually use — #AAA measured
        // 2.2:1, which is not dim text, it is absent text. A mid tone is
        // the hard case for exactly this reason, and it is the one a fixed
        // pair cannot cover.
        var ink = IsDark ? Rgb.Grey(0xFF) : Rgb.Grey(0x00);
        Foreground = Frozen(Tone.Quietest(ink, surface, PrimaryContrast));
        Dim = Frozen(Tone.Quietest(ink, surface, SecondaryContrast));

        if (!IsDark) {
            Hover = Frozen(_explorerHover);
            HoverBorder = Frozen(_explorerHoverBorder);
            Selected = Frozen(_explorerSelected);
            SelectedBorder = Frozen(_explorerSelectedBorder);
            SelectedInactive = Frozen(_explorerInactive);
            SelectedInactiveBorder = Frozen(_explorerInactiveBorder);

            return;
        }

        // On a dark surround the highlight is a *lift* of the surround
        // itself, not a colour laid over it: a fixed pale blue at these
        // levels is brighter than most of the photographs and the eye goes
        // to the frame instead of the picture. The active selection keeps a
        // blue lean so it still reads as "chosen" rather than "lighter".
        Hover = Frozen(Tone.Lift(surface, 14));
        HoverBorder = Frozen(Tone.Lift(surface, 26));
        Selected = Frozen(Tone.Lift(surface, 34, blue: 14));
        SelectedBorder = Frozen(Tone.Lift(surface, 56, blue: 26));
        SelectedInactive = Frozen(Tone.Lift(surface, 22));
        SelectedInactiveBorder = Frozen(Tone.Lift(surface, 38));
    }


    /// <summary>
    /// The untinted palette: the window's own background and text on it, in
    /// the theme shown now. What every view except the gallery is drawn on,
    /// and therefore what the preview pane follows outside the gallery —
    /// see <c>MainViewModel.ContentPalette</c>.
    /// </summary>
    public static GalleryPalette Plain => new(GalleryBackground.Light, 0, 0, window: true);


    /// <summary>Which of the three the user picked — for the toolbar's checked state.</summary>
    public GalleryBackground Kind { get; }

    /// <summary>Whether the surround is dark: what the gallery's other parts follow (<c>Util/ThemeScope</c>).</summary>
    public bool IsDark { get; }

    public Brush Background { get; }

    /// <summary>Caption under a picture.</summary>
    public Brush Foreground { get; }

    /// <summary>Secondary text — quieter than <see cref="Foreground"/>, still legible.</summary>
    public Brush Dim { get; }

    public Brush Hover { get; }
    public Brush HoverBorder { get; }

    /// <summary>Selected while the list has the keyboard.</summary>
    public Brush Selected { get; }

    public Brush SelectedBorder { get; }

    /// <summary>Selected while the keyboard is in another pane — the answer to "what does Delete mean".</summary>
    public Brush SelectedInactive { get; }

    public Brush SelectedInactiveBorder { get; }


    /// <summary>
    /// The surface colour one option would give, without building the rest
    /// of the palette — what the strip's three buttons paint themselves
    /// with. A button that shows the colour it sets needs no label.
    /// </summary>
    public static Brush Swatch(GalleryBackground kind, int greyLevel, int darkLevel) {
        return new GalleryPalette(kind, greyLevel, darkLevel).Background;
    }


    private static Rgb ToRgb(Brush brush) {
        return brush is SolidColorBrush solid ? new Rgb(solid.Color.R, solid.Color.G, solid.Color.B) : Rgb.Grey(0xFF);
    }

    private static Brush Frozen(Rgb color) {
        return Frozen(Color.FromRgb(color.R, color.G, color.B));
    }

    private static Brush Frozen(Color color) {
        var brush = new SolidColorBrush(color);
        brush.Freeze();

        return brush;
    }
}
