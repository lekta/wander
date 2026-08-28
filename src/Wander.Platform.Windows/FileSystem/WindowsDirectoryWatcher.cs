using System.IO;
using Wander.Core.FileSystem;
using Wander.Core.Logging;

namespace Wander.Platform.Windows.FileSystem;

/// <summary>
/// <see cref="IDirectoryWatcher"/> over <see cref="FileSystemWatcher"/>.
///
/// <para>
/// Everything here is about the ways this API fails rather than the way it
/// works. Constructing a watcher throws on a path that has gone away, on a
/// disconnected share and on anything that is not a real directory; its
/// internal buffer overflows when changes arrive faster than they are read,
/// and it reports that as an error rather than as changes. All three are
/// treated the same way: log it, and let the folder be un-watched or
/// re-listed. A watcher that cannot watch costs the user an F5 — it must
/// never cost them a crash.
/// </para>
///
/// <para>
/// Subfolders are not watched. The listing only ever shows one level, and
/// recursive watching on a large tree is exactly the setup that overflows
/// the buffer.
/// </para>
/// </summary>
public sealed class WindowsDirectoryWatcher : IDirectoryWatcher {
    private readonly ILogger _log;
    private readonly object _lock = new();

    private FileSystemWatcher? _watcher;


    public WindowsDirectoryWatcher(ILogger log) {
        _log = log;
    }


    public event EventHandler<DirectoryChange>? Changed;


    public void Watch(string? path) {
        lock (_lock) {
            Stop();

            if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) {
                return;
            }

            _watcher = Create(path);
        }
    }


    public void Dispose() {
        lock (_lock) {
            Stop();
        }
    }


    /// <summary>
    /// A file appeared, vanished or was renamed — the folder now holds a
    /// different set of files than it did.
    /// </summary>
    private void OnStructural(object sender, FileSystemEventArgs e) {
        Report(e.FullPath, structural: true);
    }

    /// <summary>
    /// A file that was there before and still is has been written to. The
    /// listing has the same rows; one of them says something different.
    /// </summary>
    private void OnContent(object sender, FileSystemEventArgs e) {
        Report(e.FullPath, structural: false);
    }

    /// <summary>
    /// A rename. Usually that means the folder now holds a different set of
    /// names — but not always, and the exception is the common case here:
    /// <see cref="IFileSystem.ReplaceAtomic"/> writes a scratch file and
    /// renames it onto its target, so every rating written into a sidecar
    /// arrives as "renamed to a file that was already there". Reporting that
    /// as structural is what made a single starred photograph re-list the
    /// whole folder.
    ///
    /// <para>
    /// A rename <em>out of</em> our own scratch file is therefore a content
    /// change: the name that vanished was never in the listing, and the name
    /// that appeared was already in it. Every other rename is structural.
    /// </para>
    /// </summary>
    private void OnRenamed(object sender, RenamedEventArgs e) {
        Report(e.FullPath, structural: !TransientFiles.IsTransient(e.OldFullPath));
    }

    private void Report(string path, bool structural) {
        // Our own scratch file, which exists for a few milliseconds in the
        // middle of an atomic replace. Reporting it would make every write
        // to a sidecar look like a file appearing and disappearing in the
        // folder — which is the one thing that forces a full re-listing.
        if (TransientFiles.IsTransient(path)) {
            return;
        }

        Changed?.Invoke(this, new DirectoryChange(path, structural));
    }

    /// <summary>
    /// The watcher lost track — normally its buffer overflowing under a
    /// burst of changes. The folder is now in an unknown state, which is
    /// precisely when a re-list is most needed, so this reports a change
    /// like any other and then puts the watcher back on its feet.
    /// </summary>
    private void OnError(object sender, ErrorEventArgs e) {
        _log.Warn($"[watch] watcher error: {e.GetException().Message}");
        Changed?.Invoke(this, DirectoryChange.Unknown);

        lock (_lock) {
            if (!ReferenceEquals(sender, _watcher)) {
                return;
            }

            // Re-arm on the same folder. Watch() takes the same lock, so the
            // restart is done inline rather than by calling back into it.
            string path = _watcher.Path;
            Stop();
            _watcher = Directory.Exists(path) ? Create(path) : null;
        }
    }


    /// <summary>
    /// Caller holds the lock. Null when the folder cannot be watched — a
    /// share that dropped, a drive ejected between the check and the
    /// constructor, a path we are not allowed to look at.
    /// </summary>
    private FileSystemWatcher? Create(string path) {
        try {
            var watcher = new FileSystemWatcher(path) {
                // What can change a row in the listing: a file appearing,
                // disappearing or being renamed, and the size / date columns
                // of one that is being written.
                NotifyFilter = NotifyFilters.FileName
                    | NotifyFilters.DirectoryName
                    | NotifyFilters.Size
                    | NotifyFilters.LastWrite
                    | NotifyFilters.Attributes,
                IncludeSubdirectories = false,
            };

            watcher.Created += OnStructural;
            watcher.Deleted += OnStructural;
            watcher.Renamed += OnRenamed;
            watcher.Changed += OnContent;
            watcher.Error += OnError;
            watcher.EnableRaisingEvents = true;

            return watcher;
        } catch (Exception ex) {
            _log.Warn($"[watch] cannot watch {path}: {ex.Message}");

            return null;
        }
    }

    /// <summary>Caller holds the lock.</summary>
    private void Stop() {
        if (_watcher is null) {
            return;
        }

        try {
            _watcher.EnableRaisingEvents = false;
            _watcher.Created -= OnStructural;
            _watcher.Deleted -= OnStructural;
            _watcher.Renamed -= OnRenamed;
            _watcher.Changed -= OnContent;
            _watcher.Error -= OnError;
            _watcher.Dispose();
        } catch (Exception ex) {
            _log.Warn($"[watch] stop failed: {ex.Message}");
        } finally {
            _watcher = null;
        }
    }
}
