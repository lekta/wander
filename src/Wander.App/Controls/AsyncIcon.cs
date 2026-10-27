using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Wander.App.Preview;
using Wander.App.Resources;
using Wander.App.ViewModels;
using Wander.Core;
using Wander.Core.Diagnostics;
using Wander.Core.FileSystem;
using Wander.Core.Icons;
using Wander.Core.Logging;
using Wander.Core.Operations;
using Wander.Core.Preview;
using Wander.Core.Shell;

namespace Wander.App.Controls;

/// <summary>
/// An <see cref="Image"/> that pulls its shell icon / thumbnail off the UI
/// thread. Used by the three file-list views, where a folder of heavy files
/// (RAW photos, video) would otherwise freeze the window: the 256 px
/// "Large" icon is a real thumbnail, and extracting one out of a 30 MB
/// .CR3 takes hundreds of milliseconds — times every visible tile.
///
/// <para>
/// Already-cached icons are set synchronously (see
/// <see cref="IIconProvider.TryGetCachedIcon"/>) so scrolling back over
/// seen items doesn't blink. Everything else goes through a worker thread;
/// <see cref="_gate"/> keeps a fast scroll from dumping hundreds of shell
/// calls onto the thread pool at once, and the generation counter drops
/// results that arrive after virtualization recycled the container onto a
/// different file.
/// </para>
///
/// <para>
/// With <see cref="ShowsWork"/> on - the four views of the list - it also
/// wears a small clock while an operation the user started is working on
/// its path: drawn over the picture here rather
/// than by an element in the template, because a mark most cells never
/// show must not cost every cell a visual (WINDOW, "what a tile
/// template may not do"). The row is a record and is never replaced for it.
/// The clock comes up once the work has held the path for
/// <see cref="PathClaims.BadgeDelayMs"/> (2026-09-23): an operation over
/// before that shows none.
/// </para>
///
/// <para>
/// With <see cref="PlaysSound"/> on, a sound file's picture wears a play
/// button under the mouse, and a stop button while it plays (decision
/// 2026-10-27) - drawn here for the same reason as the clock. The press is
/// taken by the list before it selects anything (<see cref="TryPlay"/>):
/// the file is heard, the selection stays.
/// </para>
/// </summary>
public sealed class AsyncIcon : Image {
    /// <summary>One second between the two attempts at a failed icon.</summary>
    private const int RetryDelayMs = 1000;

    /// <summary>The clock (Segoe MDL2 Assets "Recent"), picked from three drafts on 2026-09-21.</summary>
    private const string WorkGlyph = "\uE823";

    private static readonly Typeface _workFace = new("Segoe MDL2 Assets");

    // The play button's disc in its three states, the mark on it white:
    // light on dark reads on any picture and either theme. Faint while the
    // mouse is only on the file, the accent once it is on the button - the
    // moment a press would play (2026-10-27) - dark and solid while it plays.
    private static readonly Brush _playDiscFaint = Frozen(new SolidColorBrush(Color.FromArgb(0x55, 0x10, 0x10, 0x10)));
    private static readonly Brush _playDiscHot = Frozen(new SolidColorBrush(Color.FromArgb(0xE8, 0x00, 0x78, 0xD7)));
    private static readonly Brush _playDiscPlaying = Frozen(new SolidColorBrush(Color.FromArgb(0xC8, 0x10, 0x10, 0x10)));

    private static readonly Brush _playMark = Brushes.White;

    public static readonly DependencyProperty IconPathProperty =
        DependencyProperty.Register(
            nameof(IconPath), typeof(string), typeof(AsyncIcon),
            new PropertyMetadata(null, OnIconRequestChanged));

    public static readonly DependencyProperty IconSizeProperty =
        DependencyProperty.Register(
            nameof(IconSize), typeof(IconSize), typeof(AsyncIcon),
            new PropertyMetadata(IconSize.Normal, OnIconRequestChanged));

