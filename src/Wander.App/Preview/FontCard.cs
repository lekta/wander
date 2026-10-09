using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using Wander.App.Resources;

namespace Wander.App.Preview;

/// <summary>One line of a font's samples: the text, and the size it is set in.</summary>
public sealed record FontSample(double Size, string Text);


/// <summary>A font file as the pane shows it: the face to set text in, its names, its samples.</summary>
public sealed record FontFace(
    FontFamily Family, FontStyle Style, FontWeight Weight, FontStretch Stretch,
    string Title, IReadOnlyList<PreviewFact> Facts, IReadOnlyList<FontSample> Samples);


/// <summary>
/// The card of a font file: the family and face names, version, designer,
/// glyph count and copyright from its name table (WPF's
/// <see cref="GlyphTypeface"/>), then a pangram at sizes from text to
/// display, then the letters and figures it has. Only what the font has is
/// set: WPF would take a character it lacks from a fallback font, and the
/// sample would show glyphs that are not this font's. The pangram goes
/// whole or not at all; the alphabet lines keep the characters the font
/// has. A font with none of them - icons; a symbol font, whose pictures
/// stand in for letters by code page - shows the first of the characters
/// it does have.
/// </summary>
internal static class FontCard {
    private const string EnglishSample = "The quick brown fox jumps over the lazy dog";

    private const double AlphabetSize = 20;

    /// <summary>How many of its own characters a font without letters shows.</summary>
    private const int OwnCharacters = 64;

    /// <summary>To 48: the 72 under it was taller than the pane (2026-10-09).</summary>
    private static readonly double[] _sizes = { 12, 16, 24, 32, 48 };

    private static readonly string[] _alphabet = {
        "АБВГДЕЁЖЗИЙКЛМНОПРСТУФХЦЧШЩЪЫЬЭЮЯ",
        "абвгдеёжзийклмнопрстуфхцчшщъыьэюя",
        "ABCDEFGHIJKLMNOPQRSTUVWXYZ",
        "abcdefghijklmnopqrstuvwxyz",
        "0123456789",
        ".,:;!?()[]{}«»“”—–-+=*/\\@#$%&",
    };


    /// <summary>
    /// The first face of the font at <paramref name="path"/> - a copy the
    /// caller made, alone in its folder, as WPF does not let a font file go.
    /// Null when it is not a font WPF reads.
    ///
    /// <para>
    /// The face is read off the file itself (<see cref="GlyphTypeface"/>);
    /// the family to set text in, off its folder - which is why the copy is
    /// alone there: <c>Fonts.GetTypefaces</c> given a file lists the whole
    /// folder, simulated bold and italic included (stand 2026-10-09).
    /// </para>
    /// </summary>
    public static FontFace? Read(string path) {
        GlyphTypeface glyphs;
        FontFamily? familyFace;
        try {
            glyphs = new GlyphTypeface(new Uri(path));
            familyFace = Fonts.GetFontFamilies(new Uri(path)).FirstOrDefault();
        } catch (Exception ex) when (
            ex is ArgumentException or IOException or UriFormatException or UnauthorizedAccessException or NullReferenceException) {
            return null;
        }
        if (familyFace is null) {
            return null;
        }

        string family = Name(glyphs.Win32FamilyNames) ?? Name(glyphs.FamilyNames) ?? Path.GetFileNameWithoutExtension(path);
        string style = Name(glyphs.Win32FaceNames) ?? Name(glyphs.FaceNames) ?? "";
        int faces = CollectionSize(path);

        var facts = new List<PreviewFact>();
        Add(facts, Strings.PreviewFontFace, style);
        Add(facts, Strings.PreviewFontVersion, Name(glyphs.VersionStrings));
        Add(facts, Strings.PreviewFontMaker, Name(glyphs.DesignerNames) ?? Name(glyphs.ManufacturerNames));
        Add(facts, Strings.PreviewFontGlyphs, glyphs.GlyphCount.ToString("N0", CultureInfo.CurrentCulture));
        if (faces > 1) {
            Add(facts, Strings.PreviewFontFaces, faces.ToString(CultureInfo.CurrentCulture));
        }
        // The first line: past it, a font's notice turns into licences.
        Add(facts, Strings.PreviewFontCopyright, Name(glyphs.Copyrights)?.Split('\n')[0].Trim());

        var samples = new List<FontSample>();
        string pangram = Covers(glyphs, Strings.PreviewFontSample) ? Strings.PreviewFontSample : EnglishSample;
        if (!glyphs.Symbol && Covers(glyphs, pangram)) {
            samples.AddRange(_sizes.Select(size => new FontSample(size, pangram)));
        }
        foreach (string line in glyphs.Symbol ? [] : _alphabet) {
            string covered = string.Concat(line.Where(c => Has(glyphs, c)));
            if (covered.Length > 0) {
                samples.Add(new FontSample(AlphabetSize, covered));
            }
        }
        if (samples.Count == 0 && OwnLine(glyphs) is { Length: > 0 } own) {
            samples.Add(new FontSample(AlphabetSize, own));
        }

        return new FontFace(familyFace, glyphs.Style, glyphs.Weight, glyphs.Stretch,
            style.Length > 0 ? $"{family} {style}" : family, facts, samples);
    }


