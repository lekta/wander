using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Wander.Core.Diagnostics;
using Wander.Core.Layout;

namespace Wander.App.Controls;

/// <summary>
/// A <see cref="WrapPanel"/> that only realises the cells you can see.
///
/// <para>
/// WPF ships no virtualizing wrap panel, and the plain one is why the Tiles
/// and LargeIcons views stalled: opening a folder of twenty thousand files
/// built twenty thousand containers on the dispatcher before the first
/// pixel appeared, and every one of them asked <c>AsyncIcon</c> for a
/// thumbnail. With this panel the list draws as soon as the listing lands,
/// and thumbnails are requested in the order the user actually scrolls
/// through them.
/// </para>
///
/// <para>
/// <b>The cell size is given, not discovered.</b> <see cref="CellWidth"/> and
/// <see cref="CellHeight"/> come from <see cref="TileMetrics"/> — the same
/// value the item templates draw at — and children are measured against
/// exactly that. This is the whole design: layout runs one way, from viewport
/// plus cell size to columns, to a visible range, to positions. The panel
/// used to read the cell size back off a realised container, which closed
/// that street into a ring: content decided the geometry, the geometry
/// decided which containers existed, and a folder ended up choosing how big
/// its own cells were. <see cref="TileMetrics"/> has what that looked like.
/// </para>
///
/// <para>
/// All the arithmetic — columns, cell positions, extent, which items are
/// worth realising — lives in <see cref="TileLayout"/> in Core, where it is
/// covered by tests. What is left here is plumbing: ask the generator for
/// containers, measure them, put them where the layout says. The panel keeps
/// no derived state of its own, so there is nothing to fall out of sync.
/// </para>
///
/// <para>
/// Two things this deliberately does <b>not</b> do, both of which hung the
/// UI when it did: invalidate measure from inside arrange, and call
/// <c>UpdateLayout</c> from <c>BringIndexIntoView</c>. Either one re-enters
/// layout from within layout, and a viewport that disagrees with the measure
/// constraint by a scrollbar's width then ping-pongs between the two
/// forever.
/// </para>
/// </summary>
public class VirtualizingWrapPanel : VirtualizingPanel, IScrollInfo {
    /// <summary>How far one wheel notch moves the view, in device-independent pixels.</summary>
    private const double WheelDelta = 48;

