using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Threading;
using Wander.App.Util;
using Wander.Core.FileSystem;
using Wander.Core.Layout;

namespace Wander.App.Controls;

/// <summary>
/// Marquee ("rubber band") selection in a file list.
///
/// <para>
/// Gesture: press on empty space inside a list and drag. A translucent
/// rectangle follows the cursor; every item whose container intersects it
/// becomes selected. With Ctrl held the rectangle adds to the existing
/// selection (Explorer parity), without it the rectangle replaces it.
/// </para>
///
/// <para>
/// The press only <em>arms</em> the gesture; the rectangle appears once the
/// cursor has moved past the system drag threshold. The gap between two
/// tiles is a few pixels wide, and a click that lands in it with the hand
/// shaking by one pixel used to paint a rectangle across both neighbours
/// and select them — which is how a click on nothing ended up selecting
/// two files. Below the threshold the gesture is a click on the background
/// and nothing else.
/// </para>
///
/// <para>
/// Held at the top or the bottom of the list - or past it - the list scrolls
/// that way (U2, <see cref="EdgeScroll"/>), and the rectangle stretches with
/// it: its far corner stays on the content it was pressed on, not on the
/// screen. A row that scrolls out of view while inside the rectangle stays
/// selected - out of view it has no container to hit-test - until it is back
/// in view and outside.
/// </para>
///
/// <para>
/// Implementation notes:
/// </para>
/// <list type="bullet">
///   <item>The marquee is one <see cref="RubberBandAdorner"/> on the host's
///   adorner layer; each mouse move and each scroll step repaints it.</item>
///   <item>Hit-testing walks the host's items and transforms each realised
///   container's bounds back into host coordinates.</item>
///   <item>Mouse capture on the host guarantees the MouseUp even if the
///   cursor leaves the control; <c>LostMouseCapture</c> is the safety net.</item>
/// </list>
/// </summary>
public sealed class RubberBandController {
    /// <summary>How often a held rectangle looks at the edge.</summary>
    private const int TickMs = 40;

    private readonly Func<IReadOnlyList<FileSystemEntry>> _items;
    private readonly Action<ItemsControl, IEnumerable<FileSystemEntry>> _setSelection;
    private readonly Action<ItemsControl> _clearSelection;
    private readonly DispatcherTimer _tick;

    private ItemsControl? _host;
    private ScrollViewer? _scroller;
    private AdornerLayer? _layer;
    private RubberBandAdorner? _adorner;
    private HashSet<FileSystemEntry>? _baseSelection;
    private readonly HashSet<FileSystemEntry> _swept = new(ReferenceEqualityComparer.Instance);
    private Point _origin;
    private double _originOffset;
    private Point _current;
    private bool _armed;
    private double _scrollCarry;
    private long _lastTickMs;


