using System.Collections.ObjectModel;
using System.IO;
using Wander.App.Resources;
using Wander.App.ViewModels;
using Wander.Core;
using Wander.Core.FileSystem;
using Wander.Core.Logging;
using Wander.Core.Navigation;
using Wander.Core.Shell;

namespace Wander.App.Controllers;

/// <summary>
/// The left panel's bookmarks: the special folders the settings enable, and
/// the user's own list under them.
///
/// <para>
/// Owns the whole of that state — the ordered list of favourite paths and
/// the nodes built from it — and nothing else. What it cannot own it is
/// handed: wiring a fresh node into the window's tree bookkeeping is the
/// trees' business, so it arrives as one delegate rather than as a
/// reference back to the view model.
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
    private readonly Action<TreeNodeViewModel> _wire;
    private readonly List<string> _favorites = new();
    // The built-in rows of the current build and the settings switch behind
    // each of them: Delete on such a row turns the switch off - see HideSpecial.
    private readonly Dictionary<TreeNodeViewModel, Action> _specialSwitches = new();


    public BookmarksController(
        IFileSystem fs, SettingsViewModel settings, ILogger log, Action<TreeNodeViewModel> wire) {
        _fs = fs;
        _settings = settings;
        _log = log;
        _wire = wire;
    }


    /// <summary>Something to tell the user — already localised.</summary>
    public event EventHandler<string>? StatusReported;

    /// <summary>The list changed and is worth persisting.</summary>
    public event EventHandler? Changed;


    /// <summary>The rows, in panel order. Bound by the left panel.</summary>
    public ObservableCollection<TreeNodeViewModel> Items { get; } = new();


    /// <summary>
    /// True while <see cref="Build"/> is replacing the rows. Saving session
    /// state during a rebuild would record a half-built panel.
    /// </summary>
    public bool IsBuilding { get; private set; }


    /// <summary>The user's own bookmarks, in order — what gets persisted.</summary>
    public IReadOnlyList<string> Paths => _favorites;


    /// <summary>Replaces the saved list. Does not rebuild — the caller does.</summary>
    public void Load(IEnumerable<string> paths) {
        _favorites.Clear();
        _favorites.AddRange(paths);
    }


    /// <summary>
    /// Rebuilds the panel from the current settings and the saved list.
    /// Idempotent; every row is a fresh instance, so callers should be ready
    /// for a binding refresh.
    /// </summary>
    /// <param name="persistedStops">
    /// Expanded branches loaded at startup. Merged with what is expanded on
    /// screen right now, because a rebuild would otherwise close everything
    /// the user had opened this session.
    /// </param>
    public void Build(IReadOnlyList<NavigationStop> persistedStops) {
        var live = new List<NavigationStop>();
        foreach (var b in Items) {
            b.CollectExpanded(live, NavigationSource.Bookmark);
        }
        var stops = new HashSet<NavigationStop>(live);
        foreach (var stop in persistedStops) {
            // Drives-side entries describe the lower tree and don't belong here.
            if (stop.Source == NavigationSource.Bookmark) {
                stops.Add(stop);
            }
        }

        IsBuilding = true;
        try {
            Items.Clear();
            _specialSwitches.Clear();

            if (_settings.ShowBookmarkDownloads) {
                AddSpecialFolder(Strings.SpecialFolderDownloads, ResolveKnown(f => f.GetDownloads()), () => _settings.ShowBookmarkDownloads = false);
            }
            if (_settings.ShowBookmarkDocuments) {
                AddSpecialFolder(Strings.SpecialFolderDocuments, ResolveKnown(f => f.GetDocuments()), () => _settings.ShowBookmarkDocuments = false);
            }
            if (_settings.ShowBookmarkPictures) {
                AddSpecialFolder(Strings.SpecialFolderPictures, ResolveKnown(f => f.GetPictures()), () => _settings.ShowBookmarkPictures = false);
            }
            if (_settings.ShowBookmarkDesktop) {
                AddSpecialFolder(Strings.SpecialFolderDesktop, ResolveKnown(f => f.GetDesktop()), () => _settings.ShowBookmarkDesktop = false);
            }
            if (_settings.ShowBookmarkMusic) {
                AddSpecialFolder(Strings.SpecialFolderMusic, ResolveKnown(f => f.GetMusic()), () => _settings.ShowBookmarkMusic = false);
            }
            if (_settings.ShowBookmarkVideos) {
                AddSpecialFolder(Strings.SpecialFolderVideos, ResolveKnown(f => f.GetVideos()), () => _settings.ShowBookmarkVideos = false);
            }
            if (_settings.ShowBookmarkRecycleBin && ServiceLocator.TryGet<IShellNamespace>() is not null) {
                AddSpecialFolder(Strings.SpecialFolderRecycleBin, ShellPaths.RecycleBin, () => _settings.ShowBookmarkRecycleBin = false);
            }

            // The divider goes on the first user bookmark, and only when
            // there is a special folder above it to be divided from.
            bool startsSection = Items.Count > 0;
            foreach (string path in _favorites) {
                var node = BuildFolderNode(path, startsSection);
                if (node is null) {
                    continue;
                }
                Items.Add(node);
                startsSection = false;
            }

            foreach (var stop in stops) {
                foreach (var b in Items) {
                    if (b.TryExpandToPath(stop.Path, select: false, expandTarget: true)) {
                        break;
                    }
                }
            }

            // One pass for the whole panel: every row above was drawn with
            // a chevron on faith, and the disk answers off the UI thread.
            // Shell namespaces and missing bookmarks are built without a
            // filesystem and there is nothing to ask about them.
            TreeNodeViewModel.ProbeForChevrons(
                _fs, Items.Where(b => b.HasFileSystem).ToList());
        } finally {
            IsBuilding = false;
        }
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
    /// "this folder is gone" panel, which knows the path but has no tree
    /// node to hand over.
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
    /// bookmark across the divider.
    ///
    /// <para>
    /// Returns the rebuilt row — <see cref="Build"/> creates fresh
    /// instances, and the caller needs the new one to put the keyboard back
    /// on the row so a second <c>Ctrl+Up</c> keeps working.
    /// </para>
    /// </summary>
    public TreeNodeViewModel? Move(TreeNodeViewModel? node, int delta) {
        if (node is null || string.IsNullOrEmpty(node.FullPath)) {
            return null;
        }

        string path = node.FullPath;
        int from = IndexOf(path);
        if (from < 0) {
            return null;
        }

        int to = from + delta;
        if (to < 0 || to >= _favorites.Count) {
            return null;
        }

        _favorites.RemoveAt(from);
        _favorites.Insert(to, path);
        _log.Info($"Bookmark moved: {path} ({from} -> {to})");
        Changed?.Invoke(this, EventArgs.Empty);

        return Items.FirstOrDefault(b => string.Equals(b.FullPath, path, StringComparison.OrdinalIgnoreCase));
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
    /// False for any other node, a folder under a built-in row included.
    /// </summary>
    public bool HideSpecial(TreeNodeViewModel node) {
        if (!_specialSwitches.TryGetValue(node, out var switchOff)) {
            return false;
        }

        switchOff();

        return true;
    }


    /// <summary>
    /// Adds one special-folder node. No-op when the path can't be resolved
    /// or doesn't exist on disk (e.g. the user moved the folder to a drive
    /// that is no longer there). The label is a fixed localised name, not
    /// the on-disk folder name, so the user sees a stable caption.
    ///
    /// <para>
    /// Shell-namespace paths (Recycle Bin) take a different route: no
    /// <see cref="IFileSystem"/> probe and no lazy children — the node is a
    /// clickable leaf, navigated through <see cref="IShellNamespace"/>.
    /// </para>
    /// </summary>
    private void AddSpecialFolder(string label, string? path, Action switchOff) {
        if (string.IsNullOrEmpty(path)) {
            return;
        }

        if (ServiceLocator.TryGet<IShellNamespace>() is { } shell && shell.IsShellPath(path)) {
            // No tree children for shell namespaces in this iteration —
            // Recycle Bin is presented as a flat list in the right pane,
            // not browseable from the bookmarks tree.
            var shellNode = new TreeNodeViewModel(label, path, EntryKind.Directory, fs: null, hasChildren: false);
            _wire(shellNode);
            Items.Add(shellNode);
            _specialSwitches[shellNode] = switchOff;

            return;
        }

        if (!_fs.DirectoryExists(path)) {
            return;
        }

        // Optimistic chevron; Build's ProbeForChevrons pass corrects the
        // leaves off the UI thread rather than asking the disk here.
        var node = new TreeNodeViewModel(
            label, path, EntryKind.Directory, _fs, hasChildren: true, _settings);
        _wire(node);
        Items.Add(node);
        _specialSwitches[node] = switchOff;
    }


    /// <summary>
    /// One user bookmark. A folder that is no longer on disk still gets a
    /// row — dropping it would look like Wander forgot the bookmark, and
    /// the user is the one who decides whether it goes. The row is built
    /// without an <see cref="IFileSystem"/> so it has no chevron and no
    /// children to enumerate; clicking it lands on the "this folder is
    /// gone" panel in the file area.
    /// </summary>
    private TreeNodeViewModel? BuildFolderNode(string path, bool startsSection) {
        if (string.IsNullOrEmpty(path)) {
            return null;
        }

        string name = LeafName(path);
        if (string.IsNullOrEmpty(name)) {
            // e.g. a drive root — fall back to the trimmed path itself.
            name = path;
        }

        // A bookmark inside an archive is not a directory and never will
        // be, but it is not missing either — the same answer the Recycle
        // Bin gets. It has no children to enumerate, so it is a leaf. A
        // bookmark on the archive itself is the tree row it would be under
        // its folder, and expands the same way.
        bool isShell = ServiceLocator.TryGet<IShellNamespace>() is { } shell && shell.IsShellPath(path);
        bool isArchive = isShell && Archives.Of(path) is { IsRoot: true };
        bool exists = isArchive || (!isShell && _fs.DirectoryExists(path));
        var node = new TreeNodeViewModel(
            name, path, EntryKind.Directory,
            exists ? _fs : null,
            hasChildren: exists,
            _settings) {
            IsRemovableBookmark = true,
            StartsUserSection = startsSection,
            IsMissing = !exists && !isShell,
        };
        _wire(node);

        return node;
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
