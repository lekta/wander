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
        return ServiceLocator.IsRegistered<ITextSource>()
            ? ServiceLocator.Get<ITextSource>().Get(key)
            : key;
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
}
