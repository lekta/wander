using System.Collections.Concurrent;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Wander.App.Converters;
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
///
/// <para>
/// A <see cref="IconSize.Large"/> picture wears the shell's badge - a
/// TortoiseGit state - in its lower left corner at a size of its own, drawn
/// here too, not baked into the picture: the picture is cached for days, the
/// state changes with a commit (<see cref="IIconProvider.OverlayOf"/>).
/// </para>
/// </summary>
public sealed class AsyncIcon : Image {
    /// <summary>One second between the two attempts at a failed icon.</summary>
    private const int RetryDelayMs = 1000;

    /// <summary>A picture this late gets a stand-in (decision 2026-11-09): sooner, the stand-in itself would be a flash.</summary>
    private const int InterimDelayMs = 150;

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

    public static readonly DependencyProperty FolderGridProperty =
        DependencyProperty.Register(
            nameof(FolderGrid), typeof(bool), typeof(AsyncIcon),
            new PropertyMetadata(false, OnIconRequestChanged));


    // Thumbnail extraction is disk- and CPU-heavy, so it is metered rather
    // than let loose on the thread pool. Four rather than two: two was
    // sized against the shell call, which does not parallelise, and it
    // became the ceiling once RAW files stopped going through it — a
    // screenful of photographs measured 75 ms per file through the shell
    // and 3 ms per file decoded here (see RawThumbnail), at which point the
    // gate, not the work, was what the user was waiting for. Four still
    // leaves the pool to the rest of the app.
    private static readonly IconLoadGate _gate = new(4);

    /// <summary>The badges, decoded once by overlay index; null where the shell had no picture.</summary>
    private static readonly ConcurrentDictionary<int, ImageSource?> _badges = new();

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

    /// <summary>
    /// Icons whose picture is being made and that have no stand-in yet; one
    /// timer for all of them (<see cref="InterimDelayMs"/>), running only
    /// while there are any.
    /// </summary>
    private static readonly HashSet<AsyncIcon> _waiting = new();

    private static DispatcherTimer? _interimDue;

    /// <summary>
    /// Thumbnails made and waiting to be set, landed together: one dispatcher
    /// operation - and one layout and render - for a burst of them, rather
    /// than one each. A folder of sources had its pictures ready in 0,3 s and
    /// on screen in 1 s, waiting in the queue (first-screen line, 2026-11-09).
    /// </summary>
    private static readonly ConcurrentQueue<Action> _landing = new();

    private static int _landingPosted;


    private int _generation;
    private bool _detached;
    private bool _listening;
    private bool _working;
    private bool _sound;
    private bool _playing;
    private bool _hot;
    private ImageSource? _badge;

    /// <summary>
    /// The load in flight, cancelled when the icon leaves the tree or asks
    /// for something else: a track's shape, or a folder of tracks, stops
    /// half-way instead of holding one of the four slots for a second after
    /// the user has gone (2026-11-09).
    /// </summary>
    private CancellationTokenSource? _load;

    /// <summary>When this icon joined <see cref="_waiting"/>.</summary>
    private long _waitingSince;

    /// <summary>
    /// The stand-in while the picture is made (second fitting, 2026-11-09):
    /// a folder's silhouette, or a file's type icon drawn on a plate. Drawn
    /// here, not set as <see cref="Image.Source"/>: the picture's arrival
    /// is still the one change of Source, and the first screen does not
    /// count a stand-in as painted.
    /// </summary>
    private ImageSource? _interim;

    /// <summary>The stand-in made off the UI thread, not shown yet.</summary>
    private ImageSource? _interimReady;

    private bool _interimIsFolder;

    /// <summary>A picture file: its plate covers the place, as the photograph will.</summary>
    private bool _interimFills;


