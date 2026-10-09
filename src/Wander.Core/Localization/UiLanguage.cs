namespace Wander.Core.Localization;

/// <summary>The interface language as the user picked it on the settings page; stored by name.</summary>
public enum UiLanguage {
    /// <summary>Whatever Windows is shown in - see <see cref="UiLanguages.Resolve"/>.</summary>
    System,
    Russian,
    English,
}


public static class UiLanguages {
    /// <summary>
    /// The culture the string table is read in - "ru" or "en" - for the
    /// user's <paramref name="choice"/> on a Windows shown in
    /// <paramref name="systemLanguage"/> (two letters, "ru", "de"...).
    /// Following Windows means Russian for a Russian Windows and English for
    /// every other one: of the two languages Wander speaks, English is the
    /// one a German or a Spanish reader is likelier to read.
    /// </summary>
    public static string Resolve(UiLanguage choice, string systemLanguage) {
        return choice switch {
            UiLanguage.Russian => "ru",
            UiLanguage.English => "en",
            _ => string.Equals(systemLanguage, "ru", StringComparison.OrdinalIgnoreCase) ? "ru" : "en",
        };
    }
}
