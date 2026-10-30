using System.Collections.ObjectModel;
using System.IO;
using Wander.App.Resources;
using Wander.App.ViewModels;
using Wander.Core;
using Wander.Core.FileSystem;
using Wander.Core.Logging;
using Wander.Core.Navigation;
using Wander.Core.Panels;
using Wander.Core.Shell;

namespace Wander.App.Controllers;

/// <summary>
/// The left panel's bookmarks: the special folders the settings enable, and
/// the user's own list under them.
///
/// <para>
/// Owns the list of favourite paths and says what the panel's top rows are
/// (<see cref="BuildRows"/>) - values the window's model is handed whole
/// (<c>BookmarksChanged</c>); what is open under them, where the cursor is
/// and where the open folder is are the model's, kept by path, so building
/// the rows again loses none of it.
/// </para>
///
/// <para>
/// Everything the user should be told about goes out through
/// <see cref="StatusReported"/> and <see cref="Changed"/>. Picking a folder
/// from a dialog stays outside: that is a window's job, and this class only
/// gets told which path was chosen.
/// </para>
/// </summary>
public sealed class BookmarksController {
    private readonly IFileSystem _fs;
    private readonly SettingsViewModel _settings;
    private readonly ILogger _log;
    private readonly List<string> _favorites = new();
    // Of the favourites, those switched off on the bookmarks' page: kept, no row.
    private readonly HashSet<string> _hidden = new(StringComparer.OrdinalIgnoreCase);
    private readonly Special[] _specials;
    private List<string> _specialOrder;
    // The built-in folders as last resolved, switched on or not: what tells
    // a built-in row's path from any other - see HideSpecial, NameOf.
    private readonly Dictionary<string, Special> _specialByPath = new(StringComparer.OrdinalIgnoreCase);
    private readonly BookmarkTitles _titles = new();

    // The names of the phones and cameras plugged in: a bookmark on one has a
    // row only while it is (BookmarkTitles.IsShown). Told by the window, which
    // asks the devices off its thread.
    private IReadOnlyCollection<string> _plugged = Array.Empty<string>();


    public BookmarksController(IFileSystem fs, SettingsViewModel settings, ILogger log) {
        _fs = fs;
        _settings = settings;
        _log = log;
        _specials = new[] {
            new Special("Downloads", () => Strings.SpecialFolderDownloads, () => ResolveKnown(f => f.GetDownloads()), () => _settings.ShowBookmarkDownloads, on => _settings.ShowBookmarkDownloads = on),
            new Special("Documents", () => Strings.SpecialFolderDocuments, () => ResolveKnown(f => f.GetDocuments()), () => _settings.ShowBookmarkDocuments, on => _settings.ShowBookmarkDocuments = on),
            new Special("Pictures", () => Strings.SpecialFolderPictures, () => ResolveKnown(f => f.GetPictures()), () => _settings.ShowBookmarkPictures, on => _settings.ShowBookmarkPictures = on),
            new Special("Desktop", () => Strings.SpecialFolderDesktop, () => ResolveKnown(f => f.GetDesktop()), () => _settings.ShowBookmarkDesktop, on => _settings.ShowBookmarkDesktop = on),
            new Special("Music", () => Strings.SpecialFolderMusic, () => ResolveKnown(f => f.GetMusic()), () => _settings.ShowBookmarkMusic, on => _settings.ShowBookmarkMusic = on),
            new Special("Videos", () => Strings.SpecialFolderVideos, () => ResolveKnown(f => f.GetVideos()), () => _settings.ShowBookmarkVideos, on => _settings.ShowBookmarkVideos = on),
            new Special("RecycleBin", () => Strings.SpecialFolderRecycleBin, () => ServiceLocator.TryGet<IShellNamespace>() is null ? null : ShellPaths.RecycleBin, () => _settings.ShowBookmarkRecycleBin, on => _settings.ShowBookmarkRecycleBin = on),
        };
        _specialOrder = _specials.Select(s => s.Key).ToList();
    }


