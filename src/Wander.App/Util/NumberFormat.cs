using System.Globalization;

namespace Wander.App.Util;

/// <summary>
/// One decision about how Wander writes numbers to people: digits are
/// grouped by threes, and the group separator is a narrow no-break space.
///
/// <para>
/// "36465 файлов" is a number nobody reads — the eye counts the digits
/// instead of taking in the size. Explorer groups them and so does every
/// other place a person meets a file count. The Russian culture's own
/// separator is a full no-break space, which is wide enough to read as two
/// numbers; a narrow one (U+202F) separates without breaking the number
/// apart. No-break rather than a plain thin space, so a count never wraps
/// across a line in the middle.
/// </para>
///
/// <para>
/// Applied by <see cref="Install"/> to the whole process rather than by a
/// helper called at each site: the format specifier belongs in the
/// resource string (<c>{0:N0}</c>), where whoever writes the string can
/// see it, and the separator is a property of the application, not of one
/// label. Everything that already formats with <c>N</c> — the mesh
/// summary, the census — picks it up for free.
/// </para>
/// </summary>
public static class NumberFormat {
    /// <summary>U+202F NARROW NO-BREAK SPACE.</summary>
    public const string GroupSeparator = "\u202F";


    /// <summary>
    /// Makes the group separator the app's. Called once at startup, before
    /// any thread that might format a number is started — a culture
    /// assigned to <see cref="CultureInfo.DefaultThreadCurrentCulture"/>
    /// is inherited by the thread pool, so the background passes format the
    /// same way the UI thread does.
    ///
    /// <para>
    /// Only the grouping is touched. Parsing is unaffected: nothing in
    /// Wander parses a number with <c>AllowThousands</c> — the mesh and
    /// model readers use <see cref="CultureInfo.InvariantCulture"/> because
    /// they read files, and the settings fields parse plain integers.
    /// </para>
    /// </summary>
    public static void Install() {
        CultureInfo.DefaultThreadCurrentCulture = WithGroupSeparator(CultureInfo.CurrentCulture);
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentCulture = CultureInfo.DefaultThreadCurrentCulture;
    }


    private static CultureInfo WithGroupSeparator(CultureInfo source) {
        var culture = (CultureInfo)source.Clone();
        culture.NumberFormat.NumberGroupSeparator = GroupSeparator;
        culture.NumberFormat.PercentGroupSeparator = GroupSeparator;

        return culture;
    }
}