    public static readonly DependencyProperty CellWidthProperty =
        DependencyProperty.Register(
            nameof(CellWidth), typeof(double), typeof(VirtualizingWrapPanel),
            new FrameworkPropertyMetadata(100.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty CellHeightProperty =
        DependencyProperty.Register(
            nameof(CellHeight), typeof(double), typeof(VirtualizingWrapPanel),
            new FrameworkPropertyMetadata(100.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    private Size _viewport;

    /// <summary>How far down the content the viewport sits. There is no
    /// horizontal counterpart: the layout wraps, so nothing is ever to the
    /// right of the viewport.</summary>
    private double _offsetY;

    // What is currently realised, so a pass that changes nothing does no
    // generator work. Measure runs far more often than the range actually
    // moves: every thumbnail that finishes loading sets Image.Source, which
    // is an AffectsMeasure property, so it dirties this panel's measure. In
    // a folder of RAW photos that is one full pass per file — and building
    // and destroying containers on each of them is what made scrolling feel
    // like a freeze.
    private int _realisedFirst;
    private int _realisedLast = -1;

    // Last values handed to the scroll owner. Telling it "something changed"
    // makes it invalidate its own measure, which brings us straight back
    // here — so only say it when it is true.
    private Size _notifiedExtent = Size.Empty;
    private Size _notifiedViewport = Size.Empty;
    private double _notifiedOffset = -1;

    /// <summary>
    /// The layout the last measure settled on. Arrange reads it rather than
    /// recomputing, so cells cannot be placed on a different grid from the
    /// one their containers were generated for.
    /// </summary>
    private TileLayout _layout;


    /// <summary>Width of one cell, its margin included — see <see cref="TileMetrics.CellWidth"/>.</summary>
    public double CellWidth {
        get => (double)GetValue(CellWidthProperty);
        set => SetValue(CellWidthProperty, value);
    }

    /// <summary>Height of one cell, its margin included.</summary>
    public double CellHeight {
        get => (double)GetValue(CellHeightProperty);
        set => SetValue(CellHeightProperty, value);
    }


    /// <summary>
    /// Cells per row as of the last layout pass — what arrow keys need to
    /// know to step by a whole row. Zero until the panel has been measured.
    /// </summary>
    public int Columns => _layout.Columns;


    // --- IScrollInfo: wiring ------------------------------------------

    public bool CanHorizontallyScroll { get; set; }

    public bool CanVerticallyScroll { get; set; }

    public ScrollViewer? ScrollOwner { get; set; }

    public double ExtentWidth => _layout.ExtentWidth;

    public double ExtentHeight => _layout.ExtentHeight;

    public double ViewportWidth => _viewport.Width;

    public double ViewportHeight => _viewport.Height;

    public double HorizontalOffset => 0;

    public double VerticalOffset => _offsetY;


    // --- IScrollInfo: stepping ----------------------------------------
    // Vertical only. The layout wraps, so the content is never wider than
    // the viewport and there is nothing to the right to scroll to; the
    // horizontal half of the interface has to exist and has nothing to do.

    public void LineUp() {
        SetVerticalOffset(VerticalOffset - WheelDelta);
    }

    public void LineDown() {
        SetVerticalOffset(VerticalOffset + WheelDelta);
    }

    public void LineLeft() {
    }

    public void LineRight() {
    }

    public void PageUp() {
        SetVerticalOffset(VerticalOffset - _viewport.Height);
    }

    public void PageDown() {
        SetVerticalOffset(VerticalOffset + _viewport.Height);
    }

    public void PageLeft() {
    }

    public void PageRight() {
    }

    public void MouseWheelUp() {
        SetVerticalOffset(VerticalOffset - (WheelDelta * SystemParameters.WheelScrollLines));
    }

    public void MouseWheelDown() {
        SetVerticalOffset(VerticalOffset + (WheelDelta * SystemParameters.WheelScrollLines));
    }

    public void MouseWheelLeft() {
    }

    public void MouseWheelRight() {
    }

    public void SetHorizontalOffset(double offset) {
    }

    public void SetVerticalOffset(double offset) {
        double clamped = _layout.Clamp(offset);
        if (AreClose(clamped, _offsetY)) {
            return;
        }

        _offsetY = clamped;
        InvalidateMeasure();
        NotifyScrollOwner();
    }


    /// <summary>
    /// Scrolls <paramref name="visual"/> into view. This is what backs
    /// <c>ListBox.ScrollIntoView</c>, keyboard navigation and the inline
    /// rename editor's "the row must exist before it can be focused".
    /// </summary>
    public Rect MakeVisible(Visual visual, Rect rectangle) {
        if (FindChild(visual) is not { } child) {
            return rectangle;
        }

        int childIndex = InternalChildren.IndexOf(child);
        if (childIndex < 0) {
            return rectangle;
        }

        int index = ItemContainerGenerator.IndexFromGeneratorPosition(new GeneratorPosition(childIndex, 0));
        if (index < 0) {
            return rectangle;
        }

        SetVerticalOffset(_layout.OffsetToReveal(index, _offsetY));

        // The answer has to be in this panel's own coordinates, not in the
        // grid's: the caller compares it against the viewport to decide
        // whether it got what it asked for. Handing back grid coordinates
        // means an item far down the list looks permanently off-screen, and
        // the caller keeps asking — which is a scroll that fights the user.
        var cell = _layout.CellAt(index);

        return new Rect(cell.X, cell.Y - _offsetY, cell.Width, cell.Height);
    }


    // --- Layout --------------------------------------------------------

    protected override Size MeasureOverride(Size availableSize) {
        using var pass = PerfLog.Measure("layout.measure");

        // Touching InternalChildren is what instantiates the generator;
        // without it ItemContainerGenerator is null on the first pass.
        var children = InternalChildren;
        var generator = ItemContainerGenerator;
        int count = ItemCount;

        // The constraint *is* the viewport: a ScrollViewer whose content
        // does its own scrolling measures it at the size it will show, not
        // at infinity. An infinite one means the panel is being measured
        // outside a scroll viewer — fall back on what was last seen.
        SetViewport(new Size(
            double.IsInfinity(availableSize.Width) ? _viewport.Width : availableSize.Width,
            double.IsInfinity(availableSize.Height) ? _viewport.Height : availableSize.Height));

        var cell = new Size(Math.Max(1, CellWidth), Math.Max(1, CellHeight));
        _layout = new TileLayout(ColumnWidth(count, cell), _viewport.Height, cell.Width, cell.Height, count);
        _offsetY = _layout.Clamp(_offsetY);

        var (first, last) = _layout.VisibleRange(_offsetY);
        if (first != _realisedFirst || last != _realisedLast) {
            RealiseRange(generator, cell, first, last);
            RecycleOutside(generator, first, last);
            _realisedFirst = first;
            _realisedLast = last;
        } else {
            // The common pass: the range has not moved, something inside a
            // cell just changed size — a thumbnail arriving. A dirty child
            // whose parent never measures it keeps the layout queue dirty
            // forever, so this is not optional; but WPF skips children that
            // are clean, so it costs almost nothing.
            foreach (UIElement child in children) {
                child.Measure(cell);
            }
        }

        NotifyScrollOwner();

        return new Size(
            double.IsInfinity(availableSize.Width) ? _layout.ExtentWidth : availableSize.Width,
            double.IsInfinity(availableSize.Height) ? _layout.ExtentHeight : availableSize.Height);
    }


    protected override Size ArrangeOverride(Size finalSize) {
        using var pass = PerfLog.Measure("layout.arrange");

        for (int i = 0; i < InternalChildren.Count; i++) {
            // Asking the generator rather than assuming child i is item
            // first+i: a mid-layout item change can leave the two out of
            // step for one pass, and a cell arranged at the wrong index is
            // a row of files drawn on top of each other.
            int index = ItemContainerGenerator.IndexFromGeneratorPosition(new GeneratorPosition(i, 0));
            var cell = _layout.CellAt(index < 0 ? i : index);
            InternalChildren[i].Arrange(new Rect(
                cell.X,
                cell.Y - _offsetY,
                cell.Width,
                cell.Height));
        }

        return finalSize;
    }


    /// <summary>
    /// Keyboard navigation (and <c>ScrollIntoView</c> on an item that is not
    /// realised) asks for an index to be brought into view before the
    /// container exists; scrolling to its computed cell is what makes the
    /// next measure pass realise it. No <c>UpdateLayout</c> here — this can
    /// be called from inside a layout pass, and forcing another one from
    /// there is a way to hang.
    /// </summary>
    protected override void BringIndexIntoView(int index) {
        if (index < 0 || index >= ItemCount) {
            return;
        }

        SetVerticalOffset(_layout.OffsetToReveal(index, _offsetY));
    }


    protected override void OnItemsChanged(object sender, ItemsChangedEventArgs args) {
        switch (args.Action) {
            case NotifyCollectionChangedAction.Remove:
            case NotifyCollectionChangedAction.Replace:
            case NotifyCollectionChangedAction.Move:
                RemoveInternalChildRange(args.Position.Index, args.ItemUICount);
                break;
            case NotifyCollectionChangedAction.Reset:
                RemoveInternalChildRange(0, InternalChildren.Count);
                _offsetY = 0;
                break;
        }

        // Whatever was realised describes the old collection.
        _realisedFirst = 0;
        _realisedLast = -1;

        InvalidateMeasure();
        NotifyScrollOwner();
    }


    // --- Realisation ---------------------------------------------------

    private void RealiseRange(IItemContainerGenerator generator, Size cell, int first, int last) {
        if (last < first) {
            return;
        }

        // Container generation: the template is built, its bindings are
        // resolved and it is measured, all on the UI thread. This is the
        // per-row cost of scrolling.
        using var pass = PerfLog.Measure("layout.realise");

        var start = generator.GeneratorPositionFromIndex(first);
        // A position with Offset 0 means "at this container"; the child index
        // to insert at is one past it. Offset != 0 means the item is not
        // realised, and generation starts after the previous container.
        int childIndex = start.Offset == 0 ? start.Index : start.Index + 1;

        using (generator.StartAt(start, GeneratorDirection.Forward, allowStartAtRealizedItem: true)) {
            for (int i = first; i <= last; i++, childIndex++) {
                if (generator.GenerateNext(out bool isNew) is not UIElement child) {
                    break;
                }
                // Recycling mode hands back a container that already exists
                // but is no longer a child of this panel: isNew is false and
                // it still has to be put in the tree and re-bound. Testing
                // for membership rather than for isNew covers both that and
                // a genuinely new container.
                if (isNew || !InternalChildren.Contains(child)) {
                    if (childIndex >= InternalChildren.Count) {
                        AddInternalChild(child);
                    } else {
                        InsertInternalChild(childIndex, child);
                    }
                    generator.PrepareItemContainer(child);
                }

                // Measured against the cell, never against infinity: the
                // container is being told how much room it has, not asked.
                // Asking is what let a half-built template — or an Image
                // reporting the pixel size of the photograph it just
                // loaded — redefine the grid for the whole folder.
                child.Measure(cell);
            }
        }
    }

    /// <summary>
    /// Lets go of the containers that scrolled out of view.
    ///
    /// <para>
    /// <c>Recycle</c> rather than <c>Remove</c>: the container goes back to
    /// the generator's pool and comes out again for the next item instead of
    /// being thrown away and its template inflated from scratch. Building
    /// containers was the most expensive thing the UI thread did while
    /// scrolling — 260–400 ms per second of scrolling in a folder of RAW
    /// photos, with the window not answering for a third of a second at a
    /// time. Requires <c>VirtualizationMode.Recycling</c> on the ItemsControl,
    /// which the tile views set.
    /// </para>
    /// </summary>
    private void RecycleOutside(IItemContainerGenerator generator, int first, int last) {
        // Recycling is the ItemsControl's setting; a host that leaves it off
        // gets the old behaviour rather than a broken panel.
        var pool = generator as IRecyclingItemContainerGenerator;

        for (int i = InternalChildren.Count - 1; i >= 0; i--) {
            var position = new GeneratorPosition(i, 0);
            int itemIndex = generator.IndexFromGeneratorPosition(position);
            if (itemIndex < first || itemIndex > last) {
                if (pool is not null) {
                    pool.Recycle(position, 1);
                } else {
                    generator.Remove(position, 1);
                }
                RemoveInternalChildRange(i, 1);
            }
        }
    }


    // --- State ---------------------------------------------------------

    private int ItemCount => ItemsControl.GetItemsOwner(this)?.Items.Count ?? 0;

    /// <summary>
    /// The width the columns are counted in — the viewport, minus the
    /// vertical scrollbar when the content is going to need one.
    ///
    /// <para>
    /// With an automatic scrollbar the ScrollViewer measures its content at
    /// the full width first and only afterwards discovers that a bar is
    /// needed; the next measure comes in a scrollbar narrower. A wrap layout
    /// can legitimately want the bar at one of those widths and not at the
    /// other — nine columns of content fit without a bar, eight columns of
    /// the same content do not — and then the two answers chase each other
    /// for as long as the window is open. Deciding it here instead means the
    /// column count is the same in both passes, so there is nothing to
    /// chase. Costs one throwaway layout value on the passes where the bar
    /// is not up yet.
    /// </para>
    /// </summary>
    private double ColumnWidth(int count, Size cell) {
        double width = _viewport.Width;
        if (ScrollOwner is not { } owner
            || owner.VerticalScrollBarVisibility != ScrollBarVisibility.Auto
            || owner.ComputedVerticalScrollBarVisibility == Visibility.Visible) {
            // Fixed policy, or the bar is already up and the width handed to
            // us already excludes it.
            return width;
        }

        var withoutBar = new TileLayout(width, _viewport.Height, cell.Width, cell.Height, count);

        return withoutBar.ExtentHeight > _viewport.Height
            ? Math.Max(0, width - SystemParameters.VerticalScrollBarWidth)
            : width;
    }


    private void SetViewport(Size viewport) {
        if (AreClose(viewport.Width, _viewport.Width) && AreClose(viewport.Height, _viewport.Height)) {
            return;
        }

        _viewport = viewport;
    }


    /// <summary>
    /// Tells the scroll owner what changed — and only when something did.
    /// An unconditional call here is a loop: the owner answers by
    /// invalidating its own measure, which measures this panel again.
    /// </summary>
    private void NotifyScrollOwner() {
        var extent = new Size(_layout.ExtentWidth, _layout.ExtentHeight);
        if (AreClose(extent.Width, _notifiedExtent.Width)
            && AreClose(extent.Height, _notifiedExtent.Height)
            && AreClose(_viewport.Width, _notifiedViewport.Width)
            && AreClose(_viewport.Height, _notifiedViewport.Height)
            && AreClose(_offsetY, _notifiedOffset)) {
            return;
        }

        _notifiedExtent = extent;
        _notifiedViewport = _viewport;
        _notifiedOffset = _offsetY;
        ScrollOwner?.InvalidateScrollInfo();
    }


    private UIElement? FindChild(Visual visual) {
        DependencyObject? current = visual;
        while (current is not null) {
            if (current is UIElement element && InternalChildren.Contains(element)) {
                return element;
            }
            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private static bool AreClose(double a, double b) {
        return Math.Abs(a - b) < 0.5;
    }
}
