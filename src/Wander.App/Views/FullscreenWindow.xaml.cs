using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Wander.App.Controllers;
using Wander.App.Resources;
using Wander.App.ViewModels;
using Wander.Core.Companions;
using Wander.Core.FileSystem;
using Wander.Core.Preview;

namespace Wander.App.Views;

/// <summary>
/// Pictures on their own, full screen (PLAN Q5): one, walked with the arrow
/// keys, or two side by side (2026-09-24) - the one on the left stays while
/// Shift and the arrows walk the one on the right, and a plain Left or
/// Right keeps that side alone on the screen. Covers the monitor the main
/// window is on; the keyboard is its own while it is open. Each picture
/// carries its stars and the helpers' switches on its bar, and a digit
/// rates it - of two, the one under the mouse.
/// </summary>
public partial class FullscreenWindow : Window {
    private readonly MainViewModel _vm;
    private readonly FullscreenPlan _plan;

    // The picture on the left, or alone: its pane and viewer, and where it
    // stood in the rows walked when last seen there (PictureWalk.Step).
    // The two panes swap these roles when the right one is kept (KeepSide).
    private PreviewPane _mainPane;
    private PreviewController _mainViewer;
    private int _mainStood;

    // The one on the right while the screen is split. The pane is made on
    // the first split and kept: it is a whole preview pane's worth of
    // controls, and walking the right picture must not rebuild it.
    private PreviewPane? _sidePane;
    private PreviewController? _sideViewer;
    private FileSystemEntry? _side;
    private int _sideStood;
    private ZoomLink? _zoomLink;

    // The list changed: the rows on show are looked up again, once for a burst.
    private bool _rowsPending;
    private bool _closed;

    // Alt is held and the review helpers are off the pictures meanwhile.
    private bool _peeking;


    private FullscreenWindow(MainViewModel vm, FullscreenPlan plan) {
        InitializeComponent();

        _vm = vm;
        _plan = plan;
        Background = vm.Preview.ContentPalette.Background;
        _mainPane = PaneA;
        _mainViewer = NewViewer(_mainPane);
        Current = plan.Start;
        _mainStood = PictureWalk.IndexOf(Walked(), plan.Start.FullPath);
        ShowPicture(_mainViewer, plan.Start);
        _mainViewer.SetVisible(true);
        if (plan.Mode == FullscreenMode.Pair) {
            var second = plan.Pictures[1];
            OpenSide(second, PictureWalk.IndexOf(Walked(), second.FullPath));
        }
        UpdateTitle();
        vm.Entries.CollectionChanged += OnEntriesChanged;

        // Maximised on the monitor it was placed on: the owner's. Not in a
        // headless run - maximising would bring a parked window on screen.
        SourceInitialized += (_, _) => {
            if (!App.Headless) {
                WindowState = WindowState.Maximized;
            }
        };
        App.ParkIfHeadless(this);
        Closed += (_, _) => {
            _closed = true;
            vm.Entries.CollectionChanged -= OnEntriesChanged;
            StopPeeking();
            _mainViewer.Detach();
            _sideViewer?.Detach();
            _mainPane.ReleaseWebView();
            _sidePane?.ReleaseWebView();
        };
    }


    /// <summary>The picture on show - the left one of two; where the list is left when a single one closes.</summary>
    public FileSystemEntry Current { get; private set; }


    /// <summary>Opens <paramref name="plan"/> over the monitor <paramref name="owner"/> is on.</summary>
    public static FullscreenWindow Open(FullscreenPlan plan, MainViewModel vm, Window owner) {
        var window = new FullscreenWindow(vm, plan);
        window.PlaceOver(owner);
        window.Show();

        return window;
    }


