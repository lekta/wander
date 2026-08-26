using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Wander.App.Controls;
using Wander.Core.FileSystem;

namespace Wander.App.Util;

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


    public RubberBandController(
        Func<IReadOnlyList<FileSystemEntry>> items,
        Action<ItemsControl, IEnumerable<FileSystemEntry>> setSelection,
        Action<ItemsControl> clearSelection) {
        _items = items;
        _setSelection = setSelection;
        _clearSelection = clearSelection;
    }


    public bool IsActive { get; private set; }

    public bool IsHost(object? candidate) => IsActive && ReferenceEquals(_host, candidate);


    public void Start(ItemsControl host, MouseButtonEventArgs e, IReadOnlyList<FileSystemEntry> selection) {
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
        _layer = AdornerLayer.GetAdornerLayer(host);
        if (_layer is null) {
            // No adorner layer (extremely rare) — proceed without visuals,
            // hit-testing still works.
            _adorner = null;
        } else {
            _adorner = new RubberBandAdorner(host) {
                StartPoint = _origin,
                CurrentPoint = _origin,
            };
            _layer.Add(_adorner);
        }

        IsActive = true;
        host.CaptureMouse();
        host.LostMouseCapture += OnLostCapture;
    }


    public void Update(MouseEventArgs e) {
        if (_host is null || _baseSelection is null) {
            return;
        }

        Point current = e.GetPosition(_host);
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
        if (!IsActive) {
            return;
        }
        IsActive = false;

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
