using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Media;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Wander.App.Resources;
using Wander.App.Util;
using Wander.Core.Actions;

namespace Wander.App.Dialogs;

/// <summary>
/// The delete question (<see cref="IDialogs.ConfirmDelete"/>): what
/// <see cref="DeleteConfirmation"/> says, laid out, and the folders counted
/// while it is up - the count stops when the window closes, whatever the
/// answer. Modal; true when the user pressed the button that deletes.
/// </summary>
[SuppressMessage("Design", "CA1001",
    Justification = "The count's cancellation source is cancelled and disposed when the window closes; a window is not disposed.")]
public partial class DeleteConfirmWindow : Window {
    /// <summary>How long the mouse rests on a row before its picture comes up.</summary>
    private const int PreviewDelayMs = 400;

    /// <summary>The name column's bounds: narrower wastes nothing, wider is the window running off.</summary>
    private const double NameMin = 200;

    private const double NameMax = 360;

    /// <summary>The icon in front of a name and the gap after it.</summary>
    private const double IconAndGap = 16 + 6;

    /// <summary>What a tag adds around its word: padding, border, the gap before it.</summary>
    private const double TagFrame = 10 + 2 + 6;

    /// <summary>
    /// How long "delete for good" stays disabled once the bin has refused:
    /// the question comes up in the middle of an operation, maybe under keys
    /// meant for something else.
    /// </summary>
    private static readonly TimeSpan _armDelay = TimeSpan.FromMilliseconds(500);

    private readonly DeleteConfirmViewModel _vm;
    private readonly CancellationTokenSource _count = new();
    private readonly DispatcherTimer _preview = new() { Interval = TimeSpan.FromMilliseconds(PreviewDelayMs) };
    private FrameworkElement? _hovered;


    private DeleteConfirmWindow(DeleteConfirmation question) {
        InitializeComponent();

        _vm = new DeleteConfirmViewModel(question);
        _vm.NameWidth = MeasureNames();
        DataContext = _vm;

        App.ParkIfHeadless(this);
        InterfaceTheme.Attach(this);

        _preview.Tick += (_, _) => ShowPreview();
        Table.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler((_, _) => HidePreview()));
        Deactivated += (_, _) => HidePreview();
        Loaded += OnLoaded;
        Closed += (_, _) => {
            HidePreview();
            _count.Cancel();
            _count.Dispose();
        };

        if (question.Kind == DeleteQuestionKind.Refused) {
            ActionButton.IsEnabled = false;
            var arm = new DispatcherTimer { Interval = _armDelay };
            arm.Tick += (_, _) => {
                arm.Stop();
                ActionButton.IsEnabled = true;
            };
            // Counted from the moment the question is on screen, not from when it was built.
            ContentRendered += (_, _) => arm.Start();
            Closed += (_, _) => arm.Stop();
        }
    }


    public static bool Show(DeleteConfirmation question, Window? owner) {
        var window = new DeleteConfirmWindow(question) {
            Owner = owner,
            WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
        };

        return window.ShowDialog() == true;
    }


    private void OnLoaded(object sender, RoutedEventArgs e) {
        CancelButton.Focus();
        if (!App.Headless) {
            // What the message box this replaces sounded like.
            (_vm.IsError ? SystemSounds.Hand : SystemSounds.Exclamation).Play();
        }
        _ = _vm.Question.CountAsync(_vm.Changed, _count.Token);
    }

    private void OnAction(object sender, RoutedEventArgs e) {
        DialogResult = true;
    }

    /// <summary>The first picture waits for the mouse to rest; once one is up, the next row's comes at once.</summary>
    private void OnRowEnter(object sender, MouseEventArgs e) {
        _hovered = sender as FrameworkElement;
        if (Preview.IsOpen) {
            ShowPreview();

            return;
        }
        _preview.Stop();
        _preview.Start();
    }

    /// <summary>Onto the next row the picture stays for it to take over; off the table it goes.</summary>
    private void OnRowLeave(object sender, MouseEventArgs e) {
        if (!ReferenceEquals(sender, _hovered)) {
            return;
        }

        _hovered = null;
        if (!Table.IsMouseOver) {
            HidePreview();
        }
    }

    private void OnTableLeave(object sender, MouseEventArgs e) {
        _hovered = null;
        HidePreview();
    }

    private void ShowPreview() {
        _preview.Stop();
        if (_hovered is not { DataContext: DeleteRowItem item } row || !row.IsMouseOver) {
            return;
        }

        PreviewIcon.IconPath = item.FullPath;
        PreviewName.Text = item.Name;
        PreviewFolder.Text = Path.GetDirectoryName(item.FullPath) ?? "";
        Preview.VerticalOffset = row.TranslatePoint(new Point(0, 0), Root).Y;
        Preview.IsOpen = true;
    }

    private void HidePreview() {
        _preview.Stop();
        Preview.IsOpen = false;
    }

    /// <summary>
    /// The name column wide enough for the longest name with its icon, its
    /// indent and its tags, within <see cref="NameMin"/>..<see cref="NameMax"/>:
    /// a longer one ends in an ellipsis, and the picture card shows it whole.
    /// </summary>
    private double MeasureNames() {
        double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var face = new Typeface(FontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        double Width(string text, double size) {
            return new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection, face, size, Brushes.Black, pixelsPerDip)
                .WidthIncludingTrailingWhitespace;
        }

        double widest = 0;
        foreach (var item in _vm.Rows) {
            double width = Width(item.Name, FontSize) + IconAndGap + item.Indent.Left + item.Indent.Right;
            if (item.IsCompanion) {
                width += Width(Strings.DeleteTagCompanion, 11) + TagFrame;
            }
            if (item.IsReadOnly) {
                width += Width(Strings.DeleteTagReadOnly, 11) + TagFrame;
            }
            widest = Math.Max(widest, width);
        }

        return Math.Clamp(Math.Ceiling(widest), NameMin, NameMax);
    }
}