    /// <summary>Something to tell the user — already localised.</summary>
    public event EventHandler<string>? StatusReported;

    /// <summary>The list changed and is worth persisting - and the panel's rows are to be built again.</summary>
    public event EventHandler? Changed;


    /// <summary>The user's own bookmarks, in order — what gets persisted.</summary>
    public IReadOnlyList<string> Paths => _favorites;

    /// <summary>The bookmarks' own names - persisted apart from the list (<c>IBookmarkTitleStore</c>).</summary>
    public BookmarkTitles Titles => _titles;

    /// <summary>A bookmark on a phone or a camera is in the list: the devices are worth asking about.</summary>
    public bool HasDeviceBookmarks => _favorites.Any(DevicePath.Is);

    /// <summary>The user's own bookmarks switched off in the panel - what gets persisted.</summary>
    public IReadOnlyList<string> HiddenPaths => _favorites.Where(_hidden.Contains).ToArray();

    /// <summary>The built-in rows' order, by key - what gets persisted.</summary>
    public IReadOnlyList<string> SpecialOrder => _specialOrder;

    /// <summary>Every bookmark, for the bookmarks' page: built-in first, the hidden and the absent among them. Kept by <see cref="RefreshRows"/>.</summary>
    public ObservableCollection<BookmarkRowViewModel> Rows { get; } = new();


    /// <summary>Replaces the saved list and the names. Does not rebuild — the caller does.</summary>
    public void Load(
        IEnumerable<string> paths, IReadOnlyDictionary<string, string>? titles = null,
        IEnumerable<string>? hidden = null, IEnumerable<string>? specialOrder = null) {
        _favorites.Clear();
        _favorites.AddRange(paths);
        _titles.Load(titles);
        _hidden.Clear();
        _hidden.UnionWith((hidden ?? Array.Empty<string>()).Where(Contains));
        _specialOrder = BookmarkOrder.Merge(_specials.Select(s => s.Key).ToArray(), specialOrder);
    }

    /// <summary>
    /// The phones and cameras plugged in now, by name. True when that is
    /// news for a bookmark - the rows are then to be built again.
    /// </summary>
    public bool SetPlugged(IEnumerable<string> names) {
        var now = names.ToHashSet(StringComparer.OrdinalIgnoreCase);
        bool news = _favorites.Where(DevicePath.Is).Any(p => BookmarkTitles.IsShown(p, now) != BookmarkTitles.IsShown(p, _plugged));
        _plugged = now;

        return news;
    }

    /// <summary>What the bookmark on <paramref name="path"/> is called: its own name, else the built-in row's, else its folder's.</summary>
    public string NameOf(string path) {
        return _titles.Of(path) ?? DefaultNameOf(path);
    }

    /// <summary>
    /// What to call <paramref name="path"/> while the bookmark on it is what
    /// was chosen (2026-10-30): the name in the panel; null when no bookmark
    /// stands on it - a folder under one reads as itself.
    /// </summary>
    public string? TitleOf(string? path) {
        return string.IsNullOrEmpty(path) || (IndexOf(path) < 0 && !_specialByPath.ContainsKey(path)) ? null : NameOf(path);
    }

