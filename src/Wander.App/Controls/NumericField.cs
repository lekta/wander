using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace Wander.App.Controls;

/// <summary>
/// Makes a bound <see cref="TextBox"/> behave like a number field: it stops
/// rewriting what is being typed, and it can be dragged or scrolled.
///
/// <para>
/// <b>The typing problem.</b> Every size in settings is clamped in its
/// setter — a 3-pixel cell would be a broken window, not a preference. With
/// a plain two-way binding on <c>PropertyChanged</c> the clamp lands back in
/// the box mid-keystroke: clear the field, press "1", and the box says "60".
/// The value the user was halfway through typing is gone, and there is no
/// way to reach 132 except by starting from the right end. So while the box
/// has the keyboard, whatever was typed stays on screen and only the source
/// sees the clamped number (which is what the live preview reads). The box
/// catches up with reality when the caret leaves.
/// </para>
///
/// <para>
/// <b>Which is why the binding must be <c>UpdateSourceTrigger=Explicit</c>.</b>
/// On <c>PropertyChanged</c> the push to the source and the clamp's echo back
/// to the box both happen inside WPF, and the echo arrives as an ordinary
/// <c>TextChanged</c> that is indistinguishable from a keystroke — by the
/// time anything can react, the typed text is already gone. Driving the
/// update from here means the echo happens inside a call we are holding, so
/// it can be ignored and the typed text put back immediately. The bindings
/// on the settings page say <c>Explicit</c> for that reason and no other;
/// without this behaviour attached they would never write anything at all.
/// </para>
///
/// <para>
/// <b>Dragging.</b> A pixel count is a quantity, and quantities are nicer to
/// pull than to type. The wheel over the field steps it; so does dragging
/// sideways, but only while the field does not hold the keyboard — once it
/// does, the mouse belongs to selecting text again. A click that does not
/// move stays a click and puts the caret in, so nothing is taken away from
/// anyone who just wants to type.
/// </para>
/// </summary>
public static class NumericField {
    /// <summary>Pixels of horizontal travel per step. Slow enough to land on a number.</summary>
    private const double DragPixelsPerStep = 4;

    /// <summary>Movement below this is a click, not a drag.</summary>
    private const double DragThreshold = 3;

