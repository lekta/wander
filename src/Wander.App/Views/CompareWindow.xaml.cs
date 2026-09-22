using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Wander.App.Conflict;
using Wander.App.Controllers;
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
    private readonly PreviewController _a;
    private readonly PreviewController _b;
    private readonly string _headerA;
    private readonly string _headerB;

    // A/B: both pictures on one place, one of them shown.
    private bool _showingB;


    private CompareWindow(FileSystemEntry a, FileSystemEntry b, ReviewHelpers? helpers) {
        InitializeComponent();

        _a = Controller(a, helpers);
        _b = Controller(b, helpers);
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

        // Two pictures can be laid on one place; anything else side by side only.
        bool pictures = IsPicture(a) && IsPicture(b);
        OverlayToggle.Visibility = pictures ? Visibility.Visible : Visibility.Collapsed;

        // A held zoom looks at the same place of both; a text scrolled on
        // one side scrolls the other.
        PaneA.ZoomMoved += (_, at) => PaneB.FollowZoom(at);
        PaneB.ZoomMoved += (_, at) => PaneA.FollowZoom(at);
        PaneA.TextScrolled += (_, at) => PaneB.FollowTextScroll(at);
        PaneB.TextScrolled += (_, at) => PaneA.FollowTextScroll(at);

        App.ParkIfHeadless(this);
        Closed += (_, _) => {
            _a.SetVisible(false);
            _b.SetVisible(false);
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


    private static PreviewController Controller(FileSystemEntry entry, ReviewHelpers? helpers) {
        var controller = new PreviewController(ServiceLocator.TryGet<IImageMetadataReader>(), null);
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
        bool overlay = OverlayToggle.IsChecked == true;
        Grid.SetColumnSpan(PaneA, overlay ? 2 : 1);
        Grid.SetColumn(PaneB, overlay ? 0 : 1);
        Grid.SetColumnSpan(PaneB, overlay ? 2 : 1);
        SwitchButton.Visibility = overlay ? Visibility.Visible : Visibility.Collapsed;
        HeaderA.Visibility = overlay ? Visibility.Collapsed : Visibility.Visible;
        Grid.SetColumn(HeaderBPanel, overlay ? 0 : 1);
        Grid.SetColumnSpan(HeaderBPanel, overlay ? 2 : 1);
        _showingB = false;
        ShowSides(overlay);
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
            // The window's helpers when there is a main window to take them
            // from: the switches are one set for every pane (PLAN Q5).
            var helpers = (Application.Current?.MainWindow?.DataContext as MainViewModel)?.Helpers;
            var window = new CompareWindow(left, right, helpers) { Owner = owner };
            window.Show();
        }
    }
}