    /// <summary>
    /// Names a bookmark, a built-in one too (decisions of 2026-10-19,
    /// 2026-10-30) - the row's label, not the folder: the folder keeps its
    /// name. A blank name gives the default back. False when the path is no
    /// bookmark, or nothing changed.
    /// </summary>
    public bool Rename(string? path, string? title) {
        if (string.IsNullOrEmpty(path) || TitleOf(path) is null || !_titles.Set(path, title, DefaultNameOf(path))) {
            return false;
        }

        _log.Info($"Bookmark named: {path} -> {(_titles.Of(path) is { } name ? Log.Path(name) : "(its folder's name)")}");
        Changed?.Invoke(this, EventArgs.Empty);

        return true;
    }

    /// <summary>
    /// A drop would add <paramref name="path"/>: a folder not in the list
    /// yet or hidden in it, or something on a phone or a camera - whether
    /// that is a folder is asked on the drop (<see cref="Add"/>), not on
    /// every move of the mouse.
    /// </summary>
    public bool CanTake(string path) {
        return (!Contains(path) || _hidden.Contains(path)) && (DevicePath.Is(path) || Directory.Exists(path));
    }


    /// <summary>
    /// The panel's top rows as the settings and the list say now: the
    /// special folders switched on, then the user's own, a rule above the
    /// first of them when there is a special folder to divide it from.
    /// </summary>
    public IReadOnlyList<PanelRow> BuildRows() {
        var rows = new List<PanelRow>();
        foreach (var (special, path) in ResolveSpecials()) {
            if (special.IsOn()) {
                rows.Add(SpecialRow(special, path));
            }
        }

        // The divider goes on the first user bookmark, and only when there
        // is a special folder above it to be divided from.
        bool startsSection = rows.Count > 0;
        foreach (string path in _favorites.Where(HasRow)) {
            if (BuildFolderRow(path, startsSection) is { } row) {
                rows.Add(row);
                startsSection = false;
            }
        }

        return rows;
    }


    /// <summary>
    /// Brings <see cref="Rows"/> in step with the list and the settings, the
    /// row objects kept by path: the page's selection and keyboard stay on
    /// them. Asked only while the page is on screen - it goes to the disk,
    /// off the UI thread (<see cref="ProbeAsync"/>).
    /// </summary>
    public void RefreshRows() {
        var specials = ResolveSpecials().ToArray();
        var wanted = new List<BookmarkRowViewModel>();
        for (int i = 0; i < specials.Length; i++) {
            var (special, path) = specials[i];
            var row = RowFor(path, isBuiltIn: true, on => special.Switch(on));
            row.Update(NameOf(path), path, PlaceText(path), special.IsOn(), isDeviceAway: false, startsSection: false,
                canMoveUp: i > 0, canMoveDown: i < specials.Length - 1);
            wanted.Add(row);
        }
        for (int i = 0; i < _favorites.Count; i++) {
            string path = _favorites[i];
            var row = RowFor(path, isBuiltIn: false, on => SetShown(path, on));
            row.Update(NameOf(path), DevicePath.Parse(path) is { } device ? DevicePath.RootOf(device.Device) : path, PlaceText(path),
                !_hidden.Contains(path), !BookmarkTitles.IsShown(path, _plugged), startsSection: i == 0 && specials.Length > 0,
                canMoveUp: i > 0, canMoveDown: i < _favorites.Count - 1);
            wanted.Add(row);
        }

        for (int i = Rows.Count - 1; i >= 0; i--) {
            if (!wanted.Contains(Rows[i])) {
                Rows.RemoveAt(i);
            }
        }
        for (int i = 0; i < wanted.Count; i++) {
            int at = Rows.IndexOf(wanted[i]);
            if (at < 0) {
                Rows.Insert(i, wanted[i]);
            } else if (at != i) {
                Rows.Move(at, i);
            }
        }

        foreach (var row in wanted) {
            _ = ProbeAsync(row);
        }
    }

    /// <summary>Switches a bookmark's row in the panel on or off; the built-in ones through their settings.</summary>
    public void SetShown(string path, bool shown) {
        if (!Contains(path)) {
            if (_specialByPath.TryGetValue(path, out var special)) {
                special.Switch(shown);
            }

            return;
        }
        if (shown ? !_hidden.Remove(path) : !_hidden.Add(path)) {
            return;
        }

        _log.Info($"Bookmark {(shown ? "shown" : "hidden")}: {path}");
        Changed?.Invoke(this, EventArgs.Empty);
    }


    /// <summary>
    /// Already in the bookmarks? The drop strip asks before offering to
    /// take a folder: dropping one that is in the list does nothing, and a
    /// target that lights up for a no-op is a lie.
    /// </summary>
    public bool Contains(string? path) {
        return path is { Length: > 0 }
            && _favorites.Any(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
    }


    /// <summary>
    /// Adds a bookmark on a folder - one on a phone or a camera too
    /// (decision of 2026-10-19), which then shows only while the device is
    /// plugged in. False when it is no folder.
    /// </summary>
    public bool Add(string? path) {
        if (string.IsNullOrEmpty(path) || !IsFolder(path)) {
            return false;
        }
        // One hidden comes back where it stood: dropping it says "I want this
        // in the panel" as plainly as the tick does.
        if (_hidden.Contains(path)) {
            SetShown(path, true);
            StatusReported?.Invoke(this, string.Format(Strings.StatusBookmarkAdded, NameOf(path)));

            return true;
        }
        if (Contains(path)) {
            StatusReported?.Invoke(this, Strings.StatusAlreadyBookmarked);

            return false;
        }

        _favorites.Add(path);
        // On a device that was just asked about: it is plugged in.
        if (DevicePath.Parse(path) is { } device) {
            _plugged = _plugged.Append(device.Device).ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        _log.Info($"Bookmark added: {path}");
        StatusReported?.Invoke(this, string.Format(Strings.StatusBookmarkAdded, NameOf(path)));
        Changed?.Invoke(this, EventArgs.Empty);

        return true;
    }


    /// <summary>
    /// Drops one user bookmark. Reached from the row menu and from the
    /// "this folder is gone" panel, which knows the path but has no row to
    /// hand over.
    /// </summary>
    public void Remove(string? path) {
        if (string.IsNullOrEmpty(path)) {
            return;
        }

        int idx = IndexOf(path);
        if (idx < 0) {
            // Special folder (Downloads / This PC) — not a user favourite;
            // hiding it goes via Settings.
            return;
        }

        _favorites.RemoveAt(idx);
        _titles.Forget(path);
        _hidden.Remove(path);
        _log.Info($"Bookmark removed: {path}");
        Changed?.Invoke(this, EventArgs.Empty);
    }


    /// <summary>
    /// Moves one bookmark up or down its section of the panel. The two
    /// sections are two lists, so the move can never carry a bookmark across
    /// the divider. The panel's cursor stays on it: the model keeps the
    /// cursor by path. False when nothing moved.
    /// </summary>
    /// <param name="overAbsent">
    /// Every row counts, as on the bookmarks' page, where all of them are in
    /// sight; without it, only the rows the panel draws - a press there
    /// passes the next row on screen.
    /// </param>
    public bool Move(string? path, int delta, bool overAbsent = false) {
        if (string.IsNullOrEmpty(path)) {
            return false;
        }

        bool moved = !Contains(path) && _specialByPath.TryGetValue(path, out var special)
            ? BookmarkOrder.Move(_specialOrder, special.Key, delta, key => overAbsent || _specials.First(s => s.Key == key).IsOn())
            : BookmarkOrder.Move(_favorites, path, delta, other => overAbsent || HasRow(other));
        if (!moved) {
            return false;
        }

        _log.Info($"Bookmark moved: {path} ({delta:+0;-0})");
        Changed?.Invoke(this, EventArgs.Empty);

        return true;
    }


    /// <summary>
    /// Points a bookmark at where its folder went. The path is replaced in
    /// place rather than removed and re-added, so the bookmark keeps its
    /// position in the list. Returns false when nothing changed — the
    /// caller then has nothing to navigate to.
    /// </summary>
    public bool Relocate(string? oldPath, string? newPath) {
        if (string.IsNullOrEmpty(oldPath) || string.IsNullOrEmpty(newPath) || !_fs.DirectoryExists(newPath)) {
            return false;
        }

        int idx = IndexOf(oldPath);
        if (idx < 0) {
            return false;
        }
        if (Contains(newPath)) {
            StatusReported?.Invoke(this, Strings.StatusAlreadyBookmarked);

            return false;
        }

        _favorites[idx] = newPath;
        _titles.Follow(oldPath, newPath);
        if (_hidden.Remove(oldPath)) {
            _hidden.Add(newPath);
        }
        _log.Info($"Bookmark relocated: {oldPath} -> {newPath}");
        Changed?.Invoke(this, EventArgs.Empty);

        return true;
    }


    /// <summary>
    /// A folder Wander moved or renamed: every bookmark on it, or inside
    /// it, points at the new place. Without this the row would go grey and
    /// italic over a folder that is one drag away - moved by the user's own
    /// hand, in this window.
    /// </summary>
    public void Follow(string oldRoot, string newRoot) {
        bool changed = false;
        for (int i = 0; i < _favorites.Count; i++) {
            if (PathRewrite.Under(_favorites[i], oldRoot, newRoot) is not { } moved
                || string.Equals(moved, _favorites[i], StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            _log.Info($"Bookmark followed: {_favorites[i]} -> {moved}");
            if (_hidden.Remove(_favorites[i])) {
                _hidden.Add(moved);
            }
            _favorites[i] = moved;
            changed = true;
        }
        // The names go with their bookmarks.
        changed |= _titles.Follow(oldRoot, newRoot);

        if (changed) {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }


    /// <summary>
    /// Switches a built-in row (Downloads, Documents, the Recycle Bin...) off
    /// in the settings - what Delete on it means: these rows are settings,
    /// not the user's bookmarks, and the folders behind them are not
    /// deleted from this panel. The settings change rebuilds the panel.
    /// False when <paramref name="path"/> is no built-in row.
    /// </summary>
    public bool HideSpecial(string path) {
        if (!_specialByPath.TryGetValue(path, out var special)) {
            return false;
        }

        special.Switch(false);

        return true;
    }


    /// <summary>
    /// The built-in folders that can be resolved, in the user's order,
    /// switched on or not; a folder a machine has no path for has no row
    /// anywhere.
    /// </summary>
    private IEnumerable<(Special Special, string Path)> ResolveSpecials() {
        _specialByPath.Clear();
        foreach (string key in _specialOrder) {
            var special = _specials.First(s => s.Key == key);
            if (special.Resolve() is { Length: > 0 } path) {
                _specialByPath[path] = special;

                yield return (special, path);
            }
        }
    }

    /// <summary>
    /// One special-folder row. A folder that is not on disk (moved to a
    /// share that is offline, a drive no longer there) is greyed as the
    /// user's own bookmarks are - asked of the disk off the UI thread
    /// (PanelRules, ProbeMissing): the check here held the window up on a
    /// sleeping share. The label is a fixed localised name, not the on-disk
    /// folder name, so the user sees a stable caption - unless the user gave
    /// it their own. A shell location (the Recycle Bin) is a leaf: presented
    /// as a list in the right pane, not browsed in the panel.
    /// </summary>
    private PanelRow SpecialRow(Special special, string path) {
        bool isShell = ServiceLocator.TryGet<IShellNamespace>() is { } shell && shell.IsShellPath(path);

        return new PanelRow(path, _titles.Of(path) ?? special.Label(), isShell ? PanelRowKind.Shell : PanelRowKind.Folder) { Role = PanelRowRole.BuiltInBookmark };
    }


    /// <summary>
    /// One user bookmark. A folder that is no longer on disk still gets a
    /// row — dropping it would look like Wander forgot the bookmark, and
    /// the user is the one who decides whether it goes: no chevron, and
    /// clicking it lands on the "this folder is gone" panel in the file
    /// area. Whether it is gone is the window's model's to ask, off the UI
    /// thread (PanelRules, ProbeMissing): built here, the row is an ordinary
    /// one. A bookmark inside an archive is not missing, and not a folder
    /// either - the answer the Recycle Bin gets: a leaf. One on the archive
    /// itself opens like the archive's row under its folder. One on a phone
    /// or a camera is a folder of the device, as under it in the drives
    /// panel (the caller shows it only while the device is plugged in). The
    /// label is the bookmark's own name when it has one.
    /// </summary>
    private PanelRow? BuildFolderRow(string path, bool startsSection) {
        if (string.IsNullOrEmpty(path)) {
            return null;
        }

        bool isShell = ServiceLocator.TryGet<IShellNamespace>() is { } shell && shell.IsShellPath(path);
        bool isArchive = isShell && Archives.Of(path) is { IsRoot: true };
        var kind = DevicePath.Is(path) ? PanelRowKind.DeviceFolder
            : isArchive ? PanelRowKind.Archive
            : isShell ? PanelRowKind.Shell
            : PanelRowKind.Folder;

        return new PanelRow(path, NameOf(path), kind) {
            Role = PanelRowRole.OwnBookmark,
            StartsSection = startsSection,
            // The device's own picture says where the folder is: the camera
            // or the phone, as in the drives panel (2026-10-19).
            IconPath = DevicePath.Parse(path) is { } device ? DevicePath.RootOf(device.Device) : null,
        };
    }


    private int IndexOf(string path) {
        return _favorites.FindIndex(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The panel draws it: not hidden, and not on a device away.</summary>
    private bool HasRow(string path) {
        return !_hidden.Contains(path) && BookmarkTitles.IsShown(path, _plugged);
    }

    private string DefaultNameOf(string path) {
        return _specialByPath.TryGetValue(path, out var special) ? special.Label() : FolderName(path);
    }

    private BookmarkRowViewModel RowFor(string path, bool isBuiltIn, Action<bool> setShown) {
        // The built-in flag too: a user may bookmark Downloads as well.
        return Rows.FirstOrDefault(r => r.IsBuiltIn == isBuiltIn && string.Equals(r.Path, path, StringComparison.OrdinalIgnoreCase))
            ?? new BookmarkRowViewModel(path, isBuiltIn, setShown);
    }

    /// <summary>
    /// A row's folder gone or back - a drive plugged out, a folder moved by
    /// another program. A place that is no folder on disk is not asked: a
    /// device is told about (<see cref="SetPlugged"/>), the bin is always there.
    /// </summary>
    private async Task ProbeAsync(BookmarkRowViewModel row) {
        if (row.IsProbing || ServiceLocator.TryGet<IShellNamespace>()?.IsShellPath(row.Path) == true) {
            return;
        }

        row.IsProbing = true;
        try {
            row.IsGone = !await Task.Run(() => _fs.DirectoryExists(row.Path));
        } finally {
            row.IsProbing = false;
        }
    }

    /// <summary>The path as a row shows it: a device's without its prefix, nothing for the bin.</summary>
    private static string PlaceText(string path) {
        return DevicePath.Is(path) ? path[DevicePath.Prefix.Length..]
            : path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase) ? ""
            : path;
    }


    private static string LeafName(string path) {
        return Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
    }

    /// <summary>The folder's own name: a phone by its name, a drive root by its whole text.</summary>
    private static string FolderName(string path) {
        if (DevicePath.Parse(path) is { IsRoot: true } device) {
            return device.Device;
        }

        string name = LeafName(path);

        return string.IsNullOrEmpty(name) ? path : name;
    }

    /// <summary>
    /// A folder on disk, or one on a device - the device is asked, from what
    /// its listing already read.
    /// </summary>
    private bool IsFolder(string path) {
        return DevicePath.Is(path)
            ? ServiceLocator.TryGet<IDeviceContent>()?.GetEntry(path) is { Kind: EntryKind.Directory }
            : _fs.DirectoryExists(path);
    }


    /// <summary>
    /// One known folder, or null where the platform layer is absent (tests,
    /// and any future non-Windows host). <c>SHGetKnownFolderPath</c> is the
    /// only correct answer for these: "%USERPROFILE%\Музыка" is wrong on an
    /// English install and wrong again once the folder has been moved.
    /// </summary>
    private static string? ResolveKnown(Func<IKnownFolders, string?> pick) {
        return ServiceLocator.TryGet<IKnownFolders>() is { } known ? pick(known) : null;
    }


    /// <summary>A built-in bookmark: what it is called, where it stands, the settings switch behind it.</summary>
    private sealed record Special(string Key, Func<string> Label, Func<string?> Resolve, Func<bool> IsOn, Action<bool> Switch);
}
