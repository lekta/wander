namespace Wander.Core.Localization;

/// <summary>
/// Where user-visible text comes from. Core produces a handful of strings the
/// user reads — context-menu labels, the reason a drop is refused — but the
/// string table itself lives in the app layer (a resx), and Core must not
/// reference it. So Core asks through this, and the app answers.
/// </summary>
public interface ITextSource {
    /// <summary>
    /// The text filed under <paramref name="key"/>. Implementations return
    /// the key itself when it is missing: a visibly wrong label beats an
    /// exception thrown while a menu is opening.
    /// </summary>
    string Get(string key);
}


/// <summary>
/// Convenience over the registered <see cref="ITextSource"/>. Nothing is
/// registered in tests, and then the key comes back unchanged — which keeps
/// the catalog's drift guards meaningful (a missing key is still visibly not
/// a label) without every test having to set up localisation.
/// </summary>
public static class Text {
    public static string Get(string key) {
        return ServiceLocator.TryGet<ITextSource>() is { } source ? source.Get(key) : key;
    }


    /// <summary>
    /// Formats the text under <paramref name="key"/> with
    /// <paramref name="args"/>. Falls back to the key when nothing is
    /// registered, so a format with no placeholders is returned as-is
    /// instead of throwing.
    /// </summary>
    public static string Format(string key, params object[] args) {
        string template = Get(key);
        try {
            return string.Format(template, args);
        } catch (FormatException) {
            return template;
        }
    }

    /// <summary>
    /// The text under <paramref name="key"/> for a count - "1 папка",
    /// "3 папки", "5 папок". See <see cref="PluralForm"/> for how the
    /// resource spells its forms.
    /// </summary>
    public static string Plural(string key, long count) {
        return PluralForm(Get(key), count);
    }

    /// <summary>
    /// Picks one of the forms in <paramref name="forms"/> - separated by
    /// <c>|</c>, in the order one / few / many, each free to carry
    /// <c>{0}</c> for the count - by the Russian rule: 1, 21, 101 take the
    /// first; 2-4, 22-24 the second; the rest, 11-14 included, the third.
    /// A resource with fewer forms uses its last one for the missing ones,
    /// so a word that does not change ("видео") is written once.
    /// </summary>
    public static string PluralForm(string forms, long count) {
        string[] parts = forms.Split('|');
        string form = parts[Math.Min(PluralIndex(count), parts.Length - 1)];
        try {
            return string.Format(form, count);
        } catch (FormatException) {
            return form;
        }
    }


    private static int PluralIndex(long count) {
        long n = Math.Abs(count);
        long lastTwo = n % 100;
        long last = n % 10;
        if (lastTwo is >= 11 and <= 14) {
            return 2;
        }

        return last switch {
            1 => 0,
            >= 2 and <= 4 => 1,
            _ => 2,
        };
    }
}