    public static readonly DependencyProperty ShowsWorkProperty =
        DependencyProperty.Register(
            nameof(ShowsWork), typeof(bool), typeof(AsyncIcon),
            new PropertyMetadata(false, (d, _) => ((AsyncIcon)d).UpdateWork()));

    public static readonly DependencyProperty PlaysSoundProperty =
        DependencyProperty.Register(
            nameof(PlaysSound), typeof(bool), typeof(AsyncIcon),
            new PropertyMetadata(false, (d, _) => ((AsyncIcon)d).UpdatePlaying()));


    // Thumbnail extraction is disk- and CPU-heavy, so it is metered rather
    // than let loose on the thread pool. Four rather than two: two was
    // sized against the shell call, which does not parallelise, and it
    // became the ceiling once RAW files stopped going through it — a
    // screenful of photographs measured 75 ms per file through the shell
    // and 3 ms per file decoded here (see RawThumbnail), at which point the
    // gate, not the work, was what the user was waiting for. Four still
    // leaves the pool to the rest of the app.
    private static readonly IconLoadGate _gate = new(4);

    /// <summary>
    /// Icons on screen whose file has just changed under them. Static
    /// because the invalidation comes from the folder watcher, which knows
    /// a path and nothing about which control is drawing it; subscription
    /// is tied to Loaded / Unloaded, so only the icons actually in the tree
    /// are ever called - and none of them is kept alive by this.
    /// </summary>
    private static event Action<string>? _invalidated;

    /// <summary>The claims changed, or one came of age: raised on the UI thread, once per burst.</summary>
    private static event Action? _workChanged;

    private static bool _workHooked;
    private static int _workPending;

    /// <summary>Looks again when the next young claim is old enough for its clock - nothing else would say so.</summary>
    private static DispatcherTimer? _workDue;


    private int _generation;
    private bool _detached;
    private bool _listening;
    private bool _working;
    private bool _sound;
    private bool _playing;
    private bool _hot;


    static AsyncIcon() {
        // Every picture that lands, for FirstScreenWatch. Hooked on the
        // property rather than raised from the load paths: a cached icon set
        // synchronously, a decoded one landing at ContextIdle and a fresh
        // shell load all end in the same setter.
        SourceProperty.OverrideMetadata(typeof(AsyncIcon), new FrameworkPropertyMetadata(OnSourceChanged));
    }


    /// <summary>
    /// The setting "read what is on screen first" (Settings.VisibleFirstLoading),
    /// mirrored here because an icon inside an item template has no
    /// DataContext to read it from. With it on, a load for an icon outside
    /// its scroll viewport waits behind every load for one inside; off,
    /// loads go in the order they were asked for.
    /// </summary>
    public static bool VisibleFirst { get; set; }

    /// <summary>Raised on the UI thread when an icon gets its picture - see <see cref="FirstScreenWatch"/>.</summary>
    public static event Action<AsyncIcon>? Painted;


    public AsyncIcon() {
        // Leaving the tree ends the request. A container discarded by a
        // listing swap is never recycled and never has its path changed,
        // so nothing else would bump the generation: its load kept its
        // place at the gate and its delivery its place in the queue, and
        // a folder walked through quickly left hundreds of both behind -
        // exactly what the next folder's thumbnails then waited on.
        Unloaded += (_, _) => {
            Listen(false);
            _generation++;
            _detached = true;
        };
        // A recycled container that comes back for the same file gets no
        // property change to reload it, so it asks again here.
        Loaded += (_, _) => {
            Listen(true);
            if (_detached) {
                Reload();
            }
        };
    }


    public string? IconPath {
        get => (string?)GetValue(IconPathProperty);
        set => SetValue(IconPathProperty, value);
    }

    public IconSize IconSize {
        get => (IconSize)GetValue(IconSizeProperty);
        set => SetValue(IconSizeProperty, value);
    }