    /// <summary>What Shift multiplies a step by, for crossing a wide range.</summary>
    private const int CoarseStep = 10;


    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
        "Enabled", typeof(bool), typeof(NumericField), new PropertyMetadata(false, OnEnabledChanged));

    public static void SetEnabled(DependencyObject element, bool value) {
        element.SetValue(EnabledProperty, value);
    }

    public static bool GetEnabled(DependencyObject element) {
        return (bool)element.GetValue(EnabledProperty);
    }


    /// <summary>
    /// Per-box state. An attached property rather than a table keyed by the
    /// box, so it is collected with the box; private, hence the field name
    /// the project's rules want rather than the usual "…Property".
    /// </summary>
    private static readonly DependencyProperty _stateProperty = DependencyProperty.RegisterAttached(
        "State", typeof(FieldState), typeof(NumericField), new PropertyMetadata(null));


    private static void OnEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
        if (d is not TextBox box) {
            return;
        }

        if (!(bool)e.NewValue) {
            Detach(box);

            return;
        }

        box.SetValue(_stateProperty, new FieldState());
        box.TextChanged += OnTextChanged;
        box.LostKeyboardFocus += OnLostKeyboardFocus;
        box.PreviewMouseWheel += OnPreviewMouseWheel;
        box.PreviewMouseLeftButtonDown += OnPreviewMouseLeftButtonDown;
        box.PreviewMouseMove += OnPreviewMouseMove;
        box.PreviewMouseLeftButtonUp += OnPreviewMouseLeftButtonUp;
        box.MouseEnter += OnMouseEnter;
    }

    private static void Detach(TextBox box) {
        box.TextChanged -= OnTextChanged;
        box.LostKeyboardFocus -= OnLostKeyboardFocus;
        box.PreviewMouseWheel -= OnPreviewMouseWheel;
        box.PreviewMouseLeftButtonDown -= OnPreviewMouseLeftButtonDown;
        box.PreviewMouseMove -= OnPreviewMouseMove;
        box.PreviewMouseLeftButtonUp -= OnPreviewMouseLeftButtonUp;
        box.MouseEnter -= OnMouseEnter;
        box.ClearValue(_stateProperty);
    }


    // --- Keeping the typed text ------------------------------------------

    private static void OnTextChanged(object sender, TextChangedEventArgs e) {
        if (sender is not TextBox box || StateOf(box) is not { Restoring: false } state) {
            return;
        }

        string typed = box.Text;
        int caret = box.CaretIndex;

        // The clamp's echo lands inside UpdateSource, with Restoring set, so
        // the TextChanged it raises is ignored and cannot overwrite what is
        // being typed.
        state.Restoring = true;
        try {
            BindingOperations.GetBindingExpression(box, TextBox.TextProperty)?.UpdateSource();
        } finally {
            state.Restoring = false;
        }

        if (!box.IsKeyboardFocusWithin) {
            // Changed by the wheel or a drag: the clamped value is the right
            // thing to show, and there is no caret to protect.
            return;
        }

        state.Typed = typed;
        state.Caret = caret;
        if (box.Text == typed) {
            return;
        }

        state.Restoring = true;
        try {
            box.Text = typed;
            box.CaretIndex = Math.Min(caret, typed.Length);
        } finally {
            state.Restoring = false;
        }
    }

    /// <summary>Editing over: show what the value actually became.</summary>
    private static void OnLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) {
        if (sender is not TextBox box || StateOf(box) is not { } state) {
            return;
        }

        state.Typed = null;
        // The pull cursor is only honest while a drag would actually pull.
        box.Cursor = box.IsMouseOver ? Cursors.SizeWE : null;
        state.Restoring = true;
        try {
            BindingOperations.GetBindingExpression(box, TextBox.TextProperty)?.UpdateTarget();
        } finally {
            state.Restoring = false;
        }
    }


    // --- Wheel and drag ---------------------------------------------------

    private static void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e) {
        if (sender is not TextBox box) {
            return;
        }

        Step(box, Math.Sign(e.Delta) * StepSize());
        e.Handled = true;
    }

    private static void OnMouseEnter(object sender, MouseEventArgs e) {
        if (sender is TextBox box) {
            // The cursor is the only hint that the field can be pulled, so it
            // only appears when pulling is actually what a drag would do.
            box.Cursor = box.IsKeyboardFocusWithin ? null : Cursors.SizeWE;
        }
    }

    private static void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
        if (sender is not TextBox box || StateOf(box) is not { } state) {
            return;
        }
        if (box.IsKeyboardFocusWithin) {
            // Focused: the mouse belongs to selecting text.
            box.Cursor = null;

            return;
        }

        state.DragOrigin = e.GetPosition(box).X;
        state.DragBase = ValueOf(box);
        state.Dragging = false;
    }

    private static void OnPreviewMouseMove(object sender, MouseEventArgs e) {
        if (sender is not TextBox box || StateOf(box) is not { DragOrigin: { } origin } state) {
            return;
        }
        if (e.LeftButton != MouseButtonState.Pressed) {
            return;
        }

        double travel = e.GetPosition(box).X - origin;
        if (!state.Dragging && Math.Abs(travel) < DragThreshold) {
            return;
        }

        if (!state.Dragging) {
            state.Dragging = true;
            box.CaptureMouse();
        }

        SetValue(box, state.DragBase + (int)(travel / DragPixelsPerStep) * StepSize());
        e.Handled = true;
    }

    private static void OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e) {
        if (sender is not TextBox box || StateOf(box) is not { } state) {
            return;
        }

        state.DragOrigin = null;
        if (!state.Dragging) {
            // Never moved: an ordinary click, and the caret goes where it was
            // clicked as it always would.
            return;
        }

        state.Dragging = false;
        box.ReleaseMouseCapture();
        e.Handled = true;
    }


    // --- Reading and writing the bound value ------------------------------

    private static int StepSize() {
        return Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? CoarseStep : 1;
    }

    private static void Step(TextBox box, int delta) {
        SetValue(box, ValueOf(box) + delta);
    }

    private static int ValueOf(TextBox box) {
        return int.TryParse(box.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out int value) ? value : 0;
    }

    /// <summary>
    /// Writes through the box, not around it: the binding does the clamping
    /// and the conversion, and the clamped result lands back in the text
    /// because nothing is being typed.
    /// </summary>
    private static void SetValue(TextBox box, int value) {
        // Writing Text is enough: TextChanged pushes it to the source, and
        // with no keyboard focus the clamped result is left on screen.
        box.Text = value.ToString(CultureInfo.CurrentCulture);
        BindingOperations.GetBindingExpression(box, TextBox.TextProperty)?.UpdateTarget();
    }

    private static FieldState? StateOf(TextBox box) {
        return box.GetValue(_stateProperty) as FieldState;
    }


    private sealed class FieldState {
        /// <summary>What the user has typed but the source has not confirmed. Null when not editing.</summary>
        public string? Typed { get; set; }

        public int Caret { get; set; }

        /// <summary>Guards the re-entrant TextChanged our own writes would cause.</summary>
        public bool Restoring { get; set; }

        public double? DragOrigin { get; set; }

        public int DragBase { get; set; }

        public bool Dragging { get; set; }
    }
}
