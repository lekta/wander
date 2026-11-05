using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Wander.App.Conflict;
using Wander.App.Controllers;
using Wander.App.Highlighting;
using Wander.App.Preview;
using Wander.App.Resources;
using Wander.App.Util;
using Wander.App.ViewModels;
using Wander.Core;
using Wander.Core.FileSystem;
using Wander.Core.Icons;
using Wander.Core.Logging;
using Wander.Core.Preview;
using Wander.Core.Shell;

namespace Wander.App.Views;

/// <summary>
/// Two files side by side - opened from a pair of the conflict
/// window, or from the menu on two files. Each half is a
/// <see cref="PreviewPane"/> with a controller of its own; this window only
/// ties them together. Two texts that differ are shown as their
/// differences over both halves (<see cref="ShowDifferencesAsync"/>).
/// </summary>
[SuppressMessage("Design", "CA1001",
    Justification = "Closing cancels it; not disposed, since the comparison it stops may still ask for the token, and a source with no timer holds nothing to release.")]
public partial class CompareWindow : Window {
    /// <summary>How long the window waits for its pictures' shapes before it opens with what is known.</summary>
    private const int ShapesWaitMs = 150;

    /// <summary>
    /// How long it waits for two texts' differences: git answers within this
    /// on ordinary files, and the window opens on them rather than side by
    /// side and turning over a moment later.
    /// </summary>
    private const int DiffWaitMs = 300;

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

    // The comparison of two texts stops with the window.
    private readonly CancellationTokenSource _closing;


    /// <param name="found">The text a search inside files is looking for, when the list shows its results: both texts open on it.</param>
    /// <param name="differences">The comparison of the two, already running; null for two pictures.</param>
    /// <param name="closing">Stops <paramref name="differences"/>; the window cancels it as it closes.</param>
    private CompareWindow(
        FileSystemEntry a, FileSystemEntry b, ReviewHelpers? helpers, string? found, PictureShape? shapeA, PictureShape? shapeB,
        Task<FileDiffResult>? differences, CancellationTokenSource closing) {
        InitializeComponent();
        _closing = closing;

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

        // Anything but two pictures may be two texts: they open as their
        // differences - at once when those came in time, else when they do.
        if (differences is not null) {
            _ = ShowDifferencesAsync(differences);
        }

        App.ParkIfHeadless(this);
        InterfaceTheme.Attach(this);
        // The rows of the differences take the new theme's colours.
        InterfaceTheme.Changed += DiffView.Repaint;
        Closed += (_, _) => {
            InterfaceTheme.Changed -= DiffView.Repaint;
            _closing.Cancel();
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

    private void Diff_Changed(object sender, RoutedEventArgs e) {
        Arrange();
    }

    /// <summary>
    /// The two files compared (<see cref="FileDiff"/>) and logged, off the
    /// UI thread.
    /// </summary>
    private static async Task<FileDiffResult> CompareAsync(string left, string right, CancellationToken ct) {
        var diff = new FileDiff(ServiceLocator.Get<IFileSystem>(), ServiceLocator.TryGet<IDiffTool>());
        var watch = Stopwatch.StartNew();
        var result = await diff.CompareAsync(left, right, ct);
        if (result.Verdict == FileDiffVerdict.Failed) {
            Log.Warn($"Compare: failed in {watch.ElapsedMilliseconds} ms: {result.Error}");
        } else {
            Log.Info($"Compare: {result.Verdict} in {watch.ElapsedMilliseconds} ms{(result.Cut ? ", cut" : "")}");
        }

        return result;
    }

    /// <summary>
    /// What the comparison found: differences open over both panes, toggle
    /// on; any other answer worth saying is the note, and the panes stay.
    /// </summary>
    private async Task ShowDifferencesAsync(Task<FileDiffResult> differences) {
        FileDiffResult result;
        try {
            result = await differences;
        } catch (OperationCanceledException) {
            return;
        }

        if (NoteOf(result) is { } note) {
            DiffNote.Text = note;
            DiffNote.Visibility = Visibility.Visible;
        }
        if (result.Verdict != FileDiffVerdict.Different) {
            return;
        }

        DiffView.Text = result.Text;
        DiffBackground.Show(DiffView, on: true);
        DiffToggle.Visibility = Visibility.Visible;
        DiffToggle.IsChecked = true;
    }

    private static string? NoteOf(FileDiffResult result) {
        return result.Verdict switch {
            FileDiffVerdict.Different => result.Cut ? Strings.CompareCut : null,
            FileDiffVerdict.Identical => Strings.CompareIdentical,
            FileDiffVerdict.LineEndingsOnly => Strings.CompareLineEndings,
            FileDiffVerdict.NoTool => Strings.CompareNoGit,
            FileDiffVerdict.TooLarge => Strings.CompareTooLarge,
            FileDiffVerdict.Failed => Strings.CompareFailed,
            _ => null,
        };
    }

    /// <summary>
    /// Which pane is seen. Hidden, not collapsed: the other keeps its size,
    /// its picture and its zoom, and the flick between them is instant.
    /// The differences of two texts cover both.
    /// </summary>
    private void ShowSides(bool overlay) {
        bool diff = DiffToggle.IsChecked == true;
        DiffView.Visibility = diff ? Visibility.Visible : Visibility.Collapsed;
        PaneA.Visibility = !diff && (!overlay || !_showingB) ? Visibility.Visible : Visibility.Hidden;
        PaneB.Visibility = !diff && (!overlay || _showingB) ? Visibility.Visible : Visibility.Hidden;
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
        /// the window. Anything else may be two texts: their differences are
        /// asked for here, and waited for a moment too - of two files on
        /// disk only: git cannot read an archive entry or a file on a phone,
        /// which the conflict window of an extraction or an import opens.
        /// </summary>
        private static async Task ShowAsync(FileSystemEntry left, FileSystemEntry right, Window owner) {
            var reader = ServiceLocator.TryGet<IImageMetadataReader>();
            var read = Task.Run(() => (ShapeOf(left, reader), ShapeOf(right, reader)));
            var closing = new CancellationTokenSource();
            var differences = (IsPicture(left) && IsPicture(right)) || !OnDisk(left) || !OnDisk(right)
                ? null
                : CompareAsync(left.FullPath, right.FullPath, closing.Token);
            await Task.WhenAny(differences ?? (Task)read, Task.Delay(differences is null ? ShapesWaitMs : DiffWaitMs));
            var (a, b) = read.IsCompletedSuccessfully ? read.Result : (null, null);
            if (!owner.IsVisible) {
                closing.Cancel();

                return;
            }

            // The window's helpers when there is a main window to take them
            // from: the switches are one set for every pane. And
            // the text of a search inside files, when its results are what
            // was copied.
            var main = Application.Current?.MainWindow?.DataContext as MainViewModel;
            var window = new CompareWindow(left, right, main?.Helpers, main?.FoundText, a, b, differences, closing) {
                Owner = owner,
            };
            window.Show();
        }

        private static PictureShape? ShapeOf(FileSystemEntry entry, IImageMetadataReader? reader) {
            return IsPicture(entry) ? PictureLoader.ShapeOf(entry.FullPath, reader) : null;
        }

        private static bool OnDisk(FileSystemEntry entry) {
            return !DevicePath.Is(entry.FullPath) && !Archives.Inside(entry.FullPath);
        }
    }
}