    /// <summary>Wear the clock while an operation of the user's claims <see cref="IconPath"/>.</summary>
    public bool ShowsWork {
        get => (bool)GetValue(ShowsWorkProperty);
        set => SetValue(ShowsWorkProperty, value);
    }

    /// <summary>Wear a play button when <see cref="IconPath"/> is a sound file.</summary>
    public bool PlaysSound {
        get => (bool)GetValue(PlaysSoundProperty);
        set => SetValue(PlaysSoundProperty, value);
    }


    /// <summary>
    /// The file at this path is not what it was: drop every cached picture
    /// of it and redraw the icons showing it.
    ///
    /// <para>
    /// Both tiers are keyed by path, and a path does not change when the
    /// file under it does. Deleting a photograph and copying another one
    /// into its place is the case that shows it: the listing is rebuilt,
    /// the row is new, the path is the same, and the tile went on showing
    /// the picture that had been deleted for the rest of the session.
    /// </para>
    /// </summary>
    public static void Invalidate(string path) {
        if (string.IsNullOrEmpty(path)) {
            return;
        }

        ServiceLocator.Get<IIconProvider>().Forget(path);
        IconImageCache.Forget(path);
        _invalidated?.Invoke(path);
    }


    /// <summary>
    /// Drops the cached picture of every row whose file no longer matches
    /// the reading the listing just did.
    ///
    /// <para>
    /// The watcher covers a folder while it is on screen. This covers the
    /// rest: a folder listed, left, edited by another program, and walked
    /// back into — no event ever arrived, and the memory cache is keyed by
    /// path, so it would answer with the old picture for the rest of the
    /// session. One dictionary probe per row, no disk access: the stamp is
    /// what the listing already read.
    /// </para>
    /// </summary>
    public static void DropStale(IReadOnlyList<FileSystemEntry> rows) {
        if (rows.Count == 0) {
            return;
        }

        var icons = ServiceLocator.Get<IIconProvider>();
        foreach (var row in rows) {
            if (row.Kind != EntryKind.File) {
                continue;
            }
            if (icons.ForgetIfChanged(row.FullPath, FileStamp.Of(row.ModifiedUtc, row.Size))) {
                IconImageCache.Forget(row.FullPath);
                _invalidated?.Invoke(row.FullPath);
            }
        }
    }


    /// <summary>
    /// Plays or stops the sound when <paramref name="at"/> - a point on this
    /// icon - is on its play button; false, and nothing done, when it is not.
    /// </summary>
    public bool TryPlay(Point at) {
        if (!IsOnPlay(at)) {
            return false;
        }

        QuickPlayer.Toggle(IconPath!);

        return true;
    }

    /// <summary>Whether <paramref name="at"/>, a point on this icon, is on its play button as drawn now.</summary>
    public bool IsOnPlay(Point at) {
        if (!ShowsPlay) {
            return false;
        }

        var (centre, radius) = PlayDisc();

        // A little past the disc: a button of a dozen pixels is easy to just miss.
        return (at - centre).Length <= radius * 1.2;
    }


    protected override void OnMouseEnter(MouseEventArgs e) {
        base.OnMouseEnter(e);
        if (_sound) {
            InvalidateVisual();
        }
    }

    protected override void OnMouseMove(MouseEventArgs e) {
        base.OnMouseMove(e);
        SetHot(_sound && IsOnPlay(e.GetPosition(this)));
    }

    protected override void OnMouseLeave(MouseEventArgs e) {
        base.OnMouseLeave(e);
        SetHot(false);
        if (_sound) {
            InvalidateVisual();
        }
    }

