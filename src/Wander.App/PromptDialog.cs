using System.Windows;
using System.Windows.Controls;

namespace Wander.App;

internal static class PromptDialog {
    public static string? Show(string title, string label, string initial) {
        var window = new Window {
            Title = title,
            Width = 360,
            Height = 140,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = Application.Current.MainWindow,
            ResizeMode = ResizeMode.NoResize,
        };

        var stack = new StackPanel { Margin = new Thickness(12) };
        stack.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 6) });

        var box = new TextBox { Text = initial };
        stack.Children.Add(box);

        var buttons = new StackPanel {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0),
        };
        var ok = new Button { Content = "OK", Width = 70, IsDefault = true };
        var cancel = new Button { Content = "Cancel", Width = 70, IsCancel = true, Margin = new Thickness(6, 0, 0, 0) };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        stack.Children.Add(buttons);

        window.Content = stack;

        string? result = null;
        ok.Click += (_, _) => {
            result = box.Text;
            window.DialogResult = true;
        };

        box.Focus();
        box.SelectAll();

        return window.ShowDialog() == true ? result : null;
    }
}
