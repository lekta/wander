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

        bool onDark = Luminance(surface) < 0.55;
        Foreground = Frozen(onDark ? Grey(0xEE) : Grey(0x11));
        Dim = Frozen(onDark ? Grey(0xAA) : Grey(0x77));

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