    protected override void OnRender(DrawingContext dc) {
        base.OnRender(dc);
        if (ShowsPlay) {
            DrawPlay(dc);
        }
        if (!_working) {
            return;
        }

        // Bottom-right on the picture, the size following the icon's: a
        // third of it, never smaller than reads, never bigger than a mark.
        double size = Math.Clamp(RenderSize.Height * 0.35, 9, 16);
        var glyph = new FormattedText(
            WorkGlyph, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            _workFace, size, Palette.WorkBadgeGlyph, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        double side = Math.Max(glyph.Width, glyph.Height) + 2;
        var plate = new Rect(RenderSize.Width - side, RenderSize.Height - side, side, side);
        dc.DrawRoundedRectangle(Palette.WorkBadgeBackground, null, plate, 2, 2);
        dc.DrawText(glyph, new Point(plate.X + (side - glyph.Width) / 2, plate.Y + (side - glyph.Height) / 2));
    }


    /// <summary>A sound file under the mouse, or the one playing.</summary>
    private bool ShowsPlay => _sound && (_playing || IsMouseOver);

    private static void OnIconRequestChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
        var icon = (AsyncIcon)d;
        icon.Reload();
        if (e.Property == IconPathProperty) {
            icon.UpdateWork();
            icon.UpdatePlaying();
        }
    }

    /// <summary>
    /// Whether this is a sound file, and whether it is the one playing; a
    /// redraw only when either changes - the player says it moved on twenty
    /// times a second.
    /// </summary>
    private void UpdatePlaying() {
        bool sound = PlaysSound && IconPath is { Length: > 0 } path && AudioTags.IsAudio(path);
        bool playing = sound && _listening && QuickPlayer.IsPlaying(IconPath);
        if (sound == _sound && playing == _playing) {
            return;
        }

        _sound = sound;
        _playing = playing;
        InvalidateVisual();
    }

    /// <summary>
    /// The play button: on the play mark at the left of the tile's band
    /// (<see cref="SoundTile.ButtonOnTile"/>) - not the middle, where a tile
    /// is taken to be dragged (2026-10-27); on an icon too small for a band,
    /// over most of it.
    /// </summary>
    private (Point Centre, double Radius) PlayDisc() {
        double side = Math.Min(RenderSize.Width, RenderSize.Height);
        var (x, y, r) = SoundTile.ButtonOnTile(side);

        return (new Point((RenderSize.Width - side) / 2 + x, (RenderSize.Height - side) / 2 + y), r);
    }

    /// <summary>The mouse came onto the button or left it: the disc lights up, the cursor says "press".</summary>
    private void SetHot(bool hot) {
        if (hot == _hot) {
            return;
        }

        _hot = hot;
        if (hot) {
            Cursor = Cursors.Hand;
        } else {
            ClearValue(CursorProperty);
        }
        InvalidateVisual();
    }

    private void DrawPlay(DrawingContext dc) {
        var (c, r) = PlayDisc();
        dc.DrawEllipse(_hot ? _playDiscHot : _playing ? _playDiscPlaying : _playDiscFaint, null, c, r, r);

        double m = r * 0.42;
        var mark = new StreamGeometry();
        using (var ctx = mark.Open()) {
            if (_playing) {
                ctx.BeginFigure(new Point(c.X - m * 0.8, c.Y - m * 0.8), isFilled: true, isClosed: true);
                ctx.LineTo(new Point(c.X + m * 0.8, c.Y - m * 0.8), false, false);
                ctx.LineTo(new Point(c.X + m * 0.8, c.Y + m * 0.8), false, false);
                ctx.LineTo(new Point(c.X - m * 0.8, c.Y + m * 0.8), false, false);
            } else {
                // Nudged right: a triangle centred by its box looks left of centre.
                ctx.BeginFigure(new Point(c.X - m * 0.7, c.Y - m), isFilled: true, isClosed: true);
                ctx.LineTo(new Point(c.X + m * 1.05, c.Y), false, false);
                ctx.LineTo(new Point(c.X - m * 0.7, c.Y + m), false, false);
            }
        }

        mark.Freeze();
        dc.DrawGeometry(_playMark, null, mark);
    }

    private static Brush Frozen(Brush brush) {
        brush.Freeze();

        return brush;
    }

