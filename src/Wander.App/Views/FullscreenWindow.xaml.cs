using System.Windows;
using System.Windows.Input;
using Wander.App.Controllers;
using Wander.App.ViewModels;
using Wander.Core;
using Wander.Core.FileSystem;
using Wander.Core.Icons;
using Wander.Core.Preview;

namespace Wander.App.Views;

/// <summary>
/// The picture on its own, full screen (PLAN Q5). Covers the monitor the
/// main window is on; the keyboard is its own while it is open.
/// </summary>
public partial class FullscreenWindow : Window {
    private readonly PreviewController _controller;
    private readonly Func<IReadOnlyList<FileSystemEntry>> _listing;


    /// <param name="listing">The list's rows in their order - what the arrows walk.</param>
    /// <param name="helpers">The window's review helpers: the same switches as the pane's.</param>
    /// <param name="palette">The surround - the gallery's, which this was opened from.</param>
    public FullscreenWindow(
        FileSystemEntry start, Func<IReadOnlyList<FileSystemEntry>> listing, ReviewHelpers helpers, GalleryPalette palette) {
        InitializeComponent();

        _listing = listing;
        _controller = new PreviewController(ServiceLocator.TryGet<IImageMetadataReader>(), null) {
            ShowFooter = false,
            Listing = listing,
        };
        _controller.SetHelpers(helpers);
        _controller.SetPalette(palette);
        Background = palette.Background;
        Pane.DataContext = _controller;
        _controller.SetVisible(true);
        Current = start;
        ShowEntry(start);

        // Maximised on the monitor it was placed on: the owner's. Not in a
        // headless run - maximising would bring a parked window on screen.
        SourceInitialized += (_, _) => {
            if (!App.Headless) {
                WindowState = WindowState.Maximized;
            }
        };
        App.ParkIfHeadless(this);
        Closed += (_, _) => {
            _controller.SetVisible(false);
            Pane.ReleaseWebView();
        };
    }


    /// <summary>The picture on show - where the list is left when the window closes.</summary>
    public FileSystemEntry Current { get; private set; }


    /// <summary>Puts the window over the monitor <paramref name="owner"/> is on, before it is shown.</summary>
    public void PlaceOver(Window owner) {
        Owner = owner;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = owner.Left + 20;
        Top = owner.Top + 20;
    }


    protected override void OnPreviewKeyDown(KeyEventArgs e) {
        base.OnPreviewKeyDown(e);
        if (Keyboard.Modifiers != ModifierKeys.None) {
            return;
        }

        switch (e.Key) {
            case Key.Escape:
            case Key.Enter:
                Close();
                e.Handled = true;
                break;

            case Key.Right:
            case Key.Down:
            case Key.Space:
            case Key.PageDown:
                Step(+1);
                e.Handled = true;
                break;

            case Key.Left:
            case Key.Up:
            case Key.Back:
            case Key.PageUp:
                Step(-1);
                e.Handled = true;
                break;

            case Key.Home:
                Step(int.MinValue);
                e.Handled = true;
                break;

            case Key.End:
                Step(int.MaxValue);
                e.Handled = true;
                break;
        }
    }


    /// <summary>
    /// To the next picture of the list the given way, skipping what is not
    /// one; at an end it stays. <see cref="int.MinValue"/> and
    /// <see cref="int.MaxValue"/> go to the first and the last.
    /// </summary>
    private void Step(int by) {
        var rows = _listing();
        int at = -1;
        for (int i = 0; i < rows.Count; i++) {
            if (string.Equals(rows[i].FullPath, Current.FullPath, StringComparison.OrdinalIgnoreCase)) {
                at = i;
                break;
            }
        }

        int direction = by > 0 ? 1 : -1;
        int i0 = by switch {
            int.MinValue => 0,
            int.MaxValue => rows.Count - 1,
            _ => at + direction,
        };
        // Home and End look inwards from their end for the first picture.
        if (by is int.MinValue or int.MaxValue) {
            direction = -direction;
        }
        for (int i = i0; i >= 0 && i < rows.Count; i += direction) {
            if (IsPicture(rows[i])) {
                ShowEntry(rows[i]);

                return;
            }
        }
    }

    private void ShowEntry(FileSystemEntry entry) {
        Current = entry;
        Title = entry.Name;
        _controller.SetSelection(new[] { entry });
        _controller.SetPrimary(entry);
    }

    private static bool IsPicture(FileSystemEntry entry) {
        return entry.Kind == EntryKind.File
            && PreviewRouter.Route(entry.FullPath) is PreviewRoute.Image or PreviewRoute.Animation;
    }
}
