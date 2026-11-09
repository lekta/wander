using System.Diagnostics;
using System.Windows;
using Wander.Core.Diagnostics;
using Wander.Core.Logging;

namespace Wander.App.Controls;

/// <summary>
/// How long a folder takes to look finished: from the moment it was asked
/// for until every icon on the first screen has its picture.
///
/// <para>
/// "The folder opened" has two readings. The rows land and the list is
/// usable in a few frames; the thumbnails arrive over the next second or
/// so, and that is the moment it stops looking half-done. The session log
/// timed the first for a long time and never the second, which is the one
/// a person walking through photographs actually waits for. One watch at
/// a time: a folder left before it finished is closed as abandoned, with
/// how much was still missing.
/// </para>
///
/// <para>
/// Everything here runs on the UI thread - the icons raise
/// <see cref="AsyncIcon.Painted"/> from the setter of <c>Source</c>, and
/// the view arms the watch after the landing's layout pass.
/// </para>
/// </summary>
public sealed class FirstScreenWatch {
    private static FirstScreenWatch? _current;

    private readonly HashSet<AsyncIcon> _pending;
    private readonly int _total;
    private readonly int _awaited;
    private readonly Stopwatch _clock;
    private readonly string _path;
    private readonly ILogger _log;

    /// <summary>Where the awaited icons spent their time - see <see cref="IconLoadTimes"/>.</summary>
    private readonly Waits _waits = new();

    /// <summary>When the watch was armed, on the navigation's clock: what the listing and its layout took.</summary>
    private readonly long _armedMs;
    private int _gone;


    private FirstScreenWatch(string path, Stopwatch clock, IReadOnlyList<AsyncIcon> visible, ILogger log) {
        _path = path;
        _clock = clock;
        _armedMs = clock.ElapsedMilliseconds;
        _log = log;
        _total = visible.Count;
        _pending = new HashSet<AsyncIcon>();
        foreach (var icon in visible) {
            if (icon.Source is null) {
                _pending.Add(icon);
            }
        }
        _awaited = _pending.Count;
    }


    /// <summary>
    /// Starts timing the folder at <paramref name="path"/>. <paramref name="clock"/>
    /// has been running since the navigation; <paramref name="visible"/> is
    /// what the first screen holds right after the rows landed.
    /// </summary>
    public static void Begin(string path, Stopwatch clock, IReadOnlyList<AsyncIcon> visible, ILogger log) {
        _current?.Close(painted: false);
        var watch = new FirstScreenWatch(path, clock, visible, log);
        _current = watch;
        watch.Arm();
    }


    private void Arm() {
        if (_pending.Count == 0) {
            Close(painted: true);

            return;
        }

        AsyncIcon.Painted += OnPainted;
        foreach (var icon in _pending) {
            icon.Unloaded += OnGone;
        }
    }


    private void OnPainted(AsyncIcon icon) {
        if (!_pending.Remove(icon)) {
            return;
        }

        _waits.Add(icon.LastLoad);
        if (_pending.Count == 0) {
            Close(painted: true);
        }
    }


    /// <summary>
    /// An icon that left the tree before its picture came - the user
    /// scrolled, or the row was replaced. Not waited for any more, and
    /// counted so the line says so.
    /// </summary>
    private void OnGone(object sender, RoutedEventArgs e) {
        if (_pending.Remove((AsyncIcon)sender)) {
            _gone++;
            if (_pending.Count == 0) {
                Close(painted: true);
            }
        }
    }


    private void Close(bool painted) {
        AsyncIcon.Painted -= OnPainted;
        foreach (var icon in _pending) {
            icon.Unloaded -= OnGone;
        }
        if (ReferenceEquals(_current, this)) {
            _current = null;
        }

        long ms = _clock.ElapsedMilliseconds;
        if (painted) {
            string gone = _gone > 0 ? $", {_gone} scrolled away" : "";
            _log.Info($"First screen painted in {ms} ms: {_total} icons, {_awaited} awaited{gone}; armed at {_armedMs} ms{_waits} - {_path}");
            PerfLog.Note("ui.first-screen", ms);
        } else {
            _log.Info($"First screen abandoned after {ms} ms: {_pending.Count} of {_total} icons still missing - {_path}");
        }
    }


    /// <summary>
    /// The four waits of the awaited icons, summed
    /// and at their worst - written into the line as
    /// <c>; waits sum/max ms: layout a/b, gate c/d, work e/f, apply g/h (n)</c>.
    /// </summary>
    private sealed class Waits {
        private readonly double[] _sum = new double[4];
        private readonly double[] _max = new double[4];
        private int _count;


        public void Add(IconLoadTimes? times) {
            if (times is not { IsComplete: true }) {
                return;
            }

            double[] each = { times.LayoutMs, times.GateMs, times.WorkMs, times.ApplyMs };
            for (int i = 0; i < each.Length; i++) {
                _sum[i] += each[i];
                _max[i] = Math.Max(_max[i], each[i]);
            }
            _count++;
        }

        public override string ToString() {
            return _count == 0
                ? ""
                : FormattableString.Invariant(
                    $"; waits sum/max ms: layout {_sum[0]:F0}/{_max[0]:F0}, gate {_sum[1]:F0}/{_max[1]:F0}, work {_sum[2]:F0}/{_max[2]:F0}, apply {_sum[3]:F0}/{_max[3]:F0} ({_count})");
        }
    }
}
