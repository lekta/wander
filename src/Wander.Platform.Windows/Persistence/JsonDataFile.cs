using System.Text;
using System.Text.Json;
using Wander.Core.Logging;
using Wander.Core.Persistence;

namespace Wander.Platform.Windows.Persistence;

/// <summary>
/// One data file of the stores here, read and written by the rule of
/// <see cref="StoreRead"/> (audit 2026-11-18, P6, V14, L8). A read tells
/// apart no file, read, broken, could not be opened and of a newer shape; a
/// file that did not read is not written over as it is - one that could not
/// be opened is left alone until a later read gets through, a broken one
/// goes to <c>&lt;name&gt;.bad</c> before the first write; a write is
/// flushed to the disk before it takes the file's name, so a power cut
/// right after leaves the old file or the new one, never a file of zeros.
/// A failed read or write is a WARN, once while the outcome stays the same.
/// </summary>
internal sealed class JsonDataFile {
    private static readonly UTF8Encoding _utf8 = new(encoderShouldEmitUTF8Identifier: false);

    private readonly string _path;
    private readonly string _name;
    private readonly ILogger _log;
    private readonly int? _currentVersion;
    private readonly Lock _gate = new();

    // What the last read decided (StoreRead.Plan); a store that has not
    // read yet writes, as before.
    private bool _mayWrite = true;
    private bool _moveAside;

    // The first read of the session could not open the file: what is in
    // memory is the defaults, and nothing is written until the session ends.
    private bool _closedForSession;
    private bool _readBefore;

    // The outcome last warned about: the same one again says nothing.
    private StoreReadOutcome? _warned;


    /// <param name="currentVersion">The shape this build writes, for a format with a <c>Version</c>; null for one without.</param>
    public JsonDataFile(string path, ILogger log, int? currentVersion) {
        _path = path;
        _name = Path.GetFileName(path);
        _log = log;
        _currentVersion = currentVersion;
    }


    /// <summary>How the last read went.</summary>
    public StoreReadOutcome LastOutcome { get; private set; } = StoreReadOutcome.Missing;


    /// <summary>
    /// The file's content, or null when there is none to use -
    /// <see cref="LastOutcome"/> says why. A file of a newer shape is
    /// returned as far as it reads, and is not written.
    /// </summary>
    public T? Read<T>(JsonSerializerOptions options) where T : class {
        T? value = null;
        StoreReadOutcome outcome;
        string? why = null;
        string? json = null;
        if (!File.Exists(_path)) {
            outcome = StoreReadOutcome.Missing;
        } else {
            try {
                json = File.ReadAllText(_path);
            } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
                why = ex.Message;
            }

            if (json is null) {
                outcome = StoreReadOutcome.Unavailable;
            } else if (_currentVersion is { } current && VersionOf(json) > current) {
                outcome = StoreReadOutcome.Newer;
                value = TryParse<T>(json, options, out _);
            } else {
                value = TryParse<T>(json, options, out why);
                outcome = value is null ? StoreReadOutcome.Broken : StoreReadOutcome.Read;
            }
        }

        Settle(outcome, why);

        return value;
    }

    /// <summary>
    /// Writes <paramref name="json"/>, unless the last read said not to:
    /// through <c>.tmp</c>, flushed to the disk, then under the file's
    /// name. A broken file goes aside to <c>.bad</c> first (an older
    /// <c>.bad</c> is replaced). Never throws: a failure is a WARN.
    /// </summary>
    /// <returns>True when the file was written.</returns>
    public bool Write(string json) {
        lock (_gate) {
            if (!_mayWrite || _closedForSession) {
                return false;
            }

            try {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                if (_moveAside && File.Exists(_path)) {
                    File.Move(_path, _path + ".bad", overwrite: true);
                    _log.Warn($"{_name} did not read - kept as {_name}.bad, a new one is written");
                }
                _moveAside = false;

                string tmpPath = _path + ".tmp";
                using (var file = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None)) {
                    file.Write(_utf8.GetBytes(json));
                    file.Flush(flushToDisk: true);
                }
                File.Move(tmpPath, _path, overwrite: true);

                return true;
            } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
                _log.Warn($"{_name} not written: {ex.Message}");

                return false;
            }
        }
    }


    private void Settle(StoreReadOutcome outcome, string? why) {
        StoreReadPlan plan;
        bool closesNow;
        lock (_gate) {
            plan = StoreRead.Plan(outcome, firstRead: !_readBefore);
            _readBefore = true;
            LastOutcome = outcome;
            _mayWrite = plan.MayWrite;
            _moveAside = plan.MoveAsideFirst;
            closesNow = plan.ClosedForSession && !_closedForSession;
            _closedForSession |= plan.ClosedForSession;
        }

        if (closesNow) {
            _warned = outcome;
            _log.Warn($"{_name} could not be opened at the start ({why}) - defaults; nothing of this session will be saved to it");

            return;
        }

        if (outcome is StoreReadOutcome.Missing or StoreReadOutcome.Read) {
            _warned = null;

            return;
        }
        if (_warned == outcome) {
            return;
        }

        _warned = outcome;
        switch (outcome) {
            case StoreReadOutcome.Broken:
                _log.Warn($"{_name} did not read ({why}) - defaults; the file goes to {_name}.bad before the next write");
                break;
            case StoreReadOutcome.Unavailable:
                _log.Warn($"{_name} could not be opened ({why}) - defaults; not written until it reads");
                break;
            case StoreReadOutcome.Newer:
                _log.Info($"{_name} is of a newer shape than this build - read as far as it goes, not written");
                break;
        }
    }

    private static T? TryParse<T>(string json, JsonSerializerOptions options, out string? why) where T : class {
        try {
            var value = JsonSerializer.Deserialize<T>(json, options);
            why = value is null ? "empty" : null;

            return value;
        } catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException) {
            why = ex.Message;

            return null;
        }
    }

    /// <summary>The <c>Version</c> of a file whose body may not read; null when there is none to find.</summary>
    private static int? VersionOf(string json) {
        try {
            using var doc = JsonDocument.Parse(json);

            return doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("Version", out var version)
                && version.TryGetInt32(out int number)
                    ? number
                    : null;
        } catch (JsonException) {
            return null;
        }
    }
}
