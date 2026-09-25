using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Windows;
using Wander.App.Dialogs;
using Wander.App.Resources;
using Wander.App.ViewModels;
using Wander.Core.Operations;

namespace Wander.App.Views;

/// <summary>
/// The window one batch file operation runs in. Owns a
/// <see cref="CancellationTokenSource"/> the caller passes to the operation,
/// follows that operation in the <see cref="OperationTracker"/>, and is
/// closed by the caller (<see cref="Finish"/>) the moment the work is over.
///
/// <para>
/// Usage pattern (see <c>MainViewModel.RunWithProgressDialogAsync</c>): the
/// caller creates the window, fires the async work using <see cref="Token"/>,
/// calls <see cref="Window.Show"/>, awaits its own task and calls
/// <see cref="Finish"/> - on the UI thread, before doing anything else
/// with the outcome. The window is <em>not</em> modal - the list stays live
/// underneath. It used to close itself from a continuation of the task
/// instead, and that close was a separate dispatcher operation, queued
/// after the caller's own continuation: a question the caller asked about
/// the outcome ("the file is in use, try again?") went up while this
/// window was still the active one, took it as its owner, and was
/// destroyed with it a moment later - with the application left
/// disabled behind a modal loop nobody could see (2026-09-17).
/// </para>
///
/// <para>
/// Which operation is mine: the one registered under this window's
/// <see cref="Token"/>. The handle is created several layers down, in Core,
/// and the token is the one thing the window and the work already share -
/// an id watermark was tried first, and an extraction that registers only
/// after its conflict dialog let a second operation slip in under it. One
/// window, one operation - two copies at once get two windows.
/// </para>
///
/// <para>
/// It cannot be closed while the work runs, only minimised (into the
/// status-bar panel) or cancelled - see the XAML.
/// </para>
/// </summary>
[SuppressMessage("Design", "CA1001",
    Justification = "Its token goes to the operation (Token), which may run on after the dialog closes: cancelling is the dialog's part, disposing would pull the source from under the operation.")]
public partial class ProgressDialog : Window, INotifyPropertyChanged, ITransientWindow {
    /// <summary>Every window from construction to close - what <see cref="CancelAll"/> reaches.</summary>
    private static readonly List<ProgressDialog> _live = new();

    private readonly CancellationTokenSource _cts = new();
    private readonly OperationTracker _tracker;

    private OperationViewModel? _operation;
    private bool _finished;


    public ProgressDialog(string headline, OperationTracker tracker) {
        InitializeComponent();
        // Off the desktop in a harness run, like every window: centred on an
        // owner parked off-screen, this one came up at (0, 0) with the focus
        // on every paste.
        App.ParkIfHeadless(this);
        DialogTitle = headline;
        Headline = headline + "...";
        _tracker = tracker;
        DataContext = this;
        _tracker.Changed += OnTrackerChanged;
        Closed += OnClosed;
        lock (_live) {
            _live.Add(this);
        }
        RefreshSnapshot();
    }


    public event PropertyChangedEventHandler? PropertyChanged;


    public string DialogTitle { get; }

    public string Headline { get; }

    public CancellationToken Token => _cts.Token;

    /// <summary>The tracker id of the operation this window shows; 0 until it appears.</summary>
    public long OperationId => _operation?.Id ?? 0;

    /// <summary>The numbers on screen. Null for the moment before the operation registers itself.</summary>
    public OperationViewModel? Operation {
        get => _operation;
        private set {
            _operation = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Operation)));
        }
    }


    /// <summary>
    /// The work is over (success, failure, or cancellation): the window
    /// goes, synchronously, so that by the time the caller looks at the
    /// outcome this window is neither open nor active. UI thread only.
    /// </summary>
    public void Finish() {
        _finished = true;
        Close();
    }

    /// <summary>Back from the status bar, where "Свернуть" put it.</summary>
    public void Restore() {
        if (_finished) {
            return;
        }

        Show();
        if (WindowState == WindowState.Minimized) {
            WindowState = WindowState.Normal;
        }
        Activate();
    }

    /// <summary>Stop the operation - the status-bar panel's Cancel comes here too.</summary>
    public void RequestCancel() {
        if (_cts.IsCancellationRequested) {
            return;
        }

        CancelButton.IsEnabled = false;
        CancelButton.Content = Strings.ProgressCancelling;
        _cts.Cancel();
    }

    /// <summary>
    /// Stops every operation that has a window, from any thread, without
    /// touching a control: the crash handler calls this, and the dispatcher
    /// may be what died. The buttons keep their caption - nobody is looking
    /// any more. The UI thread's way is <see cref="RequestCancel"/>.
    /// </summary>
    public static void CancelAll() {
        ProgressDialog[] windows;
        lock (_live) {
            windows = _live.ToArray();
        }
        foreach (var window in windows) {
            window._cts.Cancel();
        }
    }


    // --- Tracker plumbing ---------------------------------------------

    private void OnTrackerChanged(object? sender, EventArgs e) {
        if (Dispatcher.CheckAccess()) {
            RefreshSnapshot();
        } else {
            Dispatcher.BeginInvoke(RefreshSnapshot);
        }
    }

    /// <summary>
    /// Finds this window's operation and hands it the fresh numbers. An
    /// operation that has gone leaves the last state on screen: the window
    /// is closing anyway, and blanking it first would flash.
    /// </summary>
    private void RefreshSnapshot() {
        var snapshot = _tracker.Snapshot();
        OperationSnapshot? mine = null;
        foreach (var candidate in snapshot) {
            if (candidate.Token != Token) {
                continue;
            }
            if (_operation is null || candidate.Id == _operation.Id) {
                mine = candidate;
                break;
            }
        }

        if (mine is null) {
            return;
        }

        if (_operation is null || _operation.Id != mine.Id) {
            Operation = new OperationViewModel(mine.Id, cancel: _ => RequestCancel());
        }
        _operation!.Update(mine, DateTime.UtcNow);
    }


    // --- User intent --------------------------------------------------

    private void MinimizeButton_Click(object sender, RoutedEventArgs e) {
        Hide();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) {
        RequestCancel();
    }

    /// <summary>
    /// While the work runs there is no closing this window: Alt+F4, the
    /// system menu's Close and Escape all mean "Свернуть" - the title bar
    /// has no X (see the XAML). Once the task has finished, the close is
    /// ours and goes through.
    /// </summary>
    private void OnClosing(object? sender, CancelEventArgs e) {
        if (!_finished) {
            e.Cancel = true;
            Hide();
        }
    }

    /// <summary>
    /// The window really is going away. If the work has not finished, this
    /// is the application shutting down - WPF closes owned windows with the
    /// cancel above ignored - and the operation has to be told, rather than
    /// have the process pulled out from under it mid-write.
    /// </summary>
    private void OnClosed(object? sender, EventArgs e) {
        _tracker.Changed -= OnTrackerChanged;
        lock (_live) {
            _live.Remove(this);
        }
        if (!_finished) {
            _cts.Cancel();
        }
    }
}
