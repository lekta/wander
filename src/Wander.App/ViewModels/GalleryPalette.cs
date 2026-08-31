using System.Windows;
using System.Windows.Media;
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
/// This is also the first piece of the dark theme on the roadmap: the
/// content area already switches palette rather than one hard-coded colour,
/// so the rest of the window can join it later instead of this having to be
/// torn out.
/// </para>
/// </summary>
public sealed class GalleryPalette {
    /// <summary>
    /// Explorer's own three, used as-is on the light background so the
    /// gallery matches the other views exactly where it can. Only the
    /// darkened backgrounds need colours of their own.
    /// </summary>
    private static readonly Color _explorerHover = Color.FromRgb(0xE5, 0xF3, 0xFB);
    private static readonly Color _explorerHoverBorder = Color.FromRgb(0xD2, 0xEC, 0xF8);
    private static readonly Color _explorerSelected = Color.FromRgb(0xCC, 0xE8, 0xFF);
    private static readonly Color _explorerSelectedBorder = Color.FromRgb(0xAF, 0xD9, 0xF2);
    private static readonly Color _explorerInactive = Color.FromRgb(0xE8, 0xE8, 0xE8);
    private static readonly Color _explorerInactiveBorder = Color.FromRgb(0xDC, 0xDC, 0xDC);


    public GalleryPalette(GalleryBackground background, int greyLevel, int darkLevel) {
        Kind = background;

        // "Light" is the window's own background rather than a white of our
        // choosing: the point of that option is that the gallery stops
        // looking like a separate application inside the window.
        Color surface = background switch {
            GalleryBackground.Grey => Grey(greyLevel),
            GalleryBackground.Dark => Grey(darkLevel),
            _ => SystemColors.WindowColor,
        };

        Background = Frozen(surface);

        // Both text tones are measured against the surface rather than
        // picked from a pair of constants. The constants worked at the ends
        // of the range and failed in the middle: on the mid grey — the
        // default, and the one photographers actually use — #AAA measured
        // 2.2:1, which is not dim text, it is absent text. A mid tone is
        // the hard case for exactly this reason, and it is the one a fixed
        // pair cannot cover.
        bool onDark = Luminance(surface) < 0.55;
        Color ink = onDark ? Grey(0xFF) : Grey(0x00);
        Foreground = Frozen(Quietest(ink, surface, PrimaryContrast));
        Dim = Frozen(Quietest(ink, surface, SecondaryContrast));

        if (!onDark) {
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
        Hover = Frozen(Lift(surface, 14));
        HoverBorder = Frozen(Lift(surface, 26));
        Selected = Frozen(Lift(surface, 34, blue: 14));
        SelectedBorder = Frozen(Lift(surface, 56, blue: 26));
        SelectedInactive = Frozen(Lift(surface, 22));
        SelectedInactiveBorder = Frozen(Lift(surface, 38));
    }


    /// <summary>
    /// The untinted palette: the window's own background and text on it.
    /// What every view except the gallery is drawn on, and therefore what
    /// the preview pane follows outside the gallery — see
    /// <c>MainViewModel.ContentPalette</c>.
    /// </summary>
    public static GalleryPalette Plain { get; } = new(GalleryBackground.Light, 0, 0);


    /// <summary>
    /// The surface colour one option would give, without building the rest
    /// of the palette — what the strip's three buttons paint themselves
    /// with. A button that shows the colour it sets needs no label.
    /// </summary>
    public static Brush Swatch(GalleryBackground kind, int greyLevel, int darkLevel) {
        return new GalleryPalette(kind, greyLevel, darkLevel).Background;
    }


    /// <summary>Which of the three the user picked — for the toolbar's checked state.</summary>
    public GalleryBackground Kind { get; }

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
    /// The most restrained tone between <paramref name="ink"/> and the
    /// surface that still reads at <paramref name="target"/>. Steps toward
    /// the surface as far as it can and stops; on a surface where even
    /// undiluted ink cannot reach the target, it returns the ink, which is
    /// the best available.
    /// </summary>
    private static Color Quietest(Color ink, Color surface, double target) {
        for (int percent = 60; percent > 0; percent -= 4) {
            var candidate = Mix(ink, surface, percent / 100.0);
            if (Contrast(candidate, surface) >= target) {
                return candidate;
            }
        }

        return ink;
    }

    /// <summary><paramref name="amount"/> of the way from <paramref name="to"/> to <paramref name="from"/>.</summary>
    private static Color Mix(Color from, Color to, double amount) {
        return Color.FromRgb(
            (byte)Math.Round((to.R * amount) + (from.R * (1 - amount))),
            (byte)Math.Round((to.G * amount) + (from.G * (1 - amount))),
            (byte)Math.Round((to.B * amount) + (from.B * (1 - amount))));
    }

    /// <summary>WCAG contrast ratio, 1:1 to 21:1.</summary>
    private static double Contrast(Color a, Color b) {
        double la = RelativeLuminance(a);
        double lb = RelativeLuminance(b);

        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    private static double RelativeLuminance(Color c) {
        return (0.2126 * Linear(c.R)) + (0.7152 * Linear(c.G)) + (0.0722 * Linear(c.B));
    }

    private static double Linear(byte value) {
        double v = value / 255.0;

        return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
    }


    private static Color Grey(int level) {
        byte v = (byte)Math.Clamp(level, 0, 255);

        return Color.FromRgb(v, v, v);
    }

    private static Color Lift(Color from, int by, int blue = 0) {
        return Color.FromRgb(
            (byte)Math.Clamp(from.R + by, 0, 255),
            (byte)Math.Clamp(from.G + by, 0, 255),
            (byte)Math.Clamp(from.B + by + blue, 0, 255));
    }

    /// <summary>Perceived lightness, 0…1 — Rec. 601 weights, which is plenty for a light/dark decision.</summary>
    private static double Luminance(Color color) {
        return ((0.299 * color.R) + (0.587 * color.G) + (0.114 * color.B)) / 255.0;
    }

    private static Brush Frozen(Color color) {
        var brush = new SolidColorBrush(color);
        brush.Freeze();

        return brush;
    }
}