    protected override void OnPreviewKeyDown(KeyEventArgs e) {
        base.OnPreviewKeyDown(e);
        // Alt held: the pictures without the helpers' marks for as long as
        // it is, as in the main window.
        if (e.Key == Key.System && e.SystemKey is Key.LeftAlt or Key.RightAlt) {
            if (!_peeking && _vm.Helpers.AnyOn) {
                _peeking = true;
                _vm.Helpers.SetPeek(true);
            }

            return;
        }
        if (TryRate(e.Key)) {
            e.Handled = true;

            return;
        }

        var modifiers = Keyboard.Modifiers;
        // Shift and an arrow: the picture on the right - brought up beside
        // the one on show, or walked on while the left one stays.
        if (modifiers == ModifierKeys.Shift && e.Key is Key.Left or Key.Up or Key.Right or Key.Down) {
            WalkSide(e.Key is Key.Right or Key.Down ? +1 : -1);
            e.Handled = true;

            return;
        }
        if (modifiers != ModifierKeys.None) {
            return;
        }

        // Split, Left and Right choose the side that stays on screen, and
        // the rest of the walking keys wait: the left picture stands still
        // while the two are compared.
        bool split = _side is not null;
        switch (e.Key) {
            case Key.Escape:
            case Key.Enter:
                Close();
                e.Handled = true;
                break;

            case Key.Left when split:
                CloseSide();
                e.Handled = true;
                break;

            case Key.Right when split:
                KeepSide();
                e.Handled = true;
                break;

            case Key.Right:
            case Key.Down:
            case Key.Space:
            case Key.PageDown:
                if (!split) {
                    Step(+1);
                }
                e.Handled = true;
                break;

            case Key.Left:
            case Key.Up:
            case Key.Back:
            case Key.PageUp:
                if (!split) {
                    Step(-1);
                }
                e.Handled = true;
                break;

            case Key.Home:
            case Key.End:
                if (!split) {
                    Step(e.Key == Key.Home ? PictureWalk.First : PictureWalk.Last);
                }
                e.Handled = true;
                break;
        }
    }


    protected override void OnPreviewKeyUp(KeyEventArgs e) {
        base.OnPreviewKeyUp(e);
        if (e.Key == Key.System && e.SystemKey is Key.LeftAlt or Key.RightAlt) {
            StopPeeking();
            // Handled, or letting go would put the window into menu mode:
            // the next key would go to a system menu nobody can see.
            e.Handled = true;
        }
    }


    /// <summary>Alt+Tab with Alt held: the key-up lands in another window, so the marks would stay off.</summary>
    protected override void OnDeactivated(EventArgs e) {
        base.OnDeactivated(e);
        StopPeeking();
    }


    /// <summary>Puts the window over the monitor <paramref name="owner"/> is on, before it is shown.</summary>
    private void PlaceOver(Window owner) {
        Owner = owner;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = owner.Left + 20;
        Top = owner.Top + 20;
    }

    /// <summary>A viewer for <paramref name="pane"/>: the window's helpers, stars that write to its own picture, the rows walked to decode ahead from.</summary>
    private PreviewController NewViewer(PreviewPane pane) {
        var viewer = _vm.NewPictureViewer(Walked);
        viewer.PropertyChanged += OnViewerChanged;
        pane.DataContext = viewer;
        // Nothing to show yet: see OnViewerChanged.
        pane.Opacity = 0;

        return viewer;
    }

    /// <summary>What the arrow keys walk: the list, or only the pictures selected.</summary>
    private IReadOnlyList<FileSystemEntry> Walked() {
        return _plan.Mode == FullscreenMode.Selection ? _plan.Pictures : _vm.Entries;
    }

    /// <summary>The row as the list has it now: a walked selection keeps the rows it was opened with, and a star written since made new ones.</summary>
    private FileSystemEntry Fresh(FileSystemEntry row) {
        return _vm.Ratings.FindInSource(row.FullPath) ?? row;
    }