    /// <summary>
    /// Subscribes to the claims once, on the first icon that shows work.
    /// Changes arrive on whatever thread claimed or let go; they are
    /// gathered into one pass on this dispatcher.
    /// </summary>
    private static void HookWork(Dispatcher dispatcher) {
        if (_workHooked || ServiceLocator.TryGet<PathClaims>() is not { } claims) {
            return;
        }

        _workHooked = true;
        _workDue = new DispatcherTimer(DispatcherPriority.Background, dispatcher);
        _workDue.Tick += (_, _) => WorkPass(claims);
        claims.Changed += (_, _) => {
            if (Interlocked.Exchange(ref _workPending, 1) == 0) {
                dispatcher.BeginInvoke(DispatcherPriority.Background, () => {
                    Interlocked.Exchange(ref _workPending, 0);
                    WorkPass(claims);
                });
            }
        };
    }

    /// <summary>
    /// One pass over the icons in the tree, and the next one booked for when
    /// the youngest claim still waiting comes of age.
    /// </summary>
    private static void WorkPass(PathClaims claims) {
        _workChanged?.Invoke();
        _workDue!.Stop();
        if (claims.DueInMs(ClaimKind.UserOperation, PathClaims.BadgeDelayMs) is { } due) {
            _workDue.Interval = TimeSpan.FromMilliseconds(Math.Max(1, due));
            _workDue.Start();
        }
    }

    /// <summary>Wears the clock or takes it off; a redraw only when that changes.</summary>
    private void UpdateWork() {
        bool working = ShowsWork && _listening && IconPath is { Length: > 0 } path
            && ServiceLocator.TryGet<PathClaims>()?.IsClaimed(path, ClaimKind.UserOperation, PathClaims.BadgeDelayMs) == true;
        if (working == _working) {
            return;
        }

        _working = working;
        if (working) {
            ToolTip = Strings.WorkBadgeHint;
        } else {
            ClearValue(ToolTipProperty);
        }
        InvalidateVisual();
    }

