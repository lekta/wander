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
/// What a phone is opened for is the camera's pictures, the downloads, and
/// the books and music the owner put at the root (decision of 2026-10-19).
/// <c>Android</c> itself stays, and in it <c>media</c>: that is where
/// WhatsApp and Telegram keep their pictures; everything else in it is the
/// apps' (<c>data</c>, <c>obb</c>, OnePlus's <c>obj</c>). An empty folder
/// that is not one of the standard ones stays too: by its name it cannot be
/// told from one of the owner's, and asking every folder of the root
/// whether it is empty costs up to 0.7 s each on a OnePlus (stand Y4a).
/// </para>
/// </summary>
public static class DeviceClutter {
    /// <summary>At the storage's root, always: what the system keeps for itself - on memory cards, mostly.</summary>
    private static readonly string[] _systemRoot = { "LOST.DIR" };

    /// <summary>Under <c>Android</c> at the root: the one folder of the owner's.</summary>
    private const string AndroidMedia = "media";

    /// <summary>At the root, when empty: the standard folders Android makes for media that never came.</summary>
    private static readonly string[] _emptyStandard = { "Alarms", "Notifications", "Ringtones", "Podcasts", "Audiobooks", "Recordings" };


    /// <summary>
    /// Whether a folder of a device's storage is clutter.
    /// </summary>
    /// <param name="segments">Its path inside the storage, a name per level: <c>["Android", "data"]</c>.</param>
    /// <param name="isEmpty">
    /// Whether it holds nothing - nothing but names starting with a dot,
    /// which are clutter themselves; null when not asked: on a phone each
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
            return Is(_systemRoot, segments[0]) || (isEmpty == true && IsEmptyStandard(segments[0]));
        }

        return segments.Count == 2 && Same(segments[0], "Android") && !Same(segments[1], AndroidMedia);
    }

    /// <summary>
    /// A folder at the root whose emptiness decides: the one question worth
    /// a listing. Asked before the listing is made, so the others are never
    /// opened.
    /// </summary>
    public static bool IsEmptyStandard(string name) {
        return Is(_emptyStandard, name);
    }

    /// <summary>Empty as <see cref="IsClutter"/> means it: nothing in it, or only names starting with a dot.</summary>
    public static bool LooksEmpty(IEnumerable<string> names) {
        return names.All(n => n.StartsWith('.'));
    }


    private static bool Is(string[] names, string name) {
        return names.Any(n => Same(n, name));
    }

    private static bool Same(string a, string b) {
        return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }
}
