using System.Diagnostics.CodeAnalysis;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Wander.App.Preview;
using Wander.App.Resources;
using Wander.App.ViewModels;
using Wander.Core.FileSystem;
using Wander.Core.Icons;
using Wander.Core.Imaging;

namespace Wander.App.Controls;

/// <summary>
/// The review helpers over one gallery cell: the picture through a tone
/// curve with the peaking marks on it, drawn over the cell's thumbnail
/// (RAWHELPERS). Nothing at all while no helper that shows on a cell is on,
/// and nothing for a file that is not a photograph.
///
/// <para>
/// Only cells in the tree ask, and a cell leaves the tree when it scrolls
/// out of the gallery's virtualised panel - which is how "measure what is
/// on screen" is arranged without anyone keeping a list of what is on
/// screen. The same request carries the ask for the frame's sharpness
/// score (<see cref="ScoreWanted"/>), which the host answers by putting the
/// number on the row.
/// </para>
/// </summary>
[SuppressMessage("Design", "CA1001",
    Justification = "DropWork() cancels the work when the cell leaves or changes file; managed only, nothing to release - and WPF never disposes a control.")]
public sealed class ReviewThumb : Image {
    public static readonly DependencyProperty EntryProperty =
        DependencyProperty.Register(
            nameof(Entry), typeof(object), typeof(ReviewThumb),
            new PropertyMetadata(null, (d, _) => ((ReviewThumb)d).Request()));

    public static readonly DependencyProperty SideProperty =
        DependencyProperty.Register(
            nameof(Side), typeof(double), typeof(ReviewThumb),
            new PropertyMetadata(200.0, (d, _) => ((ReviewThumb)d).Request()));

    /// <summary>The switches every cell follows; raised again whenever one moves.</summary>
    private static event Action? _switched;

    private static ReviewHelpers? _helpers;

    private CancellationTokenSource? _work;
    private int _generation;
    private bool _detached;
    private bool _listening;


    public ReviewThumb() {
        Unloaded += (_, _) => {
            Listen(false);
            DropWork();
            _detached = true;
        };
        Loaded += (_, _) => {
            Listen(true);
            if (_detached) {
                Request();
            }
        };
    }


    /// <summary>A cell on screen wants its frame's sharpness score - see <see cref="ReviewHelpers.Sharpness"/>.</summary>
    public static event Action<FileSystemEntry>? ScoreWanted;


    /// <summary>The row this cell is about; <c>{Binding}</c> in the gallery template.</summary>
    public object? Entry {
        get => GetValue(EntryProperty);
        set => SetValue(EntryProperty, value);
    }

    /// <summary>How big the cell's picture is drawn, in layout units - what the thumbnail is made at.</summary>
    public double Side {
        get => (double)GetValue(SideProperty);
        set => SetValue(SideProperty, value);
    }


    /// <summary>The switches for every cell of the window. Called once, by the host.</summary>
    public static void Follow(ReviewHelpers helpers) {
        _helpers = helpers;
        helpers.PropertyChanged += (_, _) => _switched?.Invoke();
    }


    private void Listen(bool listen) {
        if (listen == _listening) {
            return;
        }

        _listening = listen;
        if (listen) {
            _switched += Request;
        } else {
            _switched -= Request;
        }
    }


    private void Request() {
        DropWork();
        int generation = _generation;
        Source = null;
        if (_helpers is null || Entry is not FileSystemEntry entry
            || entry.IsFolderLike || !ImageFormats.IsImage(entry.Name)) {
            return;
        }

        if (_helpers.Sharpness && !_helpers.Peek) {
            ScoreWanted?.Invoke(entry);
        }
        if (Ask() is not { } ask) {
            return;
        }

        _work = new CancellationTokenSource();
        _ = ShowAsync(entry, ask, Math.Max(32, (int)Side), generation, _work.Token);
    }


    /// <summary>
    /// The cell is on its way out, or on to another file: what was asked for
    /// is not wanted. Cancelled rather than dropped on the floor - a gallery
    /// scrolled through quickly would otherwise keep measuring frames that
    /// left the screen long ago.
    /// </summary>
    private void DropWork() {
        _generation++;
        _work?.Cancel();
        _work = null;
    }


    /// <summary>What the switches ask of a cell; null when they ask nothing that shows on one.</summary>
    private ReviewThumbs.Ask? Ask() {
        if (_helpers is not { } helpers || helpers.Peek || (!helpers.Peaking && !helpers.Shadows && !helpers.Highlights)) {
            return null;
        }

        return new ReviewThumbs.Ask(
            helpers.Peaking,
            helpers.Shadows ? ToneCurve.Shadows() : helpers.Highlights ? ToneCurve.Highlights() : null,
            helpers.Shadows ? "s" : helpers.Highlights ? "h" : "",
            Palette.ReviewPeaking is SolidColorBrush pink ? pink.Color : Colors.Magenta);
    }


    private async Task ShowAsync(
        FileSystemEntry entry, ReviewThumbs.Ask ask, int side, int generation, CancellationToken ct) {
        ImageSource? made;
        try {
            made = await ReviewThumbs.RenderAsync(
                FileStamp.Of(entry.ModifiedUtc, entry.Size), entry.FullPath, ask, side, ct);
        } catch (Exception) {
            // Cancelled, or an unreadable file: the cell is left as it was,
            // and the thumbnail under it is the whole answer.
            return;
        }

        if (generation == _generation) {
            Source = made;
        }
    }
}
