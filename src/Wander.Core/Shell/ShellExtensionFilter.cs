namespace Wander.Core.Shell;

/// <summary>
/// The text filter over the context-menu table in settings (2026-09-25): a
/// row passes when its caption, its application or one of its file types
/// holds the text - what the three columns show, nothing hidden.
///
/// <para>
/// An extension - text that starts with a dot, ".mp4" - also finds the rows
/// every file gets: "все файлы" and "файлы и папки" are in the menu of an
/// .mp4 as much as the .mp4 rows are, and "what is in that menu" is the
/// question a type is typed to ask. Without the dot the text is only text:
/// "mp4" could as well be part of a name.
/// </para>
/// </summary>
public static class ShellExtensionFilter {
    /// <param name="row">A row of the table.</param>
    /// <param name="query">What the field holds; empty lets every row through.</param>
    /// <param name="scopeTitle">What a scope is called in the table - <see cref="ShellScopes.Title"/>.</param>
    public static bool Matches(ShellExtensionRow row, string? query, Func<string, string> scopeTitle) {
        string needle = (query ?? string.Empty).Trim();
        if (needle.Length == 0) {
            return true;
        }

        if (Contains(row.Title, needle)
            || Contains(row.AppName, needle)
            || row.Scopes.Any(scope => Contains(scopeTitle(scope), needle))) {
            return true;
        }

        return IsExtension(needle) && row.Scopes.Any(CoversEveryFile);
    }


    private static bool IsExtension(string needle) {
        return needle.Length > 1 && needle[0] == '.' && !needle.Contains(' ');
    }

    private static bool CoversEveryFile(string scope) {
        return string.Equals(scope, ShellScopes.AllFiles, StringComparison.OrdinalIgnoreCase)
            || string.Equals(scope, ShellScopes.AllFilesystemObjects, StringComparison.OrdinalIgnoreCase);
    }

    private static bool Contains(string haystack, string needle) {
        return haystack.Contains(needle, StringComparison.CurrentCultureIgnoreCase);
    }
}