    /// <summary>Faces in a TrueType collection (<c>ttcf</c> header); 1 for a single font.</summary>
    private static int CollectionSize(string path) {
        try {
            using var file = File.OpenRead(path);
            var head = new byte[12];
            if (file.Read(head, 0, 12) == 12 && head[0] == 't' && head[1] == 't' && head[2] == 'c' && head[3] == 'f') {
                return (head[8] << 24) | (head[9] << 16) | (head[10] << 8) | head[11];
            }
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
            // Read once already: a second read failing changes nothing shown.
        }

        return 1;
    }


    /// <summary>The name in the interface's language, else in English, else the first the font has.</summary>
    private static string? Name(IDictionary<CultureInfo, string> names) {
        if (names.Count == 0) {
            return null;
        }
        if (names.TryGetValue(CultureInfo.CurrentUICulture, out string? local) && local.Length > 0) {
            return local.Trim();
        }
        if (names.TryGetValue(CultureInfo.GetCultureInfo("en-US"), out string? english) && english.Length > 0) {
            return english.Trim();
        }

        return names.Values.FirstOrDefault(v => v.Length > 0)?.Trim();
    }

    /// <summary>
    /// The first characters the font maps, a space between: what an icon or
    /// symbol font has instead of letters. Controls, spaces and characters
    /// mapped to the empty glyph are skipped.
    /// </summary>
    private static string OwnLine(GlyphTypeface glyphs) {
        var own = glyphs.CharacterToGlyphMap
            .Where(pair => pair.Value != 0 && pair.Key > 0x20 && !IsControlOrSpace(pair.Key))
            .Select(pair => pair.Key)
            .Order()
            .Take(OwnCharacters)
            .Select(char.ConvertFromUtf32);

        return string.Join(" ", own);
    }

    private static bool IsControlOrSpace(int code) {
        return code is (>= 0x7F and <= 0xA0) or (>= 0xD800 and <= 0xDFFF) or 0x2028 or 0x2029
            || (code <= 0xFFFF && char.IsWhiteSpace((char)code));
    }

    private static bool Covers(GlyphTypeface glyphs, string text) {
        return text.All(c => char.IsWhiteSpace(c) || Has(glyphs, c));
    }

    /// <summary>Whether the font draws <paramref name="c"/> itself - mapped, and not to glyph 0, the "missing" box.</summary>
    private static bool Has(GlyphTypeface glyphs, char c) {
        return glyphs.CharacterToGlyphMap.TryGetValue(c, out ushort glyph) && glyph != 0;
    }

    private static void Add(List<PreviewFact> facts, string label, string? value) {
        if (!string.IsNullOrWhiteSpace(value)) {
            facts.Add(new PreviewFact(label, value));
        }
    }
}