    /// <summary>The picture on the left, or alone, to the next one the given way (<see cref="PictureWalk.Step"/>); at an end it stays.</summary>
    private void Step(int by) {
        var rows = Walked();
        int to = PictureWalk.Step(rows, Current.FullPath, _mainStood, by);
        if (to < 0) {
            return;
        }

        _mainStood = to;
        Current = Fresh(rows[to]);
        ShowPicture(_mainViewer, Current);
        UpdateTitle();
    }

    /// <summary>
    /// Shift and an arrow. One picture on show: the screen splits, and the
    /// next one the given way comes up on the right. Split: the right one
    /// walks on, past the left one. At an end nothing moves.
    /// </summary>
    private void WalkSide(int by) {
        var rows = Walked();
        int to = _side is { } side
            ? PictureWalk.Step(rows, side.FullPath, _sideStood, by, skip: Current.FullPath)
            : PictureWalk.Step(rows, Current.FullPath, _mainStood, by);
        if (to < 0) {
            return;
        }

        if (_side is null) {
            OpenSide(Fresh(rows[to]), to);
        } else {
            ShowSide(Fresh(rows[to]), to);
        }
    }

    /// <summary>Splits the screen: <paramref name="entry"/> on the right, the picture on show stays on the left.</summary>
    private void OpenSide(FileSystemEntry entry, int stood) {
        if (_sidePane is null || _sideViewer is null) {
            _sidePane = new PreviewPane { PictureMargin = new Thickness(0) };
            _sideViewer = NewViewer(_sidePane);
            Room.Children.Add(_sidePane);
            // A held zoom looks at the same place of both, while there are two.
            _zoomLink = PreviewPane.Link(_mainPane, _sidePane, () => _side is not null);
        }

        ShowSide(entry, stood);
        _sideViewer.SetVisible(true);
        Place();
    }

    private void ShowSide(FileSystemEntry entry, int stood) {
        _side = entry;
        _sideStood = stood;
        // Another pair: whatever lined the last one up was about that one.
        _zoomLink?.Reset();
        ShowPicture(_sideViewer!, entry);
        UpdateTitle();
    }

    /// <summary>Back to one picture, the left one: the right one goes, and its viewer lets go of its file.</summary>
    private void CloseSide() {
        _side = null;
        _zoomLink?.Reset();
        _sideViewer?.SetVisible(false);
        Place();
        UpdateTitle();
    }

    /// <summary>
    /// The right picture alone. The panes swap roles rather than pictures:
    /// the right one is on screen and decoded already, and loaded into the
    /// left pane instead it would come up only after the left picture had
    /// shown full screen for a moment.
    /// </summary>
    private void KeepSide() {
        if (_side is not { } side || _sidePane is null || _sideViewer is null) {
            return;
        }

        (_mainPane, _sidePane) = (_sidePane, _mainPane);
        (_mainViewer, _sideViewer) = (_sideViewer, _mainViewer);
        _mainStood = _sideStood;
        Current = side;
        CloseSide();
    }

    /// <summary>The left, or only, picture in the first column; the right one in the second, while there is one.</summary>
    private void Place() {
        bool split = _side is not null;
        Grid.SetColumn(_mainPane, 0);
        _mainPane.BorderThickness = new Thickness(0);
        _mainPane.Visibility = Visibility.Visible;
        if (_sidePane is not null) {
            Grid.SetColumn(_sidePane, 1);
            // The line between the two is the right pane's own border.
            _sidePane.BorderThickness = new Thickness(1, 0, 0, 0);
            _sidePane.Visibility = split ? Visibility.Visible : Visibility.Collapsed;
        }
        SecondColumn.Width = split ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
    }

    private static void ShowPicture(PreviewController viewer, FileSystemEntry entry) {
        viewer.SetSelection(new[] { entry });
        viewer.SetPrimary(entry);
    }

    private void UpdateTitle() {
        Title = _side is { } side ? string.Format(Strings.CompareTitlePair, Current.Name, side.Name) : Current.Name;
    }

