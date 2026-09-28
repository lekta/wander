using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Wander.App.Controls;

/// <summary>
/// A field that narrows a list or a search (2026-09-28): a prompt while it is
/// empty, a cross that empties it while it is not, and Esc that empties it
/// before the window sees the key.
///
/// <para>
/// Esc in a dialog is Cancel, and a filter typed and then Esc'd away must not
/// take the dialog's changes with it - Rider's settings work the same way.
/// An empty field lets Esc through, so the second one does what the window
/// does with it. A window that takes Esc for itself before its children do
/// (the search window closes on it) keeps doing so.
/// </para>
/// </summary>
public partial class FilterBox : UserControl {
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(FilterBox),
        new FrameworkPropertyMetadata(
            string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnTextChanged, CoerceText));

    public string Text {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public static readonly DependencyProperty PromptProperty = DependencyProperty.Register(
        nameof(Prompt), typeof(string), typeof(FilterBox), new PropertyMetadata(string.Empty));

    /// <summary>What the empty field says it is for.</summary>
    public string Prompt {
        get => (string)GetValue(PromptProperty);
        set => SetValue(PromptProperty, value);
    }


    public FilterBox() {
        InitializeComponent();
    }


    /// <summary>After every change of <see cref="Text"/>, typed or cleared.</summary>
    public event EventHandler? TextChanged;


    /// <summary>Puts the keyboard in the field with its text selected, ready to be typed over.</summary>
    public void FocusAndSelectAll() {
        Box.Focus();
        Box.SelectAll();
    }


    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
        var box = (FilterBox)d;
        box.TextChanged?.Invoke(box, EventArgs.Empty);
    }

    /// <summary>Never null, so "empty" is one value for the triggers that show the prompt and the cross.</summary>
    private static object CoerceText(DependencyObject d, object? value) {
        return value ?? string.Empty;
    }

    private void Box_PreviewKeyDown(object sender, KeyEventArgs e) {
        if (e.Key == Key.Escape && Text.Length > 0) {
            SetCurrentValue(TextProperty, string.Empty);
            e.Handled = true;
        }
    }

    private void Clear_Click(object sender, RoutedEventArgs e) {
        SetCurrentValue(TextProperty, string.Empty);
        Box.Focus();
    }
}
