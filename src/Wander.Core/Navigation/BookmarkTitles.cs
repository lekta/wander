using Wander.Core.FileSystem;

namespace Wander.Core.Navigation;

/// <summary>
/// The user's own names for bookmarks (decision of 2026-10-19): "R8 DCIM"
/// over a camera's <c>DCIM</c>, "Отчёты" over <c>2026\Q3\final</c> - by the
/// bookmark's path. The name is the bookmark's, not the folder's: the
/// folder keeps its own, and set back to that or to nothing, the bookmark
/// reads as its folder again.
///
/// <para>
/// A name goes where its folder goes when Wander moves or renames it
/// (<see cref="Follow"/>), and with the bookmark when it is removed. Kept
/// apart from the list of bookmarks in <c>state.json</c> - an older Wander
/// writes that file whole and would drop a block it does not know
/// (BACKLOG AD11); the names have a file of their own it never touches.
/// </para>
/// </summary>
public sealed class BookmarkTitles {
    private readonly Dictionary<string, string> _titles = new(StringComparer.OrdinalIgnoreCase);


    /// <summary>Every name there is, by path - what is written to disk.</summary>
    public IReadOnlyDictionary<string, string> All => new Dictionary<string, string>(_titles, StringComparer.OrdinalIgnoreCase);


    /// <summary>The names read back; a blank one is no name.</summary>
    public void Load(IReadOnlyDictionary<string, string>? titles) {
        _titles.Clear();
        foreach (var (path, title) in titles ?? new Dictionary<string, string>()) {
            if (!string.IsNullOrWhiteSpace(path) && !string.IsNullOrWhiteSpace(title)) {
                _titles[Key(path)] = title.Trim();
            }
        }
    }

    /// <summary>The bookmark's own name, or null when it reads as its folder.</summary>
    public string? Of(string path) {
        return _titles.TryGetValue(Key(path), out string? title) ? title : null;
    }

    /// <summary>
    /// Names the bookmark on <paramref name="path"/>; a blank name, or the
    /// folder's own, takes the bookmark's name away.
    /// </summary>
    /// <returns>True when what the bookmark is called changed.</returns>
    public bool Set(string path, string? title, string folderName) {
        string key = Key(path);
        string? wanted = string.IsNullOrWhiteSpace(title) || string.Equals(title.Trim(), folderName, StringComparison.Ordinal)
            ? null
            : title.Trim();
        string? had = Of(key);
        if (string.Equals(had, wanted, StringComparison.Ordinal)) {
            return false;
        }

        if (wanted is null) {
            _titles.Remove(key);
        } else {
            _titles[key] = wanted;
        }

        return true;
    }

    /// <summary>The bookmark went: its name with it. True when it had one.</summary>
    public bool Forget(string path) {
        return _titles.Remove(Key(path));
    }

    /// <summary>
    /// A folder Wander moved or renamed: the names of the bookmarks on it,
    /// or inside it, go with them - the rule of <see cref="PathRewrite.Under"/>.
    /// </summary>
    /// <returns>True when any name moved.</returns>
    public bool Follow(string oldRoot, string newRoot) {
        var moved = new List<(string From, string To)>();
        foreach (string path in _titles.Keys) {
            if (PathRewrite.Under(path, oldRoot, newRoot) is { } to && !string.Equals(to, path, StringComparison.OrdinalIgnoreCase)) {
                moved.Add((path, Key(to)));
            }
        }
        foreach (var (from, to) in moved) {
            string title = _titles[from];
            _titles.Remove(from);
            _titles[to] = title;
        }

        return moved.Count > 0;
    }

    /// <summary>
    /// Whether a bookmark has a row in the panel now (decision of
    /// 2026-10-19): one on a phone or a camera only while that device is
    /// plugged in - not greyed like a flash drive's, simply not there, and
    /// back with the device; any other always.
    /// </summary>
    /// <param name="plugged">The names of the devices plugged in.</param>
    public static bool IsShown(string path, IReadOnlyCollection<string> plugged) {
        return DevicePath.Parse(path) is not { } device
            || plugged.Contains(device.Device, StringComparer.OrdinalIgnoreCase);
    }


    private static string Key(string path) {
        string trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return trimmed.Length == 0 ? path : trimmed;
    }
}
