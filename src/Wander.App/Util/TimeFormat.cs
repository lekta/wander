namespace Wander.App.Util;

/// <summary>
/// One decision about how Wander writes a moment to people:
/// <c>yyyy-MM-dd HH:mm</c>, sortable and unambiguous, the same in the list
/// column, the preview footer and the conflict dialog.
///
/// <para>
/// The pattern was written out at four places that did not know about each
/// other, and two of them also carried their own idea of what an absent
/// date looks like. Both live here now.
/// </para>
/// </summary>
public static class TimeFormat {
    /// <summary>The pattern itself, so XAML bindings share it too.</summary>
    public const string Pattern = "yyyy-MM-dd HH:mm";


    /// <summary>
    /// A UTC stamp shown in the user's own time zone. <see cref="DateTime.MinValue"/>
    /// means "the file system did not tell us" and prints as an em dash
    /// rather than as the year 1.
    /// </summary>
    public static string FromUtc(DateTime utc) {
        return utc == DateTime.MinValue ? "—" : utc.ToLocalTime().ToString(Pattern);
    }


    /// <summary>
    /// A moment that is already local and must not be converted — an EXIF
    /// capture time is written by the camera in its own local time, with no
    /// zone recorded to convert from.
    /// </summary>
    public static string Local(DateTime local) {
        return local.ToString(Pattern);
    }
}