    private static void OnSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
        if (e.NewValue is not null) {
            Painted?.Invoke((AsyncIcon)d);
        }
    }

    /// <summary>
    /// Subscribes to <see cref="Invalidate"/> while this icon is in the
    /// tree, and only then. The flag guards against a second Loaded without
    /// an Unloaded between them, which is what re-parenting looks like.
    /// </summary>
    private void Listen(bool on) {
        if (on == _listening) {
            return;
        }
        _listening = on;
        if (on) {
            _invalidated += OnInvalidated;
            HookWork(Dispatcher);
            _workChanged += UpdateWork;
            QuickPlayer.Changed += UpdatePlaying;
        } else {
            _invalidated -= OnInvalidated;
            _workChanged -= UpdateWork;
            QuickPlayer.Changed -= UpdatePlaying;
        }
        UpdateWork();
        UpdatePlaying();
    }

    private void OnInvalidated(string path) {
        if (string.Equals(path, IconPath, StringComparison.OrdinalIgnoreCase)) {
            Reload();
        }
    }

    private void Reload() {
        // Bumping the generation invalidates whatever load is in flight for
        // the previous path — its result will be dropped below.
        int generation = ++_generation;
        _detached = false;

        if (IconPath is not { Length: > 0 } path) {
            Source = null;
            return;
        }

        IconSize size = IconSize;
        var icons = ServiceLocator.Get<IIconProvider>();

        byte[]? cached = icons.TryGetCachedIcon(path, size, RowIsFolder());
        if (cached is not null) {
            // Drawn before this session: set synchronously, so scrolling
            // back over seen tiles doesn't blink.
            if (IconImageCache.TryGetDecoded(path, size, cached, out var decoded)) {
                Source = decoded;

                return;
            }

            // A 16-px glyph decodes in microseconds and is what the tree
            // and the Details column run on — it stays synchronous. The
            // thumbnail sizes are the expensive decodes (up to 41 ms each,
            // hundreds per second on a folder revisit — icon.decode-ui in
            // the session log), and only they take the async road.
            if (IsLightweight(size)) {
                using (PerfLog.Measure("icon.decode-ui")) {
                    Source = IconImageCache.Get(path, size, cached);
                }

                return;
            }

            Source = null;
            _ = DecodeAndApplyAsync(path, size, cached, generation);

            return;
        }

        Source = null;
        if (!VisibleFirst) {
            _ = LoadAndApplyAsync(path, size, generation, urgent: true);

            return;
        }

        // Whether this icon is on screen is only known after layout, and a
        // container is bound before it is arranged. Loaded priority runs
        // right after the layout pass, so the answer is real by then; the
        // few milliseconds it costs are the same for every icon and change
        // no order among them.
        _ = Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => {
            if (generation == _generation) {
                _ = LoadAndApplyAsync(path, size, generation, IsInViewport());
            }
        });
    }


    /// <summary>
    /// Folder or file, when the row behind this icon says so - the list's
    /// rows and the panels' nodes do, and the binding that set
    /// <see cref="IconPath"/> read the same DataContext. The cache needs it
    /// (<see cref="IIconProvider.TryGetCachedIcon"/>); read here rather than
    /// bound, so no template pays a second binding per cell for it. An
    /// archive in the panels is a node like a folder and a file to the cache,
    /// and so is a bookmark whose folder is gone: the provider asks the disk,
    /// finds no folder there and files its icon under a file's key.
    /// </summary>
    private bool? RowIsFolder() {
        return DataContext switch {
            FileSystemEntry row => row.Kind != EntryKind.File,
            TreeNodeViewModel { IsMissing: true } => false,
            TreeNodeViewModel node => Archives.Of(node.FullPath) is not { IsRoot: true },
            _ => null,
        };
    }


    /// <summary>
    /// Is this icon inside the viewport of the nearest scroll viewer? An
    /// icon with no scroll viewer above it counts as visible - a preview
    /// pane, a dialog - and one not laid out yet resolves to the origin,
    /// which is inside the viewport, so nothing is held back by mistake.
    /// </summary>
    public bool IsInViewport() {
        if (!IsVisible) {
            return false;
        }

        DependencyObject? current = VisualTreeHelper.GetParent(this);
        while (current is not null && current is not ScrollViewer) {
            current = VisualTreeHelper.GetParent(current);
        }
        if (current is not ScrollViewer scroller) {
            return true;
        }

        try {
            var bounds = TransformToAncestor(scroller).TransformBounds(new Rect(RenderSize));

            return bounds.IntersectsWith(new Rect(0, 0, scroller.ActualWidth, scroller.ActualHeight));
        } catch (InvalidOperationException) {
            // Not connected to the scroller any more - recycled between
            // the question and the answer.
            return false;
        }
    }


    /// <summary>
    /// Small and Normal are one glyph per file *type* — cheap to decode,
    /// few in number, and what the folder panels and the Details view live
    /// on. They are delivered at Normal priority, ahead of the thumbnail
    /// stream: a tree whose rows have no icons reads as broken, while a
    /// tile whose picture arrives a beat later reads as loading. Medium
    /// and Large are per-file thumbnails — the heavy stream that must
    /// never starve input, nor the listing they decorate, so they land at
    /// ContextIdle.
    /// </summary>
    private static bool IsLightweight(IconSize size) {
        return size is IconSize.Small or IconSize.Normal;
    }


    /// <summary>
    /// Decodes bytes the icon cache already had and applies the image the
    /// same way a fresh load does — below input priority.
    /// </summary>
    private async Task DecodeAndApplyAsync(string path, IconSize size, byte[] bytes, int generation) {
        // Re-checked on the pool as well: a folder left behind has hundreds
        // of these queued, and decoding for a tile that is gone is pure
        // waste in front of the next folder's own work.
        var image = await Task.Run(() => generation == _generation ? IconImageCache.Get(path, size, bytes) : null)
            .ConfigureAwait(false);
        if (image is null || generation != _generation) {
            return;
        }

        _ = Dispatcher.BeginInvoke(ApplyPriority(size), () => {
            if (generation == _generation) {
                Source = image;
            }
        });
    }


    private async Task LoadAndApplyAsync(string path, IconSize size, int generation, bool urgent) {
        var image = await LoadAsync(path, size, () => generation == _generation, urgent).ConfigureAwait(false);

        // One more try before giving up. The list heals a failed icon by
        // itself — scrolling realises the container again and re-asks —
        // but the folder panels build their rows once per session, so a
        // load that failed there (the shell flaking under the startup
        // burst) stayed a blank row forever. Legitimate "no preview"
        // answers are negative-cached by the provider, so for those the
        // retry costs one dictionary lookup.
        if (image is null && generation == _generation) {
            await Task.Delay(RetryDelayMs).ConfigureAwait(false);
            if (generation == _generation) {
                image = await LoadAsync(path, size, () => generation == _generation, urgent).ConfigureAwait(false);
            }
        }

        // A load that is still wanted and got nothing is a provider failure
        // the retry above did not heal — the one line that told apart "the
        // shell flaked" from "the delivery was dropped" when tree icons
        // went missing. It fires only on genuine failures, so it can stay.
        // Superseded loads are routine and stay silent.
        if (image is null) {
            if (generation == _generation) {
                Log.Info($"[icon-diag] no icon from provider ({size}) — {path}");
            }

            return;
        }
        if (generation != _generation) {
            return;
        }

        _ = Dispatcher.BeginInvoke(ApplyPriority(size), () => {
            if (generation == _generation) {
                Source = image;
            }
        });
    }


    /// <summary>
    /// The whole pipeline stays off the UI thread, and the finished image
    /// comes back at <see cref="DispatcherPriority.ContextIdle"/> — below
    /// input, and below the listing. A folder of photographs streams in
    /// dozens of thumbnails a second, and landing each one at the default
    /// (Normal) priority put that stream ahead of the user's clicks and
    /// keys in the dispatcher queue: the window went unresponsive for
    /// seconds while it was merely filling in pictures (ui.stall 2.5 s in
    /// the session log, with no navigation in flight at all). Background
    /// fixed that and made the next mistake: the rows of a new folder are
    /// cleared and landed at Background too, and the queue is FIFO, so a
    /// folder walked into from a folder of pictures showed the old rows
    /// filling in until every one of their pictures had landed. Pictures
    /// can wait; the user cannot, and neither can the folder they asked for.
    ///
    /// <para>See <see cref="IsLightweight"/> for why the two classes differ.</para>
    /// </summary>
    private static DispatcherPriority ApplyPriority(IconSize size) {
        return IsLightweight(size) ? DispatcherPriority.Normal : DispatcherPriority.ContextIdle;
    }

    private static async Task<BitmapImage?> LoadAsync(string path, IconSize size, Func<bool> stillWanted, bool urgent) {
        await _gate.WaitAsync(urgent).ConfigureAwait(false);
        try {
            // Re-check after queueing: a fast scroll through a big folder can
            // park hundreds of loads on this gate, and by the time one gets
            // through, its container has usually been recycled onto another
            // file. Doing the shell call anyway would be pure waste — and
            // shell calls are the expensive part.
            if (!stillWanted()) {
                return null;
            }

            return await Task.Run(() => {
                using (PerfLog.Measure("bg.icon-load")) {
                    byte[]? bytes = ServiceLocator.Get<IIconProvider>().GetIcon(path, size);

                    // Decoded here rather than in IconConverter so the result
                    // lands in the decoded cache: the same tile scrolled back
                    // to then costs nothing at all, instead of a decode on the
                    // UI thread the first time it comes round again.
                    return bytes is null ? null : IconImageCache.Get(path, size, bytes);
                }
            }).ConfigureAwait(false);
        } catch {
            // A missing icon is a cosmetic loss; never let it reach the
            // dispatcher's unhandled-exception handler.
            return null;
        } finally {
            _gate.Release();
        }
    }
}
