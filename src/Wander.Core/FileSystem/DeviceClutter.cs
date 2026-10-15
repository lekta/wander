namespace Wander.Core.FileSystem;

/// <summary>
/// The folders of a phone's storage an ordinary user has no business in
/// (PLAN Y4b, request of 2026-10-15): the apps' own data, what the system
/// keeps for itself, and the standard folders Android makes and nobody
/// fills. They are hidden the way protected Windows files are - hidden and
/// system - and come back with "Защищённые системные файлы": the apps' data
/// is sometimes wanted (a WhatsApp backup, a game's saves), so it is out of
/// the way, not out of reach.
///
/// <para>
/// <c>Android</c> itself stays: <c>Android\media</c> is where WhatsApp and
/// Telegram keep their pictures. What stays besides - <c>DCIM</c>,
/// <c>Pictures</c>, <c>Download</c>, <c>Documents</c>, <c>Movies</c>,
/// <c>Music</c>, Quest's <c>Oculus</c> - and anything with something in it,
/// is not on any list. The list is a proposal checked by the stand on the
/// devices at hand (OnePlus 6T, Pixel 6, Quest 3).
/// </para>
/// </summary>
public static class DeviceClutter {
    /// <summary>At the storage's root, always: what the system keeps for itself.</summary>
    private static readonly string[] _systemRoot = { "LOST.DIR" };

    /// <summary>Under <c>Android</c> at the root: the apps' own data and their downloads.</summary>
    private static readonly string[] _appData = { "data", "obb" };

    /// <summary>At the root, when empty: the standard folders Android makes for media that never came.</summary>
    private static readonly string[] _emptyStandard = { "Alarms", "Notifications", "Ringtones", "Podcasts", "Audiobooks", "Recordings" };


    /// <summary>
    /// Whether a folder of a device's storage is clutter.
    /// </summary>
    /// <param name="segments">Its path inside the storage, a name per level: <c>["Android", "data"]</c>.</param>
    /// <param name="isEmpty">
    /// Whether it holds nothing; null when not asked - on a phone each
    /// question is a request over the cable, and a standard folder not
    /// known to be empty is left showing.
    /// </param>
    public static bool IsClutter(IReadOnlyList<string> segments, bool? isEmpty) {
        if (segments.Count == 0) {
            return false;
        }

        // Names starting with a dot, at any depth: .thumbnails, .trashed-*,
        // .face - Unix's own way of saying "not for you".
        if (segments[^1].StartsWith('.')) {
            return true;
        }

        if (segments.Count == 1) {
            return Is(_systemRoot, segments[0]) || (isEmpty == true && Is(_emptyStandard, segments[0]));
        }

        return segments.Count == 2 && Same(segments[0], "Android") && Is(_appData, segments[1]);
    }


    private static bool Is(string[] names, string name) {
        return names.Any(n => Same(n, name));
    }

    private static bool Same(string a, string b) {
        return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }
}
