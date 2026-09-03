using System.Security.Cryptography;
using System.Text;
using Wander.Core.Logging;

namespace Wander.Platform.Windows.Icons;

/// <summary>
/// Keeps generated thumbnails on disk so a folder of RAW photos is slow to
/// browse once rather than on every launch. Lives in
/// <c>%LocalAppData%\Wander\thumbs</c> — same place as the logs and
/// <c>state.json</c>, and safe to delete by hand at any time.
///
/// <para>
/// One PNG per cached thumbnail, named after a hash of
/// <c>generation | path | last-write-time | size</c>. The two file stamps
/// are in the key on purpose: an edited file produces a different name, so
/// a stale thumbnail is never served — it is simply orphaned and eventually
/// trimmed. That is cheaper and far more robust than trying to invalidate
/// entries. <see cref="Generation"/> is the same trick for a change on our
/// side rather than the file's.
/// </para>
///
/// <para>
/// Every disk error is swallowed. A cache that cannot be read or written is
/// a performance loss, never a reason to fail showing an icon.
/// </para>
/// </summary>
public sealed class ThumbnailDiskCache {
    /// <summary>Writes between size checks. Statting the folder on every write would defeat the point.</summary>
    private const int WritesBetweenTrims = 64;

    /// <summary>How far below the budget a trim goes, so trims stay rare.</summary>
    private const double TrimTargetFraction = 0.8;

    /// <summary>
    /// Bumped when Wander starts drawing a thumbnail differently for the
    /// same bytes on disk. The key already covers "the file changed"; this
    /// covers "we changed", which nothing else could express — a cache
    /// keyed only on the source would go on serving the old picture for
    /// files that never moved. Old entries are not deleted, they simply
    /// stop being found and the budget trims them.
    ///
    /// <para>
    /// v2 (2026-09-02): the jumbo slot is trimmed to the icon in it, so
    /// applications without a 256-px icon resource no longer come back as a
    /// small picture in the corner of an empty square.
    /// </para>
    /// </summary>
    private const int Generation = 2;

    private readonly string _directory;
    private readonly ILogger _log;
    private readonly object _lock = new();

    private long _budgetBytes;
    private bool _enabled;
    private int _writesSinceTrim;


    public ThumbnailDiskCache(string directory, ILogger log) {
        _directory = directory;
        _log = log;
        _budgetBytes = 0;
        _enabled = false;
    }


    /// <summary>Where the cache lives, for showing in the settings dialog.</summary>
    public string Directory => _directory;


    public void Configure(bool enabled, long budgetBytes) {
        lock (_lock) {
            _enabled = enabled;
            _budgetBytes = Math.Max(0, budgetBytes);
        }

        if (!enabled) {
            return;
        }

        // A budget lowered in the settings dialog has to bite now, not after
        // another 64 thumbnails — but off the caller's thread. Both callers
        // (startup and the settings dialog) are on the UI thread, and a trim
        // stats and deletes thousands of files.
        _ = Task.Run(Trim);
    }


    /// <summary>Total bytes currently on disk, or 0 if the folder is unreadable.</summary>
    public long CurrentSizeBytes() {
        try {
            var dir = new DirectoryInfo(_directory);
            if (!dir.Exists) {
                return 0;
            }

            return dir.EnumerateFiles("*.png").Sum(f => f.Length);
        } catch {
            return 0;
        }
    }


    public byte[]? TryRead(string sourcePath) {
        if (!IsEnabled()) {
            return null;
        }

        string? file = TryBuildFileName(sourcePath);
        if (file is null) {
            return null;
        }

        try {
            if (!File.Exists(file)) {
                return null;
            }

            byte[] bytes = File.ReadAllBytes(file);
            // Touch it so trimming evicts what nobody looks at rather than
            // what happens to be oldest on disk.
            File.SetLastWriteTimeUtc(file, DateTime.UtcNow);

            return bytes.Length > 0 ? bytes : null;
        } catch {
            return null;
        }
    }