    public RubberBandController(
        Func<IReadOnlyList<FileSystemEntry>> items,
        Action<ItemsControl, IEnumerable<FileSystemEntry>> setSelection,
        Action<ItemsControl> clearSelection) {
        _items = items;
        _setSelection = setSelection;
        _clearSelection = clearSelection;
        _tick = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(TickMs) };
        _tick.Tick += OnTick;
    }


    /// <summary>True once the rectangle is on screen and painting a selection.</summary>
    public bool IsActive { get; private set; }

    /// <summary>
    /// True while the gesture owns the mouse — armed by a press on empty
    /// space, whether or not the rectangle has appeared yet. What tells the
    /// list that a mouse move belongs here and not to drag arming.
    /// </summary>
    public bool IsHost(object? candidate) => (IsActive || _armed) && ReferenceEquals(_host, candidate);


    /// <summary>
    /// A press landed on empty space. Takes the selection down (the click
    /// on the background it is, so far) and waits to see whether the cursor
    /// moves far enough to mean a marquee.
    /// </summary>
    public void Arm(ItemsControl host, MouseButtonEventArgs e, IReadOnlyList<FileSystemEntry> selection) {
        // If a previous gesture didn't clean up (shouldn't happen, but be
        // robust), drop it first.
        End();

        bool additive = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        _baseSelection = additive ? new HashSet<FileSystemEntry>(selection) : new HashSet<FileSystemEntry>();
        if (!additive) {
            _clearSelection(host);
        }

        _host = host;
        _scroller = ListVisuals.FindDescendant<ScrollViewer>(host);
        _origin = e.GetPosition(host);
        _originOffset = PixelOffset();
        _current = _origin;
        _armed = true;
        host.CaptureMouse();
        host.LostMouseCapture += OnLostCapture;
    }


    public void Update(MouseEventArgs e) {
        if (_host is null || _baseSelection is null) {
            return;
        }

        _current = e.GetPosition(_host);
        if (_armed) {
            if (!PastThreshold(_current)) {
                return;
            }

            Begin();
        }

        Sweep();
    }


    public void End() {
        _tick.Stop();
        if (!IsActive && !_armed) {
            return;
        }
        IsActive = false;
        _armed = false;

        if (_host is { } host) {
            host.LostMouseCapture -= OnLostCapture;
            if (host.IsMouseCaptured) {
                host.ReleaseMouseCapture();
            }
        }
        if (_adorner is not null && _layer is not null) {
            _layer.Remove(_adorner);
        }

        _host = null;
        _scroller = null;
        _layer = null;
        _adorner = null;
        _baseSelection = null;
        _swept.Clear();
        _scrollCarry = 0;
    }


    /// <summary>
    /// The cursor has moved far enough for the press to be a marquee rather
    /// than a click: put the rectangle on screen.
    /// </summary>
    private void Begin() {
        _armed = false;
        IsActive = true;
        _lastTickMs = Environment.TickCount64;
        _tick.Start();
        _layer = _host is null ? null : AdornerLayer.GetAdornerLayer(_host);
        if (_layer is null) {
            // No adorner layer (extremely rare) — proceed without visuals,
            // hit-testing still works.
            _adorner = null;

            return;
        }

        _adorner = new RubberBandAdorner(_host!) {
            StartPoint = _origin,
            CurrentPoint = _origin,
        };
        _layer.Add(_adorner);
    }

    /// <summary>
    /// The rectangle from the corner pressed - where that content is now,
    /// scrolled or not - to the cursor, and the selection it makes: what it
    /// holds on screen, what it held as it scrolled out of view, and what
    /// was selected before with Ctrl.
    /// </summary>
    private void Sweep() {
        if (_host is null || _baseSelection is null) {
            return;
        }

        var start = new Point(_origin.X, _origin.Y - (PixelOffset() - _originOffset));
        if (_adorner is not null) {
            _adorner.StartPoint = start;
            _adorner.CurrentPoint = _current;
            _adorner.InvalidateVisual();
        }

        var rect = new Rect(start, _current);
        var selection = new HashSet<FileSystemEntry>(_baseSelection);
        foreach (var entry in _items()) {
            if (TryGetContainerRect(_host, entry, out Rect itemRect)) {
                if (rect.IntersectsWith(itemRect)) {
                    _swept.Add(entry);
                } else {
                    _swept.Remove(entry);
                }
            }
            if (_swept.Contains(entry)) {
                selection.Add(entry);
            }
        }

        _setSelection(_host, selection);
    }

    /// <summary>A rectangle held at the edge: the list scrolls, and the rectangle stretches with it.</summary>
    private void OnTick(object? sender, EventArgs e) {
        long now = Environment.TickCount64;
        long elapsed = now - _lastTickMs;
        _lastTickMs = now;
        if (!IsActive || _host is null || _scroller is not { ActualHeight: > 0 } scroller) {
            return;
        }

        double y = _host.TranslatePoint(_current, scroller).Y;
        double speed = EdgeScroll.Along(y, scroller.ActualHeight);
        if (speed == 0) {
            _scrollCarry = 0;

            return;
        }

        // A list that scrolls by rows counts its offset in rows: the step is
        // turned into them and handed over whole, the rest carried on.
        _scrollCarry += EdgeScroll.Step(speed, elapsed) * scroller.ViewportHeight / scroller.ActualHeight;
        double whole = Math.Truncate(_scrollCarry);
        if (whole == 0) {
            return;
        }

        scroller.ScrollToVerticalOffset(scroller.VerticalOffset + whole);
        _scrollCarry -= whole;
        // The new rows are realised on the next layout pass; the selection
        // follows once they are.
        _host.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, Sweep);
    }

    /// <summary>How far the list is scrolled, in pixels - a list that scrolls by rows converts at its rows' height.</summary>
    private double PixelOffset() {
        if (_scroller is not { ViewportHeight: > 0 } scroller) {
            return 0;
        }

        return scroller.VerticalOffset * scroller.ActualHeight / scroller.ViewportHeight;
    }


    /// <summary>
    /// The system's own "this is a drag, not a click" distance, in both
    /// axes — the same one the drag source uses, so the two gestures start
    /// at the same remove from the press.
    /// </summary>
    private bool PastThreshold(Point current) {
        return Math.Abs(current.X - _origin.X) >= SystemParameters.MinimumHorizontalDragDistance
            || Math.Abs(current.Y - _origin.Y) >= SystemParameters.MinimumVerticalDragDistance;
    }


    private void OnLostCapture(object sender, MouseEventArgs e) {
        // Some other element grabbed the mouse — wrap up so we don't leave
        // a phantom marquee on screen.
        End();
    }


    /// <summary>
    /// The on-screen rectangle of one entry's container in the host's
    /// coordinate space, or false when the item isn't realised.
    /// </summary>
    private static bool TryGetContainerRect(ItemsControl host, FileSystemEntry entry, out Rect rect) {
        rect = default;
        if (host.ItemContainerGenerator.ContainerFromItem(entry) is not FrameworkElement container
            || container.ActualWidth <= 0 || container.ActualHeight <= 0) {
            return false;
        }

        try {
            var origin = container.TransformToAncestor(host).Transform(new Point(0, 0));
            rect = new Rect(origin, new Size(container.ActualWidth, container.ActualHeight));

            return true;
        } catch (InvalidOperationException) {
            // Container is not in the host's visual tree (mid-virtualisation).
            return false;
        }
    }
}
