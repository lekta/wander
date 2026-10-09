using System.IO;
using System.Windows;
using System.Windows.Controls;
using Wander.App.Resources;
using Wander.App.Util;

namespace Wander.App;

/// <summary>
/// One line of text to type - a name, mostly. Built in code, like
/// <see cref="Dialogs.ChoiceDialog"/>: a label, a box and two buttons do
/// not earn a XAML file. Sized to its content, so a long label wraps
/// rather than pushing the buttons off the bottom.
/// </summary>
internal static class PromptDialog {
    private static readonly HashSet<char> _invalidFileChars = new(Path.GetInvalidFileNameChars());
    private static readonly string _invalidCharsDisplay = "\\ / : * ? \" < > |";


    public static string? Show(string title, string label, string initial, bool filenameMode = false) {
        var window = new Window {
            Title = title,
            Width = 380,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = Application.Current.MainWindow,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
        };
        App.ParkIfHeadless(window);
        InterfaceTheme.Attach(window);
        Palette.ChromeBackground.Paint(window, Control.BackgroundProperty);

        var stack = new StackPanel { Margin = new Thickness(16) };
        stack.Children.Add(new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) });

        var box = new TextBox { Text = initial, Padding = new Thickness(4, 2, 4, 2) };
        stack.Children.Add(box);

        var errorBlock = new TextBlock {
            FontSize = 11,
            Margin = new Thickness(0, 4, 0, 0),
            Visibility = Visibility.Collapsed,
        };
        Palette.TextError.Paint(errorBlock, TextBlock.ForegroundProperty);
        stack.Children.Add(errorBlock);

        var buttons = new StackPanel {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0),
        };
        var buttonStyle = (Style)Application.Current.FindResource("DialogButton");
        var ok = new Button { Content = Strings.ActionOk, Style = buttonStyle, IsDefault = true };
        var cancel = new Button { Content = Strings.ActionCancel, Style = buttonStyle, IsCancel = true, Margin = new Thickness(8, 0, 0, 0) };
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

        // Live-validate the text in case something slipped in (e.g. via auto-fill).
        box.TextChanged += (_, _) => UpdateState(box, errorBlock, ok);
        UpdateState(box, errorBlock, ok);
    }

    private static void UpdateState(TextBox box, TextBlock errorBlock, Button ok) {
        string text = box.Text;
        bool empty = string.IsNullOrWhiteSpace(text);
        bool hasInvalid = text.Any(_invalidFileChars.Contains);

        if (hasInvalid) {
            Palette.InputBorderError.Paint(box, Control.BorderBrushProperty);
            errorBlock.Text = Strings.InvalidFileNameChars + _invalidCharsDisplay;
            errorBlock.Visibility = Visibility.Visible;
            ok.IsEnabled = false;
            return;
        }

        // Back to the frame the box came with, not a system colour that
        // only looks like it: the two differ by a shade.
        box.ClearValue(Control.BorderBrushProperty);
        errorBlock.Visibility = Visibility.Collapsed;
        ok.IsEnabled = !empty;
    }

    private static void FlashError(TextBlock errorBlock) {
        errorBlock.Text = Strings.InvalidFileNameChars + _invalidCharsDisplay;
        errorBlock.Visibility = Visibility.Visible;
    }
}
