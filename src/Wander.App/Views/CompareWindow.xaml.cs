using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Wander.App.Conflict;
using Wander.App.Controllers;
using Wander.App.Preview;
using Wander.App.Resources;
using Wander.App.ViewModels;
using Wander.Core;
using Wander.Core.FileSystem;
using Wander.Core.Icons;
using Wander.Core.Preview;

namespace Wander.App.Views;

/// <summary>
/// Two files side by side (PLAN Q5) - opened from a pair of the conflict
/// window. Each half is a <see cref="PreviewPane"/> with a controller of
/// its own; this window only ties them together.
/// </summary>
public partial class CompareWindow : Window {
    /// <summary>How long the window waits for its pictures' shapes before it opens with what is known.</summary>
    private const int ShapesWaitMs = 150;

    private readonly PreviewController _a;
    private readonly PreviewController _b;
    private readonly string _headerA;
    private readonly string _headerB;

    // Two pictures go one above the other when that shows them bigger;
    // anything else side by side. Null until the first arrangement.
    private readonly bool _pictures;
    private readonly PictureShape? _shapeA;
    private readonly PictureShape? _shapeB;
    private bool? _stacked;

    // A/B: both pictures on one place, one of them shown.
    private bool _showingB;


    /// <param name="found">The text a search inside files is looking for, when the list shows its results: both texts open on it (PLAN B6).</param>
    private CompareWindow(
        FileSystemEntry a, FileSystemEntry b, ReviewHelpers? helpers, string? found, PictureShape? shapeA, PictureShape? shapeB) {
        InitializeComponent();

        _a = Controller(a, helpers, found);
        _b = Controller(b, helpers, found);
        PaneA.DataContext = _a;
        PaneB.DataContext = _b;
        _headerA = Path.GetDirectoryName(a.FullPath) ?? a.FullPath;
        _headerB = Path.GetDirectoryName(b.FullPath) ?? b.FullPath;
        HeaderA.Text = _headerA;
        HeaderB.Text = _headerB;
        HeaderA.ToolTip = a.FullPath;
        HeaderB.ToolTip = b.FullPath;
        Title = string.Equals(a.Name, b.Name, StringComparison.OrdinalIgnoreCase)
            ? string.Format(Strings.CompareTitle, a.Name)
            : string.Format(Strings.CompareTitlePair, a.Name, b.Name);

        // Two pictures can be laid on one place, and one above the other;
        // anything else side by side only.
        _pictures = IsPicture(a) && IsPicture(b);
        _shapeA = shapeA;
        _shapeB = shapeB;
        OverlayToggle.Visibility = _pictures ? Visibility.Visible : Visibility.Collapsed;
        // Decided before the first layout, on the size the window opens at,
        // so the panes do not start the other way round.
        Arrange(Width, Height);

        // A held zoom looks at the same place of both - the right button
        // held too moves one alone, to line them up; a text scrolled on one
        // side scrolls the other.
        PreviewPane.Link(PaneA, PaneB);
        PaneA.TextScrolled += (_, at) => PaneB.FollowTextScroll(at);
        PaneB.TextScrolled += (_, at) => PaneA.FollowTextScroll(at);

        App.ParkIfHeadless(this);
        Closed += (_, _) => {
            _a.Detach();
            _b.Detach();
            PaneA.ReleaseWebView();
            PaneB.ReleaseWebView();
        };
    }


    protected override void OnPreviewKeyDown(KeyEventArgs e) {
        base.OnPreviewKeyDown(e);
        if (Keyboard.Modifiers != ModifierKeys.None) {
            return;
        }

        if (e.Key == Key.Space && OverlayToggle.IsChecked == true) {
            Switch();
            e.Handled = true;
        } else if (e.Key == Key.Escape) {
            Close();
            e.Handled = true;
        }
    }


    private static PreviewController Controller(FileSystemEntry entry, ReviewHelpers? helpers, string? found) {
        var controller = new PreviewController(ServiceLocator.TryGet<IImageMetadataReader>(), null) {
            FindTextFor = _ => found,
        };
        if (helpers is not null) {
            controller.SetHelpers(helpers);
        }
        controller.SetVisible(true);
        controller.SetSelection(new[] { entry });
        controller.SetPrimary(entry);

        return controller;
    }

    private static bool IsPicture(FileSystemEntry entry) {
        return PreviewRouter.Route(entry.FullPath) is PreviewRoute.Image;
    }

    /// <summary>
    /// A/B on or off. On: both panes in one place, the same size - so the
    /// two pictures lie on each other exactly - and one of them shown.
    /// </summary>
    private void Overlay_Changed(object sender, RoutedEventArgs e) {
        _showingB = false;
        Arrange();
    }

    private void Room_SizeChanged(object sender, SizeChangedEventArgs e) {
        Arrange();
    }

