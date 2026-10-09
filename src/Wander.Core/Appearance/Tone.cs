namespace Wander.Core.Appearance;

/// <summary>A colour as three bytes - what the tone arithmetic works in, free of any UI framework's colour type.</summary>
public readonly record struct Rgb(byte R, byte G, byte B) {
    /// <summary>A neutral grey of the given lightness, 0...255, clamped.</summary>
    public static Rgb Grey(int level) {
        byte v = (byte)Math.Clamp(level, 0, 255);

        return new Rgb(v, v, v);
    }
}


/// <summary>
/// Arithmetic on colours the interface derives rather than picks: the
/// gallery's palette from its background, a code colour made for white paper
/// redrawn for a dark one. Contrast is WCAG's - the figure a reader's eye
/// cares about, not a difference of bytes.
/// </summary>
public static class Tone {
    /// <summary>
    /// Where a surface stops being light: below it, text on it is light and
    /// a highlight is a lift of the surface. Rec. 601 lightness, which is
    /// plenty for a light / dark decision; a mid grey of 128 is dark.
    /// </summary>
    public const double DarkBelow = 0.55;


    /// <summary>WCAG contrast ratio, 1:1 to 21:1.</summary>
    public static double Contrast(Rgb a, Rgb b) {
        double la = RelativeLuminance(a);
        double lb = RelativeLuminance(b);

        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    /// <summary>Whether text on <paramref name="surface"/> has to be light.</summary>
    public static bool IsDark(Rgb surface) {
        double lightness = ((0.299 * surface.R) + (0.587 * surface.G) + (0.114 * surface.B)) / 255.0;

        return lightness < DarkBelow;
    }

    /// <summary><paramref name="amount"/> of the way from <paramref name="from"/> to <paramref name="to"/>.</summary>
    public static Rgb Mix(Rgb from, Rgb to, double amount) {
        return new Rgb(
            (byte)Math.Round((to.R * amount) + (from.R * (1 - amount))),
            (byte)Math.Round((to.G * amount) + (from.G * (1 - amount))),
            (byte)Math.Round((to.B * amount) + (from.B * (1 - amount))));
    }

    /// <summary>
    /// The most restrained tone between <paramref name="ink"/> and the
    /// surface that still reads at <paramref name="target"/>. Steps toward
    /// the surface as far as it can and stops; on a surface where even
    /// undiluted ink cannot reach the target, it returns the ink, which is
    /// the best available.
    /// </summary>
    public static Rgb Quietest(Rgb ink, Rgb surface, double target) {
        for (int percent = 60; percent > 0; percent -= 4) {
            var candidate = Mix(ink, surface, percent / 100.0);
            if (Contrast(candidate, surface) >= target) {
                return candidate;
            }
        }

        return ink;
    }

    /// <summary>
    /// <paramref name="ink"/> as it is if it already reads on
    /// <paramref name="surface"/> at <paramref name="target"/>; otherwise
    /// moved toward the opposite end - white on a dark surface, black on a
    /// light one - only as far as it takes. The hue stays where it was, so a
    /// keyword stays blue and a string stays red; it just stops vanishing.
    /// </summary>
    public static Rgb Readable(Rgb ink, Rgb surface, double target) {
        var away = IsDark(surface) ? new Rgb(255, 255, 255) : new Rgb(0, 0, 0);
        for (int percent = 0; percent <= 100; percent += 5) {
            var candidate = Mix(ink, away, percent / 100.0);
            if (Contrast(candidate, surface) >= target) {
                return candidate;
            }
        }

        return away;
    }

    /// <summary>
    /// <paramref name="from"/> lifted by <paramref name="by"/> on every
    /// channel and by <paramref name="blue"/> more on blue - a highlight on a
    /// dark surface that is a lighter piece of it, not a colour laid over it.
    /// </summary>
    public static Rgb Lift(Rgb from, int by, int blue = 0) {
        return new Rgb(
            (byte)Math.Clamp(from.R + by, 0, 255),
            (byte)Math.Clamp(from.G + by, 0, 255),
            (byte)Math.Clamp(from.B + by + blue, 0, 255));
    }


    private static double RelativeLuminance(Rgb c) {
        return (0.2126 * Linear(c.R)) + (0.7152 * Linear(c.G)) + (0.0722 * Linear(c.B));
    }

    private static double Linear(byte value) {
        double v = value / 255.0;

        return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
    }
}
