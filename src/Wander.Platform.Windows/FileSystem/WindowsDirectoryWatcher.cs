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


    public event EventHandler? Changed;


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


    private void OnChanged(object sender, FileSystemEventArgs e) {
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// The watcher lost track — normally its buffer overflowing under a
    /// burst of changes. The folder is now in an unknown state, which is
    /// precisely when a re-list is most needed, so this reports a change
    /// like any other and then puts the watcher back on its feet.
    /// </summary>
    private void OnError(object sender, ErrorEventArgs e) {
        _log.Warn($"[watch] watcher error: {e.GetException().Message}");
        Changed?.Invoke(this, EventArgs.Empty);

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

            watcher.Created += OnChanged;
            watcher.Deleted += OnChanged;
            watcher.Renamed += OnChanged;
            watcher.Changed += OnChanged;
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
            _watcher.Created -= OnChanged;
            _watcher.Deleted -= OnChanged;
            _watcher.Renamed -= OnChanged;
            _watcher.Changed -= OnChanged;
            _watcher.Error -= OnError;
            _watcher.Dispose();
        } catch (Exception ex) {
            _log.Warn($"[watch] stop failed: {ex.Message}");
        } finally {
            _watcher = null;
        }
    }
}
