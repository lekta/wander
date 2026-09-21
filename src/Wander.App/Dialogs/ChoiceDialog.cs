using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Wander.App.Resources;

namespace Wander.App.Dialogs;

/// <summary>
/// A message box whose buttons say what they do. Built in code, like
/// <see cref="PromptDialog"/>: one text and a row of buttons do not earn a
/// XAML file. Cancel is the default button and Esc, whatever the answers
/// are - Enter must never pick one of them by accident. A long text
/// scrolls rather than pushing the buttons off the screen.
/// </summary>
internal static class ChoiceDialog {
    /// <summary>How tall the text may grow before it scrolls.</summary>
    private const double MessageMaxHeight = 420;


    public static int Show(ChoiceRequest request, Window? owner) {
        var window = new Window {
            Title = request.Title,
            SizeToContent = SizeToContent.WidthAndHeight,
            MinWidth = 360,
            MaxWidth = 560,
            WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
            Owner = owner,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
        };
        App.ParkIfHeadless(window);

        var stack = new StackPanel { Margin = new Thickness(16) };
        stack.Children.Add(new ScrollViewer {
            Content = new TextBlock {
                Text = request.Message,
                TextWrapping = TextWrapping.Wrap,
            },
            MaxHeight = MessageMaxHeight,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Focusable = false,
            Margin = new Thickness(0, 0, 0, 16),
        });

        var buttons = new StackPanel {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        int result = -1;
        bool armed = request.ArmDelay is null;
        var choices = new List<Button>(request.Choices.Count);
        for (int i = 0; i < request.Choices.Count; i++) {
            int index = i;
            var choice = new Button {
                Content = request.Choices[i],
                Padding = new Thickness(12, 4, 12, 4),
                Margin = new Thickness(0, 0, 8, 0),
                IsEnabled = armed,
            };
            choice.Click += (_, _) => {
                result = index;
                window.DialogResult = true;
            };
            buttons.Children.Add(choice);
            choices.Add(choice);
        }
        var cancel = new Button {
            Content = request.CancelLabel ?? Strings.ActionCancel,
            Padding = new Thickness(12, 4, 12, 4),
            MinWidth = 80,
            IsCancel = true,
            IsDefault = true,
        };
        buttons.Children.Add(cancel);
        stack.Children.Add(buttons);

        window.Content = stack;
        window.Loaded += (_, _) => cancel.Focus();
        if (request.ArmDelay is { } delay) {
            // Counted from the moment the question is on screen, not from
            // when it was built.
            var timer = new DispatcherTimer { Interval = delay };
            timer.Tick += (_, _) => {
                timer.Stop();
                foreach (var choice in choices) {
                    choice.IsEnabled = true;
                }
            };
            window.ContentRendered += (_, _) => timer.Start();
            window.Closed += (_, _) => timer.Stop();
        }
        window.ShowDialog();

        return result;
    }
}
