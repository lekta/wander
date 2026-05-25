using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Wander.App;

internal static class PromptDialog {
    private static readonly HashSet<char> _invalidFileChars = new(Path.GetInvalidFileNameChars());
    private static readonly string _invalidCharsDisplay = "\\ / : * ? \" < > |";


    public static string? Show(string title, string label, string initial, bool filenameMode = false) {
        var window = new Window {
            Title = title,
            Width = 380,
            Height = 160,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = Application.Current.MainWindow,
            ResizeMode = ResizeMode.NoResize,
        };

        var stack = new StackPanel { Margin = new Thickness(12) };
        stack.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 6) });

        var box = new TextBox { Text = initial };
        stack.Children.Add(box);

        var errorBlock = new TextBlock {
            Foreground = Brushes.IndianRed,
            FontSize = 11,
            Margin = new Thickness(0, 4, 0, 0),
            Visibility = Visibility.Collapsed,
        };
        stack.Children.Add(errorBlock);

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

        if (filenameMode) {
            WireFilenameValidation(box, errorBlock, ok);
        }

        string? result = null;
        ok.Click += (_, _) => {
            result = box.Text;
            window.DialogResult = true;
        };

        box.Focus();
        box.SelectAll();

        return window.ShowDialog() == true ? result : null;
    }


    // --- Filename-mode validation --------------------------------------

    private static void WireFilenameValidation(TextBox box, TextBlock errorBlock, Button ok) {
        // Block forbidden characters at input time (typing + paste).
        box.PreviewTextInput += (_, e) => {
            if (e.Text.Any(_invalidFileChars.Contains)) {
                e.Handled = true;
                FlashError(errorBlock);
            }
        };
        DataObject.AddPastingHandler(box, (_, e) => {
            if (e.SourceDataObject.GetData(DataFormats.UnicodeText) is string pasted
                && pasted.Any(_invalidFileChars.Contains)) {
                e.CancelCommand();
                FlashError(errorBlock);
            }
        });
        box.PreviewKeyDown += (_, e) => {
            // Disallow Tab/Enter producing weird whitespace via composition. Default
            // WPF already handles most; keep this hook in case we want more rules.
        };

        // Live-validate the text in case something slipped in (e.g. via auto-fill).
        box.TextChanged += (_, _) => UpdateState(box, errorBlock, ok);
        UpdateState(box, errorBlock, ok);
    }

    private static void UpdateState(TextBox box, TextBlock errorBlock, Button ok) {
        string text = box.Text;
        bool empty = string.IsNullOrWhiteSpace(text);
        bool hasInvalid = text.Any(_invalidFileChars.Contains);

        if (hasInvalid) {
            box.BorderBrush = Brushes.IndianRed;
            errorBlock.Text = "A file name can't contain any of these characters: " + _invalidCharsDisplay;
            errorBlock.Visibility = Visibility.Visible;
            ok.IsEnabled = false;
            return;
        }

        box.BorderBrush = SystemColors.ControlDarkBrush;
        errorBlock.Visibility = Visibility.Collapsed;
        ok.IsEnabled = !empty;
    }

    private static void FlashError(TextBlock errorBlock) {
        errorBlock.Text = "A file name can't contain any of these characters: " + _invalidCharsDisplay;
        errorBlock.Visibility = Visibility.Visible;
    }
}
