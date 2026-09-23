using System.IO;
using Wander.App.Resources;
using Wander.App.ViewModels;
using Wander.Core;
using Wander.Core.FileSystem;
using Wander.Core.Logging;
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
    // The built-in rows of the last build and the settings switch behind
    // each of them: Delete on such a row turns the switch off - see HideSpecial.
    private readonly Dictionary<string, Action> _specialSwitches = new(StringComparer.OrdinalIgnoreCase);


    public BookmarksController(IFileSystem fs, SettingsViewModel settings, ILogger log) {
        _fs = fs;
        _settings = settings;
        _log = log;
    }


    /// <summary>Something to tell the user — already localised.</summary>
    public event EventHandler<string>? StatusReported;

    /// <summary>The list changed and is worth persisting - and the panel's rows are to be built again.</summary>
    public event EventHandler? Changed;


    /// <summary>The user's own bookmarks, in order — what gets persisted.</summary>
    public IReadOnlyList<string> Paths => _favorites;


    /// <summary>Replaces the saved list. Does not rebuild — the caller does.</summary>
    public void Load(IEnumerable<string> paths) {
        _favorites.Clear();
        _favorites.AddRange(paths);
    }


    /// <summary>
    /// The panel's top rows as the settings and the list say now: the
    /// special folders switched on, then the user's own, a rule above the
    /// first of them when there is a special folder to divide it from.
    /// </summary>
    public IReadOnlyList<PanelRow> BuildRows() {
        var rows = new List<PanelRow>();
        _specialSwitches.Clear();

        if (_settings.ShowBookmarkDownloads) {
            AddSpecialFolder(rows, Strings.SpecialFolderDownloads, ResolveKnown(f => f.GetDownloads()), () => _settings.ShowBookmarkDownloads = false);
        }
        if (_settings.ShowBookmarkDocuments) {
            AddSpecialFolder(rows, Strings.SpecialFolderDocuments, ResolveKnown(f => f.GetDocuments()), () => _settings.ShowBookmarkDocuments = false);
        }
        if (_settings.ShowBookmarkPictures) {
            AddSpecialFolder(rows, Strings.SpecialFolderPictures, ResolveKnown(f => f.GetPictures()), () => _settings.ShowBookmarkPictures = false);
        }
        if (_settings.ShowBookmarkDesktop) {
            AddSpecialFolder(rows, Strings.SpecialFolderDesktop, ResolveKnown(f => f.GetDesktop()), () => _settings.ShowBookmarkDesktop = false);
        }
        if (_settings.ShowBookmarkMusic) {
            AddSpecialFolder(rows, Strings.SpecialFolderMusic, ResolveKnown(f => f.GetMusic()), () => _settings.ShowBookmarkMusic = false);
        }
        if (_settings.ShowBookmarkVideos) {
            AddSpecialFolder(rows, Strings.SpecialFolderVideos, ResolveKnown(f => f.GetVideos()), () => _settings.ShowBookmarkVideos = false);
        }
        if (_settings.ShowBookmarkRecycleBin && ServiceLocator.TryGet<IShellNamespace>() is not null) {
            AddSpecialFolder(rows, Strings.SpecialFolderRecycleBin, ShellPaths.RecycleBin, () => _settings.ShowBookmarkRecycleBin = false);
        }

        // The divider goes on the first user bookmark, and only when there
        // is a special folder above it to be divided from.
        bool startsSection = rows.Count > 0;
        foreach (string path in _favorites) {
            if (BuildFolderRow(path, startsSection) is { } row) {
                rows.Add(row);
                startsSection = false;
            }
        }

        return rows;
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


    public void Add(string? path) {
        if (string.IsNullOrEmpty(path) || !_fs.DirectoryExists(path)) {
            return;
        }
        if (Contains(path)) {
            StatusReported?.Invoke(this, Strings.StatusAlreadyBookmarked);

            return;
        }

        _favorites.Add(path);
        _log.Info($"Bookmark added: {path}");
        StatusReported?.Invoke(this, string.Format(Strings.StatusBookmarkAdded, LeafName(path)));
        Changed?.Invoke(this, EventArgs.Empty);
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
        _log.Info($"Bookmark removed: {path}");
        Changed?.Invoke(this, EventArgs.Empty);
    }


    /// <summary>
    /// Moves one user bookmark up or down its section of the panel.
    /// Special folders are not in the list, so the move can never carry a
    /// bookmark across the divider. The panel's cursor stays on it: the
    /// model keeps the cursor by path. False when nothing moved.
    /// </summary>
    public bool Move(string? path, int delta) {
        if (string.IsNullOrEmpty(path)) {
            return false;
        }

        int from = IndexOf(path);
        int to = from + delta;
        if (from < 0 || to < 0 || to >= _favorites.Count) {
            return false;
        }

        string moved = _favorites[from];
        _favorites.RemoveAt(from);
        _favorites.Insert(to, moved);
        _log.Info($"Bookmark moved: {moved} ({from} -> {to})");
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
            _favorites[i] = moved;
            changed = true;
        }

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
        if (!_specialSwitches.TryGetValue(path, out var switchOff)) {
            return false;
        }

        switchOff();

        return true;
    }


    /// <summary>
    /// Adds one special-folder row. None when the path can't be resolved
    /// or doesn't exist on disk (e.g. the user moved the folder to a drive
    /// that is no longer there). The label is a fixed localised name, not
    /// the on-disk folder name, so the user sees a stable caption. A shell
    /// location (the Recycle Bin) is a leaf: presented as a list in the
    /// right pane, not browsed in the panel.
    /// </summary>
    private void AddSpecialFolder(List<PanelRow> rows, string label, string? path, Action switchOff) {
        if (string.IsNullOrEmpty(path)) {
            return;
        }

        bool isShell = ServiceLocator.TryGet<IShellNamespace>() is { } shell && shell.IsShellPath(path);
        if (!isShell && !_fs.DirectoryExists(path)) {
            return;
        }

        rows.Add(new PanelRow(path, label, isShell ? PanelRowKind.Shell : PanelRowKind.Folder) { Role = PanelRowRole.BuiltInBookmark });
        _specialSwitches[path] = switchOff;
    }


    /// <summary>
    /// One user bookmark. A folder that is no longer on disk still gets a
    /// row — dropping it would look like Wander forgot the bookmark, and
    /// the user is the one who decides whether it goes: no chevron, and
    /// clicking it lands on the "this folder is gone" panel in the file
    /// area. A bookmark inside an archive is not missing, and not a folder
    /// either - the answer the Recycle Bin gets: a leaf. One on the archive
    /// itself opens like the archive's row under its folder.
    /// </summary>
    private PanelRow? BuildFolderRow(string path, bool startsSection) {
        if (string.IsNullOrEmpty(path)) {
            return null;
        }

        string name = LeafName(path);
        if (string.IsNullOrEmpty(name)) {
            // e.g. a drive root — fall back to the trimmed path itself.
            name = path;
        }

        bool isShell = ServiceLocator.TryGet<IShellNamespace>() is { } shell && shell.IsShellPath(path);
        bool isArchive = isShell && Archives.Of(path) is { IsRoot: true };
        bool exists = isArchive || (!isShell && _fs.DirectoryExists(path));
        var kind = isArchive ? PanelRowKind.Archive : isShell ? PanelRowKind.Shell : PanelRowKind.Folder;

        return new PanelRow(path, name, kind) {
            Role = PanelRowRole.OwnBookmark,
            StartsSection = startsSection,
            IsMissing = !exists && !isShell,
        };
    }


    private int IndexOf(string path) {
        return _favorites.FindIndex(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
    }


    private static string LeafName(string path) {
        return Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
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
}