    static AsyncIcon() {
        // Every picture that lands, for FirstScreenWatch. Hooked on the
        // property rather than raised from the load paths: a cached icon set
        // synchronously, a decoded one landing with its burst and a fresh
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

    /// <summary>
    /// Where the last load of this icon spent its time, for
    /// <see cref="FirstScreenWatch"/>'s line. Null for a picture set from the
    /// cache.
    /// </summary>
    internal IconLoadTimes? LastLoad { get; private set; }


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
            CancelLoad();
            DropInterim();
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
    /// Draw a folder with its files laid out as its face, the gallery's way
    /// (PLAN F1, decision 2026-11-06); off, they are cards slid into it.
    /// The two looks are two pictures of one folder (<see cref="FolderLook"/>).
    /// </summary>
    public bool FolderGrid {
        get => (bool)GetValue(FolderGridProperty);
        set => SetValue(FolderGridProperty, value);
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
    ///
    /// <para>
    /// Folders as well: their picture shows what they hold, and a file
    /// copied in while the folder's parent was not on screen moves the
    /// folder's last-write time and nothing else Wander would see.
    /// </para>
    /// </summary>
    public static void DropStale(IReadOnlyList<FileSystemEntry> rows) {
        if (rows.Count == 0) {
            return;
        }

        var icons = ServiceLocator.Get<IIconProvider>();
        foreach (var row in rows) {
            if (row.Kind is not (EntryKind.File or EntryKind.Directory)) {
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

    /// <summary>No picture yet, a stand-in shown: the place a picture would take - square, as the stand-in is.</summary>
    protected override Size MeasureOverride(Size constraint) {
        return Source is null && _interim is not null ? InterimSize(constraint) : base.MeasureOverride(constraint);
    }

    protected override Size ArrangeOverride(Size arrangeSize) {
        return Source is null && _interim is not null ? InterimSize(arrangeSize) : base.ArrangeOverride(arrangeSize);
    }

    protected override void OnRender(DrawingContext dc) {
        if (Source is null) {
            if (_interim is not null) {
                DrawInterim(dc);
            }
        } else {
            base.OnRender(dc);
        }
        if (_badge is not null && Source is not null) {
            // Lower left on the picture, where Explorer puts it: 3/16 of the
            // side as on its 256 jumbo, held between what reads and a mark.
            double badge = Math.Clamp(Math.Min(RenderSize.Width, RenderSize.Height) * 3 / 16, 12, 24);
            dc.DrawImage(_badge, new Rect(0, RenderSize.Height - badge, badge, badge));
        }
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
            var icon = (AsyncIcon)d;
            icon.DropInterim();
            Painted?.Invoke(icon);
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

    /// <summary>
    /// The picture of this path changed: asked again, the old one kept on
    /// screen until the new one lands - a cell that goes empty in between
    /// is one more change of frame than the picture needs (pillar 4).
    /// </summary>
    private void OnInvalidated(string path) {
        if (string.Equals(path, IconPath, StringComparison.OrdinalIgnoreCase)) {
            Reload(keepShown: true);
        }
    }

    /// <param name="keepShown">The same path asked again: what is on screen stays until the new picture replaces it.</param>
    private void Reload(bool keepShown = false) {
        // Bumping the generation invalidates whatever load is in flight for
        // the previous path — its result will be dropped below.
        int generation = ++_generation;
        _detached = false;
        CancelLoad();
        DropInterim();
        LastLoad = null;
        if (_badge is not null) {
            _badge = null;
            InvalidateVisual();
        }

        if (IconPath is not { Length: > 0 } path) {
            Source = null;
            return;
        }

        IconSize size = IconSize;
        if (size == IconSize.Large) {
            _ = LoadBadgeAsync(path, generation);
        }
        var icons = ServiceLocator.Get<IIconProvider>();
        var look = new FolderLook(FolderGrid);

        byte[]? cached = icons.TryGetCachedIcon(path, size, RowIsFolder(), look);
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

            if (!keepShown) {
                Source = null;
            }
            LastLoad = new IconLoadTimes();
            _ = DecodeAndApplyAsync(path, size, cached, generation, LastLoad);

            return;
        }

        if (!keepShown) {
            Source = null;
        }
        var times = new IconLoadTimes();
        LastLoad = times;
        var load = new CancellationTokenSource();
        _load = load;
        var cancel = load.Token;
        if (Source is null && !IsLightweight(size)) {
            StartWaiting();
        }
        if (!VisibleFirst) {
            times.Started = times.Asked;
            _ = LoadAndApplyAsync(path, size, look, generation, urgent: true, times, cancel);

            return;
        }

        // Whether this icon is on screen is only known after layout, and a
        // container is bound before it is arranged. Loaded priority runs
        // right after the layout pass, so the answer is real by then; the
        // few milliseconds it costs are the same for every icon and change
        // no order among them.
        _ = Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => {
            if (generation == _generation) {
                times.Started = Stopwatch.GetTimestamp();
                _ = LoadAndApplyAsync(path, size, look, generation, IsInViewport(), times, cancel);
            }
        });
    }


    /// <summary>Gives up the load in flight, if any: its token says so to the work still running.</summary>
    private void CancelLoad() {
        if (_load is null) {
            return;
        }

        // Not disposed, as in WaveformStrip: the work still running only
        // reads the token, and a source with no timer holds nothing.
        _load.Cancel();
        _load = null;
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
    /// never starve input, so they land together, one operation a burst (see
    /// <see cref="Land"/>).
    /// </summary>
    private static bool IsLightweight(IconSize size) {
        return size is IconSize.Small or IconSize.Normal;
    }


    /// <summary>
    /// Decodes bytes the icon cache already had and applies the image the
    /// same way a fresh load does — below input priority.
    /// </summary>
    private async Task DecodeAndApplyAsync(string path, IconSize size, byte[] bytes, int generation, IconLoadTimes times) {
        times.Started = times.Admitted = times.Asked;
        // Re-checked on the pool as well: a folder left behind has hundreds
        // of these queued, and decoding for a tile that is gone is pure
        // waste in front of the next folder's own work.
        var image = await Task.Run(() => generation == _generation ? IconImageCache.Get(path, size, bytes) : null)
            .ConfigureAwait(false);
        if (image is null || generation != _generation) {
            return;
        }

        times.Done = Stopwatch.GetTimestamp();
        Land(size, () => {
            if (generation == _generation) {
                times.Applied = Stopwatch.GetTimestamp();
                Source = image;
            }
        });
    }


    /// <summary>
    /// The shell's badge for the file, asked on every load and off the UI
    /// thread - outside <see cref="_gate"/>: one shell call, no picture to
    /// make. Landed with the pictures, below input.
    /// </summary>
    private async Task LoadBadgeAsync(string path, int generation) {
        var badge = await Task.Run(() => {
            if (generation != _generation) {
                return null;
            }

            try {
                var icons = ServiceLocator.Get<IIconProvider>();
                int index = icons.OverlayOf(path);

                return index == 0
                    ? null
                    : _badges.GetOrAdd(index, i => icons.OverlayImage(i) is { } png ? IconConverter.ToImage(png) : null);
            } catch {
                // A badge is cosmetic, like the picture under it.
                return null;
            }
        }).ConfigureAwait(false);
        if (badge is null || generation != _generation) {
            return;
        }

        // With the pictures (see Land): below Input it waits out every animation.
        _ = Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => {
            if (generation == _generation) {
                _badge = badge;
                InvalidateVisual();
            }
        });
    }


    private async Task LoadAndApplyAsync(
        string path, IconSize size, FolderLook look, int generation, bool urgent, IconLoadTimes times, CancellationToken cancel) {
        var image = await LoadAsync(path, size, look, () => generation == _generation, urgent, times, cancel).ConfigureAwait(false);

        // One more try before giving up. The list heals a failed icon by
        // itself — scrolling realises the container again and re-asks —
        // but the folder panels build their rows once per session, so a
        // load that failed there (the shell flaking under the startup
        // burst) stayed a blank row forever. Legitimate "no preview"
        // answers are negative-cached by the provider, so for those the
        // retry costs one dictionary lookup.
        if (image is null && generation == _generation) {
            // Not on the token: a load given up meanwhile is told by the
            // generation below, without an exception in a task nobody awaits.
            await Task.Delay(RetryDelayMs, CancellationToken.None).ConfigureAwait(false);
            if (generation == _generation) {
                image = await LoadAsync(path, size, look, () => generation == _generation, urgent, times, cancel).ConfigureAwait(false);
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
                // No picture is coming: out of the stand-ins' wait, or their
                // timer would tick for as long as the cell is on screen. A
                // stand-in already shown stays - better than an empty cell.
                _ = Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => {
                    if (generation == _generation) {
                        StopWaiting();
                    }
                });
            }

            return;
        }
        if (generation != _generation) {
            return;
        }

        Land(size, () => {
            if (generation == _generation) {
                times.Applied = Stopwatch.GetTimestamp();
                Source = image;
            }
        });
    }


    /// <summary>
    /// The whole pipeline stays off the UI thread; the finished pictures land
    /// together (<see cref="_landing"/>) - one operation for whatever is
    /// ready, at <see cref="DispatcherPriority.Loaded"/>.
    ///
    /// <para>
    /// One operation per picture at Normal put the stream ahead of the user's
    /// clicks: the window went unresponsive for seconds while it filled in
    /// pictures (ui.stall 2.5 s). The cost was the number of operations, not
    /// the priority: one cheap operation per burst does not hold input.
    /// </para>
    ///
    /// <para>
    /// Below Input they starve (2026-11-09, stand of a smoke run with a
    /// dispatcher ping): WPF serves Input and Background one operation per
    /// turn, higher first, and while anything animates - a spinner turning,
    /// counting the folder in the preview pane - every frame queues
    /// <c>InputManager.HitTestInvalidatedAsync</c> at Input. There is always
    /// one ahead, and a Background operation waited 100-300 ms on a thread
    /// with nothing to do (a Send one took 0,5 ms): the pictures and their
    /// stand-ins came all at once when the spinner stopped. ContextIdle,
    /// the priority before, waited a second.
    /// </para>
    ///
    /// <para>See <see cref="IsLightweight"/> for why the two classes differ.</para>
    /// </summary>
    private void Land(IconSize size, Action apply) {
        if (IsLightweight(size)) {
            _ = Dispatcher.BeginInvoke(DispatcherPriority.Normal, apply);

            return;
        }

        _landing.Enqueue(apply);
        if (Interlocked.Exchange(ref _landingPosted, 1) == 0) {
            _ = Dispatcher.BeginInvoke(DispatcherPriority.Loaded, LandAll);
        }
    }

    private static void LandAll() {
        Interlocked.Exchange(ref _landingPosted, 0);
        while (_landing.TryDequeue(out var apply)) {
            apply();
        }
    }


    /// <summary>
    /// The picture is being made: the stand-in is prepared now and shown if
    /// the picture is still not here in <see cref="InterimDelayMs"/>. Only a
    /// row that says what it is gets one.
    /// </summary>
    private void StartWaiting() {
        if (RowIsFolder() is not { } folder || IconPath is not { Length: > 0 } path || _load is not { } load) {
            return;
        }

        _waitingSince = Stopwatch.GetTimestamp();
        _waiting.Add(this);
        if (_interimDue is null) {
            // Loaded, as the pictures land (see Land): a Background tick
            // came twice a second while a spinner turned.
            _interimDue = new DispatcherTimer(DispatcherPriority.Loaded, Dispatcher) {
                Interval = TimeSpan.FromMilliseconds(30),
            };
            _interimDue.Tick += (_, _) => InterimPass();
        }
        _interimDue.Start();
        PrepareInterim(path, folder, load.Token);
    }

    private void StopWaiting() {
        _waiting.Remove(this);
    }

    /// <summary>
    /// Every icon whose wait is over and whose stand-in is ready shows it, all
    /// in this one pass - one frame for the screenful, not one each as they
    /// trickle in. One not ready yet waits for the next pass. The timer stops
    /// once none waits.
    /// </summary>
    private static void InterimPass() {
        long due = Stopwatch.GetTimestamp() - (Stopwatch.Frequency * InterimDelayMs / 1000);
        List<AsyncIcon>? ripe = null;
        foreach (var icon in _waiting) {
            if (icon._waitingSince <= due && Volatile.Read(ref icon._interimReady) is not null) {
                (ripe ??= new List<AsyncIcon>()).Add(icon);
            }
        }
        if (ripe is not null) {
            foreach (var icon in ripe) {
                _waiting.Remove(icon);
                icon.ShowInterim();
            }
        }
        if (_waiting.Count == 0) {
            _interimDue!.Stop();
        }
    }

    /// <summary>
    /// Makes the stand-in off the UI thread, outside the gate - a walk and
    /// type icons, not a picture: a folder's silhouette, a file's type icon.
    /// Kept in <see cref="_interimReady"/> for <see cref="InterimPass"/>.
    /// </summary>
    private void PrepareInterim(string path, bool folder, CancellationToken cancel) {
        int generation = _generation;
        var size = IconSize;
        var look = new FolderLook(FolderGrid);
        _ = Task.Run(() => {
            if (generation != _generation) {
                return;
            }

            try {
                var icons = ServiceLocator.Get<IIconProvider>();
                byte[]? bytes = folder ? icons.GetSilhouette(path, size, look, cancel) : icons.GetIcon(path, IconSize.Normal);
                if (bytes is not null && generation == _generation) {
                    var image = IconConverter.ToImage(bytes);
                    Volatile.Write(ref _interimReady, image);
                    // Asked for something else meanwhile: not this one's stand-in.
                    if (generation != _generation) {
                        Interlocked.CompareExchange(ref _interimReady, null, image);
                    }
                }
            } catch {
                // A stand-in is cosmetic twice over.
            }
        }, CancellationToken.None);
    }

    private void ShowInterim() {
        if (Source is not null || IconPath is not { Length: > 0 } path || RowIsFolder() is not { } folder) {
            return;
        }

        _interim = _interimReady;
        _interimIsFolder = folder;
        _interimFills = !folder && ImageFormats.IsImage(path);
        InvalidateMeasure();
        InvalidateVisual();
    }

    /// <summary>No stand-in any more, nor a wait for one: the picture replaces it at once (decision 2026-11-09, no transitions).</summary>
    private void DropInterim() {
        StopWaiting();
        _interimReady = null;
        if (_interim is null) {
            return;
        }

        _interim = null;
        InvalidateMeasure();
        InvalidateVisual();
    }

    /// <summary>The stand-in's place: the biggest square the space allows, a folder's no bigger than its picture; a tile's icon where nothing bounds it.</summary>
    private Size InterimSize(Size space) {
        double side = Math.Min(space.Width, space.Height);
        if (_interimIsFolder && _interim is not null) {
            side = Math.Min(side, Math.Min(_interim.Width, _interim.Height));
        }

        return double.IsInfinity(side) ? new Size(48, 48) : new Size(side, side);
    }

    /// <summary>
    /// The stand-in in this icon's box: a folder's silhouette as drawn; a
    /// file's plate - covering the place for a picture file, a page's shape
    /// for the rest - with its type icon in the middle at a list icon's size
    /// (Pending.dc.html of the fitting).
    /// </summary>
    private void DrawInterim(DrawingContext dc) {
        var box = new Rect(RenderSize);
        if (_interimIsFolder) {
            dc.DrawImage(_interim, box);

            return;
        }

        double side = Math.Min(box.Width, box.Height);
        var place = new Rect((box.Width - side) / 2, (box.Height - side) / 2, side, side);
        var plate = _interimFills
            ? place
            : new Rect(place.X + side * 0.10, place.Y + side * 0.06, side * 0.80, side * 0.88);
        double radius = side * (_interimFills ? 0.03 : 0.06);
        dc.DrawRoundedRectangle(Palette.PendingPlate.Current, null, plate, radius, radius);

        double icon = Math.Min(32, side * 0.6);
        dc.DrawImage(_interim, new Rect(place.X + (side - icon) / 2, place.Y + (side - icon) / 2, icon, icon));
    }

    private static async Task<BitmapImage?> LoadAsync(
        string path, IconSize size, FolderLook look, Func<bool> stillWanted, bool urgent, IconLoadTimes times, CancellationToken cancel) {
        await _gate.WaitAsync(urgent).ConfigureAwait(false);
        times.Admitted = Stopwatch.GetTimestamp();
        try {
            // Re-check after queueing: a fast scroll through a big folder can
            // park hundreds of loads on this gate, and by the time one gets
            // through, its container has usually been recycled onto another
            // file. Doing the shell call anyway would be pure waste — and
            // shell calls are the expensive part.
            if (!stillWanted()) {
                return null;
            }

            var image = await Task.Run(() => {
                using (PerfLog.Measure("bg.icon-load")) {
                    byte[]? bytes = ServiceLocator.Get<IIconProvider>().GetIcon(path, size, look, cancel);

                    // Decoded here rather than in IconConverter so the result
                    // lands in the decoded cache: the same tile scrolled back
                    // to then costs nothing at all, instead of a decode on the
                    // UI thread the first time it comes round again.
                    return bytes is null ? null : IconImageCache.Get(path, size, bytes);
                }
            }).ConfigureAwait(false);
            times.Done = Stopwatch.GetTimestamp();

            return image;
        } catch {
            // A missing icon is a cosmetic loss; never let it reach the
            // dispatcher's unhandled-exception handler.
            return null;
        } finally {
            _gate.Release();
        }
    }
}
