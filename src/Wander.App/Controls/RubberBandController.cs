using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using Wander.Core.FileSystem;

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
/// Implementation notes:
/// </para>
/// <list type="bullet">
///   <item>The marquee is one <see cref="RubberBandAdorner"/> on the host's
///   adorner layer; each mouse move repaints it.</item>
///   <item>Hit-testing walks the host's items and transforms each realised
///   container's bounds back into host coordinates. Virtualised items are
///   skipped — they are off-screen, so a visible rectangle cannot cover
///   them anyway.</item>
///   <item>Mouse capture on the host guarantees the MouseUp even if the
///   cursor leaves the control; <c>LostMouseCapture</c> is the safety net.</item>
/// </list>
/// </summary>
public sealed class RubberBandController {
    private readonly Func<IReadOnlyList<FileSystemEntry>> _items;
    private readonly Action<ItemsControl, IEnumerable<FileSystemEntry>> _setSelection;
    private readonly Action<ItemsControl> _clearSelection;

    private ItemsControl? _host;
    private AdornerLayer? _layer;
    private RubberBandAdorner? _adorner;
    private HashSet<FileSystemEntry>? _baseSelection;
    private Point _origin;
    private bool _armed;


    public RubberBandController(
        Func<IReadOnlyList<FileSystemEntry>> items,
        Action<ItemsControl, IEnumerable<FileSystemEntry>> setSelection,
        Action<ItemsControl> clearSelection) {
        _items = items;
        _setSelection = setSelection;
        _clearSelection = clearSelection;
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
        _origin = e.GetPosition(host);
        _armed = true;
        host.CaptureMouse();
        host.LostMouseCapture += OnLostCapture;
    }


    public void Update(MouseEventArgs e) {
        if (_host is null || _baseSelection is null) {
            return;
        }

        Point current = e.GetPosition(_host);
        if (_armed) {
            if (!PastThreshold(current)) {
                return;
            }

            Begin();
        }

        if (_adorner is not null) {
            _adorner.CurrentPoint = current;
            _adorner.InvalidateVisual();
        }
        var rect = new Rect(_origin, current);

        // Base (already-selected at gesture start, empty in non-additive
        // mode) ∪ everything intersecting the rectangle.
        var selection = new HashSet<FileSystemEntry>(_baseSelection);
        foreach (var entry in _items()) {
            if (TryGetContainerRect(_host, entry, out Rect itemRect) && rect.IntersectsWith(itemRect)) {
                selection.Add(entry);
            }
        }

        _setSelection(_host, selection);
    }


    public void End() {
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
        _layer = null;
        _adorner = null;
        _baseSelection = null;
    }


    /// <summary>
    /// The cursor has moved far enough for the press to be a marquee rather
    /// than a click: put the rectangle on screen.
    /// </summary>
    private void Begin() {
        _armed = false;
        IsActive = true;
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
