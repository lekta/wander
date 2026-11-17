using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace Wander.App.Preview;

/// <summary>
/// How a model's parts are painted, by the switches under the viewport.
/// Lit: the colour under the scene's lights plus a white shine - the shine
/// is added to the colour, not multiplied by it, so a near-black material
/// still shows its volume. Flat: the colour as it is, no shading, to judge
/// the colours themselves. Without the file's paint the model is clay, one
/// light grey. Back faces in signal pink show faces wound the wrong way and
/// the inside of open places. Materials are made on the dispatcher when a switch flips: a
/// handful per model, no file read again.
/// </summary>
internal static class ModelLook {
    /// <summary>The reverse of a face, a share of the front's brightness: inconsistent winding reads as the inside of a solid.</summary>
    private const byte BackLevel = 184;

    /// <summary>How tight the shine is: higher is a smaller, sharper spot.</summary>
    private const double ShinePower = 30;

    /// <summary>The signal back's own glow, a share of its colour: it stays pink in a cavity's shadow.</summary>
    private const byte SignalGlow = 89;


    public static readonly Brush Clay = Frozen(Color.FromRgb(0xC2, 0xC2, 0xC2));

    private static readonly Brush _shine = Frozen(Color.FromRgb(0x80, 0x80, 0x80));
    private static readonly Brush _black = Frozen(Colors.Black);
    private static readonly Brush _inkLight = Frozen(Color.FromRgb(0xB4, 0xB4, 0xB4));
    private static readonly Brush _inkDark = Frozen(Color.FromRgb(0x30, 0x30, 0x30));
    private static readonly Brush _signal = Frozen(Color.FromRgb(0xFF, 0x4F, 0xA3));


    public static Material Front(Brush surface, bool lit) {
        return lit ? Group(new DiffuseMaterial(surface), new SpecularMaterial(_shine, ShinePower)) : Flat(surface, back: false);
    }


    public static Material Back(Brush surface, bool lit, bool signal) {
        if (signal) {
            return lit ? Group(new DiffuseMaterial(_signal), Glow(_signal, SignalGlow)) : Flat(_signal, back: false);
        }
        if (!lit) {
            return Flat(surface, back: true);
        }

        var material = new DiffuseMaterial(surface) { Color = Color.FromRgb(BackLevel, BackLevel, BackLevel) };
        material.Freeze();

        return material;
    }


    /// <summary>The wireframe's strips: one flat tone, light over a dark model, dark over a light one.</summary>
    public static Material Ink(bool light) {
        return Flat(light ? _inkLight : _inkDark, back: false);
    }


    /// <summary>
    /// Unlit: emissive light is added over what is behind, so a black
    /// diffuse goes under it to hide the background and the lights.
    /// </summary>
    private static Material Flat(Brush surface, bool back) {
        return Group(new DiffuseMaterial(_black), back ? Glow(surface, BackLevel) : new EmissiveMaterial(surface));
    }


    private static EmissiveMaterial Glow(Brush surface, byte level) {
        return new EmissiveMaterial(surface) { Color = Color.FromRgb(level, level, level) };
    }


    private static Material Group(params Material[] materials) {
        var group = new MaterialGroup();
        foreach (var material in materials) {
            group.Children.Add(material);
        }
        group.Freeze();

        return group;
    }


    private static Brush Frozen(Color colour) {
        var brush = new SolidColorBrush(colour);
        brush.Freeze();

        return brush;
    }
}