    private void Arrange() {
        Arrange(Room.ActualWidth, Room.ActualHeight - HeaderRowA.ActualHeight - HeaderRowB.ActualHeight);
    }

    /// <summary>
    /// Where the panes and their headers go: on each other for A/B; one
    /// above the other for two pictures that show bigger that way
    /// (SplitOrientation, 2026-09-23) - landscape frames in a window wider
    /// than tall, mostly; side by side otherwise, where two texts are read.
    /// With the window resized the split turns over only when the other way
    /// is clearly bigger.
    /// </summary>
    /// <param name="width">The room the two pictures share.</param>
    /// <param name="height">Same.</param>
    private void Arrange(double width, double height) {
        bool overlay = OverlayToggle.IsChecked == true;
        if (!overlay && _pictures) {
            _stacked = SplitOrientation.Stacked(width, height, _shapeA, _shapeB, _stacked);
        }
        bool stacked = !overlay && _stacked == true;
        bool across = overlay || stacked;

        Place(HeaderA, row: 0, column: 0, columns: stacked ? 2 : 1);
        Place(PaneA, row: 1, column: 0, rows: stacked ? 1 : 3, columns: across ? 2 : 1);
        Place(HeaderBPanel, row: stacked ? 2 : 0, column: across ? 0 : 1, columns: across ? 2 : 1);
        Place(PaneB, row: stacked ? 3 : 1, column: across ? 0 : 1, rows: stacked ? 1 : 3, columns: across ? 2 : 1);
        PaneB.BorderThickness = stacked ? new Thickness(0, 1, 0, 0) : new Thickness(1, 1, 0, 0);
        HeaderBPanel.Margin = stacked ? new Thickness(12, 6, 8, 4) : new Thickness(6, 6, 8, 4);
        SwitchButton.Visibility = overlay ? Visibility.Visible : Visibility.Collapsed;
        HeaderA.Visibility = overlay ? Visibility.Collapsed : Visibility.Visible;
        ShowSides(overlay);
    }

    private static void Place(UIElement element, int row, int column, int rows = 1, int columns = 1) {
        Grid.SetRow(element, row);
        Grid.SetRowSpan(element, rows);
        Grid.SetColumn(element, column);
        Grid.SetColumnSpan(element, columns);
    }

    private void Switch_Click(object sender, RoutedEventArgs e) {
        Switch();
    }

    private void Switch() {
        _showingB = !_showingB;
        ShowSides(overlay: true);
    }

    /// <summary>
    /// Which pane is seen. Hidden, not collapsed: the other keeps its size,
    /// its picture and its zoom, and the flick between them is instant.
    /// </summary>
    private void ShowSides(bool overlay) {
        PaneA.Visibility = !overlay || !_showingB ? Visibility.Visible : Visibility.Hidden;
        PaneB.Visibility = !overlay || _showingB ? Visibility.Visible : Visibility.Hidden;
        HeaderB.Text = !overlay ? _headerB
            : _showingB ? string.Format(Strings.CompareSideB, _headerB)
            : string.Format(Strings.CompareSideA, _headerA);
    }


    /// <summary>What the conflict window opens a pair with - see <see cref="IPairViewer"/>.</summary>
    public sealed class Viewer : IPairViewer {
        /// <summary>Opens the comparison of <paramref name="left"/> and <paramref name="right"/> over <paramref name="owner"/>.</summary>
        public void Show(FileSystemEntry left, FileSystemEntry right, Window owner) {
            _ = ShowAsync(left, right, owner);
        }


        /// <summary>
        /// The pictures' shapes first, off the UI thread and for a moment at
        /// most - which way the panes are laid out depends on them - then
        /// the window.
        /// </summary>
        private static async Task ShowAsync(FileSystemEntry left, FileSystemEntry right, Window owner) {
            var reader = ServiceLocator.TryGet<IImageMetadataReader>();
            var read = Task.Run(() => (ShapeOf(left, reader), ShapeOf(right, reader)));
            await Task.WhenAny(read, Task.Delay(ShapesWaitMs));
            var (a, b) = read.IsCompletedSuccessfully ? read.Result : (null, null);
            if (!owner.IsVisible) {
                return;
            }

            // The window's helpers when there is a main window to take them
            // from: the switches are one set for every pane (PLAN Q5). And
            // the text of a search inside files, when its results are what
            // was copied (PLAN B6).
            var main = Application.Current?.MainWindow?.DataContext as MainViewModel;
            var window = new CompareWindow(left, right, main?.Helpers, main?.FoundText, a, b) { Owner = owner };
            window.Show();
        }

        private static PictureShape? ShapeOf(FileSystemEntry entry, IImageMetadataReader? reader) {
            return IsPicture(entry) ? PictureLoader.ShapeOf(entry.FullPath, reader) : null;
        }
    }
}
