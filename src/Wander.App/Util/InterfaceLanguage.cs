using System.Globalization;
using Wander.App.Resources;
using Wander.Core.Localization;
using Wander.Core.Logging;

namespace Wander.App.Util;

/// <summary>
/// The language the interface speaks in this session: picked once at
/// startup, before the first string is read. Windows and menus take their
/// text when they are built (<c>{x:Static}</c>), so a change on the
/// settings page applies on the next start and says so.
///
/// <para>
/// Only the UI culture is set - the string table and .NET's own messages
/// follow it. Dates and numbers keep the user's regional format
/// (<see cref="NumberFormat"/>).
/// </para>
/// </summary>
public static class InterfaceLanguage {
    /// <summary>Windows' own display language, two letters; read before <see cref="Apply"/> replaces the UI culture.</summary>
    public static string Windows { get; private set; } = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;

    /// <summary>"ru" or "en": the language of this session's string table. Windows' choice until <see cref="Apply"/> - the harness never calls it.</summary>
    public static string Current { get; private set; } = UiLanguages.Resolve(UiLanguage.System, Windows);

    public static bool IsEnglish => Current == "en";

    /// <summary>What "follow Windows" means on this machine, in the language's own name - the settings page says it.</summary>
    public static string WindowsChoiceName => UiLanguages.Resolve(UiLanguage.System, Windows) == "en"
        ? Strings.SettingsLanguageEnglish
        : Strings.SettingsLanguageRussian;


    /// <summary>
    /// Sets the UI culture of this thread and of every thread started from
    /// now on. Called once, after the settings are readable and before
    /// anything reads a string.
    /// </summary>
    public static void Apply(UiLanguage choice) {
        Windows = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        Current = UiLanguages.Resolve(choice, Windows);
        var culture = CultureInfo.GetCultureInfo(Current);
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentUICulture = culture;
        Log.Info($"Interface language: {Current} (setting {choice}, Windows {Windows})");
    }
}
