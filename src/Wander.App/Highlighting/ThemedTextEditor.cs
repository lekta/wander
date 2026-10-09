using System.Runtime.CompilerServices;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Rendering;
using Wander.App.Resources;
using Wander.App.Util;
using Wander.Core.Appearance;

namespace Wander.App.Highlighting;

/// <summary>
/// AvalonEdit's editor whose syntax colours follow the theme. The
/// definitions - AvalonEdit's own and Wander's <c>.xshd</c> - pick their
/// colours for white paper: navy keywords, dark green comments, which on a
/// dark one are dark on dark. Rather than a second set of every definition,
/// the colorizer moves each colour toward white until it reads, keeping its
/// hue (<see cref="CodeColors"/>); the definitions stay as they are.
/// </summary>
public sealed class ThemedTextEditor : TextEditor {
    /// <summary>After a switch of the theme: what is on screen is painted again in the new colours.</summary>
    public void Repaint() {
        TextArea.TextView.Redraw();
    }


    protected override IVisualLineTransformer CreateColorizer(IHighlightingDefinition highlightingDefinition) {
        return new ThemedColorizer(highlightingDefinition);
    }


    private sealed class ThemedColorizer(IHighlightingDefinition definition) : HighlightingColorizer(definition) {
        protected override void ApplyColorToElement(VisualLineElement element, HighlightingColor color) {
            base.ApplyColorToElement(element, InterfaceTheme.IsDark ? CodeColors.ForDark(color) : color);
        }
    }
}


/// <summary>
/// A syntax colour made for white paper, redrawn for the dark theme's: the
/// text colour as far toward white as it takes to read at 4.5:1 on the
/// preview's surface (<see cref="Tone.Readable"/>), a background tint
/// sunk most of the way into that surface. Worked out once per colour.
/// </summary>
internal static class CodeColors {
    private const double TextContrast = 4.5;

    /// <summary>How much of a background tint goes under the dark surface: enough left to see where a span starts.</summary>
    private const double TintSunk = 0.8;


    private static readonly ConditionalWeakTable<HighlightingColor, HighlightingColor> _dark = new();


    public static HighlightingColor ForDark(HighlightingColor color) {
        return _dark.GetValue(color, Redraw);
    }


    private static HighlightingColor Redraw(HighlightingColor color) {
        var paper = ToRgb(Palette.PreviewBackground.Current is SolidColorBrush surface ? surface.Color : Colors.Black);
        var copy = color.Clone();
        if (color.Foreground?.GetColor(null) is { } ink) {
            copy.Foreground = new SimpleHighlightingBrush(ToColor(Tone.Readable(ToRgb(ink), paper, TextContrast)));
        }
        if (color.Background?.GetColor(null) is { } tint) {
            copy.Background = new SimpleHighlightingBrush(ToColor(Tone.Mix(ToRgb(tint), paper, TintSunk)));
        }
        copy.Freeze();

        return copy;
    }

    private static Rgb ToRgb(Color color) {
        return new Rgb(color.R, color.G, color.B);
    }

    private static Color ToColor(Rgb color) {
        return Color.FromRgb(color.R, color.G, color.B);
    }
}
