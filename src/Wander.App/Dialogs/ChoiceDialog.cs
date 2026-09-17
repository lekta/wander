using System.Windows;
using System.Windows.Controls;
using Wander.App.Resources;

namespace Wander.App.Dialogs;

/// <summary>
/// A message box whose buttons say what they do. Built in code, like
/// <see cref="PromptDialog"/>: one text and a row of buttons do not earn a
/// XAML file. Cancel is the default button and Esc, whatever the answers
/// are - Enter must never pick one of them by accident.
/// </summary>
internal static class ChoiceDialog {
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
        stack.Children.Add(new TextBlock {
            Text = request.Message,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 16),
        });

        var buttons = new StackPanel {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        int result = -1;
        for (int i = 0; i < request.Choices.Count; i++) {
            int index = i;
            var choice = new Button {
                Content = request.Choices[i],
                Padding = new Thickness(12, 4, 12, 4),
                Margin = new Thickness(0, 0, 8, 0),
            };
            choice.Click += (_, _) => {
                result = index;
                window.DialogResult = true;
            };
            buttons.Children.Add(choice);
        }
        var cancel = new Button {
            Content = Strings.ActionCancel,
            Padding = new Thickness(12, 4, 12, 4),
            MinWidth = 80,
            IsCancel = true,
            IsDefault = true,
        };
        buttons.Children.Add(cancel);
        stack.Children.Add(buttons);

        window.Content = stack;
        window.Loaded += (_, _) => cancel.Focus();
        window.ShowDialog();

        return result;
    }
}
