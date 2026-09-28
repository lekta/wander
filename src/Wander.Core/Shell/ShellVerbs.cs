namespace Wander.Core.Shell;

/// <summary>
/// Canonical verbs Wander renders itself, and therefore drops from the
/// shell's contribution.
///
/// <para>
/// Dropping them is what keeps the menu from repeating Cut / Copy / Delete /
/// Properties two inches below Wander's own copies. Matching on the
/// canonical verb rather than the label is deliberate: labels are localised,
/// verbs are not.
/// </para>
///
/// <para>
/// It lives in Core because two very different places need the same list and
/// must never drift: the interop that filters the live menu, and the
/// registry scan behind the settings table — a row Wander will never draw
/// has no business offering the user a switch for it.
/// </para>
/// </summary>
public static class ShellVerbs {
    public static IReadOnlySet<string> Suppressed { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "open", "opennewwindow", "opennewprocess", "opennewtab", "explore", "openas",
            "cut", "copy", "paste", "pastelink", "delete", "rename", "link",
            "properties", "undo",
            "copyaspath", "windows.copyaspath", "windows.modernshare", "windows.share",
            // Windows 11 "Add to Favorites" — Wander has its own bookmarks panel.
            "pintohome", "pintohomefile",
            // Windows Terminal's "Open in Terminal" - release, Preview, Canary
            // (2026-09-28). Its verb is its CLSID (OpenTerminalHere.h in
            // microsoft/terminal). Wander's own row does the same and more:
            // it is in the header menu too and falls back to PowerShell.
            "{9F156763-7844-4DC4-B2B1-901F640F5155}",
            "{02DB545A-3E20-46DE-83A5-1329B1E88B6B}",
            "{6119575F-6918-4392-AF16-C2C627AF9416}",
        };


    public static bool IsSuppressed(string? verb) {
        return verb is not null && Suppressed.Contains(verb);
    }
}