    public void Write(string sourcePath, byte[] png) {
        if (!IsEnabled() || png.Length == 0) {
            return;
        }

        string? file = TryBuildFileName(sourcePath);
        if (file is null) {
            return;
        }

        // Write beside and move into place: two Wander windows browsing the
        // same folder must not leave a half-written PNG behind.
        string temp = file + "." + Environment.ProcessId + ".tmp";
        try {
            System.IO.Directory.CreateDirectory(_directory);
            File.WriteAllBytes(temp, png);
            File.Move(temp, file, overwrite: true);
        } catch {
            // Clean up after ourselves — a leftover temp file counts against
            // no budget and would never be trimmed.
            try {
                File.Delete(temp);
            } catch {
                // Nothing more to try; Clear() sweeps these up too.
            }
            return;
        }

        bool trim;
        lock (_lock) {
            trim = ++_writesSinceTrim >= WritesBetweenTrims;
            if (trim) {
                _writesSinceTrim = 0;
            }
        }

        if (trim) {
            Trim();
        }
    }


    /// <summary>
    /// Drops the least recently used files until the folder is comfortably
    /// under budget. Cheap enough to run inline on the caller's background
    /// thread; it only ever stats and deletes.
    /// </summary>
    public void Trim() {
        long budget;
        lock (_lock) {
            budget = _budgetBytes;
        }

        try {
            var dir = new DirectoryInfo(_directory);
            if (!dir.Exists) {
                return;
            }

            var files = dir.GetFiles("*.png");
            long total = files.Sum(f => f.Length);
            if (total <= budget) {
                return;
            }

            long target = (long)(budget * TrimTargetFraction);
            foreach (var file in files.OrderBy(f => f.LastWriteTimeUtc)) {
                if (total <= target) {
                    break;
                }
                long size = file.Length;
                try {
                    file.Delete();
                    total -= size;
                } catch {
                    // Locked by another reader — try the next one.
                }
            }

            _log.Info($"Thumbnail cache trimmed to {total / (1024 * 1024)} MB");
        } catch (Exception ex) {
            _log.Warn($"Thumbnail cache trim failed: {ex.Message}");
        }
    }


    /// <summary>
    /// Drops the entry for one file, if the key it would be under right now
    /// exists.
    ///
    /// <para>
    /// Normally there is nothing to do: the key carries the file's stamp,
    /// so an edited file asks under a new name and the old entry is already
    /// unreachable. This covers the one case the stamp cannot - a file
    /// rewritten with its size and its last-write time unchanged, which is
    /// what a tool that preserves timestamps does. Then the key is the same
    /// key, and the picture behind it is the wrong one.
    /// </para>
    /// </summary>
    public void Forget(string sourcePath) {
        if (TryBuildFileName(sourcePath) is not { } file) {
            return;
        }

        try {
            File.Delete(file);
        } catch {
            // Being read right now, or already gone. Either way there is
            // nothing to do about it: the memory tier has been dropped, so
            // the next load re-reads the file itself.
        }
    }


    /// <summary>Removes every cached thumbnail. Wired to the settings dialog's button.</summary>
    public void Clear() {
        try {
            var dir = new DirectoryInfo(_directory);
            if (!dir.Exists) {
                return;
            }

            // Includes any *.tmp left by a write that died mid-flight.
            foreach (var file in dir.GetFiles()) {
                try {
                    file.Delete();
                } catch {
                    // In use right now — it will be trimmed later.
                }
            }
            _log.Info("Thumbnail cache cleared");
        } catch (Exception ex) {
            _log.Warn($"Thumbnail cache clear failed: {ex.Message}");
        }
    }


    private bool IsEnabled() {
        lock (_lock) {
            return _enabled && _budgetBytes > 0;
        }
    }


    /// <summary>
    /// Cache file for a source path, or null when the file cannot be
    /// stamped (gone, or a shell-namespace pseudo-path that has no
    /// on-disk identity to key on).
    /// </summary>
    private string? TryBuildFileName(string sourcePath) {
        try {
            var info = new FileInfo(sourcePath);
            if (!info.Exists) {
                return null;
            }

            string key = $"v{Generation}|{sourcePath.ToLowerInvariant()}|{info.LastWriteTimeUtc.Ticks}|{info.Length}";
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));

            return Path.Combine(_directory, Convert.ToHexString(hash, 0, 16) + ".png");
        } catch {
            return null;
        }
    }
}