    /// <summary>
    /// 0..5 set that many stars, Shift + 1..5 a colour label and Shift + 0
    /// takes it off - the gallery's keys, here for the picture on show; of
    /// two, for the one under the mouse.
    /// </summary>
    private bool TryRate(Key key) {
        var modifiers = Keyboard.Modifiers;
        bool colour = modifiers == ModifierKeys.Shift;
        if (modifiers != ModifierKeys.None && !colour) {
            return false;
        }

        int digit = key switch {
            >= Key.D0 and <= Key.D5 => key - Key.D0,
            >= Key.NumPad0 and <= Key.NumPad5 when !colour => key - Key.NumPad0,
            _ => -1,
        };
        if (digit < 0) {
            return false;
        }

        if (Rated() is { } picture) {
            _vm.RatePicture(picture, colour ? RatingField.ColorLabel : RatingField.Rank, digit);
        }

        return true;
    }

    /// <summary>What a digit rates: the picture on show; of two, the one the mouse is over, none when it is over neither.</summary>
    private FileSystemEntry? Rated() {
        if (_side is null) {
            return Current;
        }

        return _mainPane.IsMouseOver ? Current
            : _sidePane?.IsMouseOver == true ? _side
            : null;
    }

    /// <summary>
    /// A viewer's picture came, or went. A pane is see-through while its
    /// viewer has nothing to show: a fresh one's first decode would
    /// otherwise put "select a file" up for a moment first - unless the
    /// decode is slow enough for the spinner. And the RAW switch pressed on
    /// either picture: the other follows it, and so does the main pane - it
    /// is one mode.
    /// </summary>
    private void OnViewerChanged(object? sender, PropertyChangedEventArgs e) {
        if (sender is not PreviewController viewer) {
            return;
        }

        switch (e.PropertyName) {
            case nameof(PreviewController.Kind):
            case nameof(PreviewController.IsLoading):
                var pane = ReferenceEquals(viewer, _mainViewer) ? _mainPane : _sidePane;
                if (pane is not null) {
                    pane.Opacity = viewer.Kind != PreviewKind.None || viewer.IsLoading ? 1 : 0;
                }
                break;

            case nameof(PreviewController.ShowRawDecode):
                bool raw = viewer.ShowRawDecode;
                _mainViewer.ShowRawDecode = raw;
                if (_sideViewer is not null) {
                    _sideViewer.ShowRawDecode = raw;
                }
                _vm.Preview.ShowRawDecode = raw;
                break;
        }
    }

    private void OnEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e) {
        if (_rowsPending) {
            return;
        }

        _rowsPending = true;
        _ = Dispatcher.BeginInvoke(DispatcherPriority.Background, RefreshRows);
    }

    /// <summary>
    /// The pictures on show again as the list has them now. A star written
    /// from here comes back as a new row, and the stars on the picture's bar
    /// are read from it - the same file, so nothing is decoded again. A row
    /// the rating filter hid is found all the same.
    /// </summary>
    private void RefreshRows() {
        _rowsPending = false;
        if (_closed) {
            return;
        }

        if (_vm.Ratings.FindInSource(Current.FullPath) is { } main && !ReferenceEquals(main, Current)) {
            Current = main;
            ShowPicture(_mainViewer, main);
        }
        if (_side is { } side && _sideViewer is not null
            && _vm.Ratings.FindInSource(side.FullPath) is { } fresh && !ReferenceEquals(fresh, side)) {
            _side = fresh;
            ShowPicture(_sideViewer, fresh);
        }

        var rows = Walked();
        int at = PictureWalk.IndexOf(rows, Current.FullPath);
        if (at >= 0) {
            _mainStood = at;
        }
        if (_side is not null) {
            at = PictureWalk.IndexOf(rows, _side.FullPath);
            if (at >= 0) {
                _sideStood = at;
            }
        }
    }

    private void StopPeeking() {
        if (_peeking) {
            _peeking = false;
            _vm.Helpers.SetPeek(false);
        }
    }
}
