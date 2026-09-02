using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows;
using Wander.App.Resources;
using Wander.Core.Operations;

namespace Wander.App.Views;

/// <summary>
/// Modal progress window for batch file ops. Owns a <see cref="CancellationTokenSource"/>
/// the caller passes to the operation, watches an <see cref="OperationTracker"/> for
/// per-item progress, and auto-closes when the watched task completes.
///
/// <para>
/// Usage pattern (see <c>MainViewModel.RunWithProgressDialogAsync</c>):
/// the caller creates the dialog, fires the async work using
/// <see cref="Token"/>, attaches the resulting task via
/// <see cref="TrackTask"/>, and calls <see cref="Window.ShowDialog"/>.
/// ShowDialog blocks the calling continuation while the WPF dispatcher
/// keeps pumping — when the task finishes (or the user clicks Cancel),
/// the dialog closes and ShowDialog returns.
/// </para>
/// </summary>
public partial class ProgressDialog : Window {
    private readonly CancellationTokenSource _cts = new();
    private readonly OperationTracker _tracker;
    private bool _autoClosing;


    public ProgressDialog(string headline, OperationTracker tracker) {
        InitializeComponent();
        // Off the desktop in a harness run, like every window: centred on
        // an owner parked off-screen, this one came up at (0, 0) with the
        // focus on every paste.
        App.ParkIfHeadless(this);
        DialogTitle = headline;
        Headline = headline + "...";
        _tracker = tracker;
        _tracker.Changed += OnTrackerChanged;
        RefreshSnapshot();
    }


    public string DialogTitle { get; }
    public string Headline { get; }

    public CancellationToken Token => _cts.Token;


    /// <summary>
    /// Tell the dialog which task to follow. When the task completes (success,
    /// failure, or cancellation), the dialog closes itself on the UI thread.
    /// Safe to call before <c>ShowDialog</c> — completion that races the show
    /// is honoured the moment the dispatcher starts pumping.
    /// </summary>
    public void TrackTask(Task task) {
        _ = task.ContinueWith(_ => Dispatcher.BeginInvoke(() => {
            _autoClosing = true;
            if (IsVisible) {
                Close();
            }
        }));
    }


    // --- Progress display (bound by XAML) ------------------------------

    private string _currentPath = "";
    public string CurrentPath {
        get => _currentPath;
        private set { _currentPath = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentPath))); }
    }

    private double _percent;
    public double Percent {
        get => _percent;
        private set { _percent = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Percent))); }
    }

    private string _counterText = "";
    public string CounterText {
        get => _counterText;
        private set { _counterText = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CounterText))); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;


    // --- Tracker plumbing ---------------------------------------------

    private void OnTrackerChanged(object? sender, EventArgs e) {
        if (Dispatcher.CheckAccess()) {
            RefreshSnapshot();
        } else {
            Dispatcher.BeginInvoke(RefreshSnapshot);
        }
    }

    private void RefreshSnapshot() {
        var snapshot = _tracker.Snapshot();
        if (snapshot.Count == 0) {
            CurrentPath = "";
            Percent = 0;
            CounterText = "";
            return;
        }

        // For the base dialog we report on the first in-flight op; with one
        // batch in flight at a time (the common case) that's exactly right.
        // Nested ops + an op-picker can come later.
        var op = snapshot[0];
        CurrentPath = op.CurrentPath ?? "";
        Percent = op.Total > 0 ? (double)op.Completed * 100.0 / op.Total : 0;
        CounterText = $"{op.Completed} / {op.Total}";
    }


    // --- User intent --------------------------------------------------

    private void CancelButton_Click(object sender, RoutedEventArgs e) {
        CancelButton.IsEnabled = false;
        CancelButton.Content = Strings.ProgressCancelling;
        _cts.Cancel();
    }

    private void OnClosing(object? sender, CancelEventArgs e) {
        // X / Esc / Alt+F4 = cancel. If we're auto-closing because the task
        // finished, skip cancelling (it'd be a no-op but explicit).
        if (!_autoClosing && !_cts.IsCancellationRequested) {
            _cts.Cancel();
        }
        _tracker.Changed -= OnTrackerChanged;
    }
}
