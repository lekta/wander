using System.Text;

namespace Wander.Site;

/// <summary>
/// Two ways to turn a heading into an anchor. The site's own slugs are
/// transliterated, so a page's address reads in Latin and survives being
/// pasted anywhere; GitHub's are what links inside GUIDE.md use, because
/// GitHub is the other place the file is read.
/// </summary>
internal static class Slugs {
    /// <summary>Cyrillic U+0430..U+044F in alphabet order; hard and soft signs vanish.</summary>
    private static readonly string[] _latin = {
        "a", "b", "v", "g", "d", "e", "zh", "z", "i", "y", "k", "l", "m", "n", "o", "p",
        "r", "s", "t", "u", "f", "kh", "ts", "ch", "sh", "shch", "", "y", "", "e", "yu", "ya",
    };


    /// <summary>
    /// Cyrillic spelled in Latin, Latin letters and digits kept, anything
    /// else one hyphen: the hotkeys page comes out as "goryachie-klavishi".
    /// </summary>
    public static string Translit(string title) {
        var slug = new StringBuilder();
        foreach (char c in title.ToLowerInvariant()) {
            if (c is >= 'а' and <= 'я') {
                slug.Append(_latin[c - 'а']);
            } else if (c == 'ё') {
                slug.Append('e');
            } else if (c is >= 'a' and <= 'z' or >= '0' and <= '9') {
                slug.Append(c);
            } else if (slug.Length > 0 && slug[^1] != '-') {
                slug.Append('-');
            }
        }

        return slug.ToString().TrimEnd('-');
    }

    /// <summary>
    /// The anchor GitHub gives a heading (github-slugger): lower case,
    /// punctuation dropped, every space a hyphen, and a repeat of an earlier
    /// anchor numbered -1, -2 in file order. <paramref name="seen"/> carries
    /// the count through the file.
    /// </summary>
    public static string GitHub(string title, Dictionary<string, int> seen) {
        var slug = new StringBuilder();
        foreach (char c in title.ToLowerInvariant()) {
            if (char.IsLetterOrDigit(c) || c is '-' or '_') {
                slug.Append(c);
            } else if (c == ' ') {
                slug.Append('-');
            }
        }

        string original = slug.ToString();
        string result = original;
        while (seen.ContainsKey(result)) {
            seen[original]++;
            result = original + "-" + seen[original];
        }
        seen[result] = 0;

        return result;
    }
}
