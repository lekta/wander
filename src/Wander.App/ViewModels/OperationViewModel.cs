using System.IO;
using System.Windows.Input;
using Wander.App.Resources;
using Wander.App.Util;
using Wander.Core.FileSystem;
using Wander.Core.Operations;

namespace Wander.App.ViewModels;

/// <summary>
/// One running file operation, as the operation window and the status-bar
/// panel read it: the verb in Russian, the file being worked on, the two
/// counts, the speed and what is left.
///
/// <para>
/// Updated in place rather than rebuilt on every tick. The tracker fires ten
/// times a second and the panel carries buttons - a collection replaced
/// under a pressed button loses the press, and the speed is an average over
/// a window that a rebuilt object would forget every time.
/// </para>
///
/// <para>
/// Two of these can look at the same operation at once: the window that
/// started it and the row in the status-bar panel. They keep their own
/// running averages, which is why the rate lives here and not in the
/// tracker.
/// </para>
///
/// <para>
/// A finished operation is a row of its own kind, made from its
/// <see cref="OperationRecord"/>: how it ended, what went wrong, and the two
/// things to do about it - try again or clear it away.
/// </para>
/// </summary>
public sealed class OperationViewModel : ObservableObject {
    /// <summary>How many problems the row names before it counts the rest.</summary>
    private const int ProblemLinesShown = 10;

    private readonly TransferRate _rate = new();

    private string _verb = "";
    private string _currentFile = "";
    private string _itemsText = "";
    private string _bytesText = "";
    private string _speedText = "";
    private string _remainingText = "";
    private double _percent;
    private bool _hasBytes;
    private bool _weighing;
    private bool _cancelling;
    private IReadOnlyList<string> _problemLines = Array.Empty<string>();
    private string? _ejectRoot;


    /// <param name="show">Brings the operation's window back; null where there is nothing to bring back.</param>
    /// <param name="cancel">Asks the operation to stop.</param>
    public OperationViewModel(long id, Action<long>? show = null, Action<long>? cancel = null) {
        Id = id;
        ShowCommand = new RelayCommand(() => show?.Invoke(Id), () => show is not null);
        CancelCommand = new RelayCommand(
            () => {
                Cancelling = true;
                cancel?.Invoke(Id);
            },
            () => cancel is not null && !Cancelling);
        RetryCommand = new RelayCommand(() => { }, () => false);
        DismissCommand = new RelayCommand(() => { }, () => false);
        EjectCommand = new RelayCommand(() => { }, () => false);
    }

    /// <summary>The row of a finished operation.</summary>
    /// <param name="canRetry">There is something to try again - the caller knows what a retry can take.</param>
    /// <param name="retry">Tries again what did not get there.</param>
    /// <param name="dismiss">Clears the row away.</param>
    /// <param name="eject">Safely removes the drive at the root it is given (<see cref="ShowEjectable"/>).</param>
    public OperationViewModel(OperationRecord record, bool canRetry, Action<long> retry, Action<long> dismiss, Action<long, string> eject) {
        Id = record.Id;
        Record = record;
        ShowCommand = new RelayCommand(() => { }, () => false);
        CancelCommand = new RelayCommand(() => { }, () => false);
        RetryCommand = new RelayCommand(() => retry(Id), () => canRetry);
        DismissCommand = new RelayCommand(() => dismiss(Id));
        EjectCommand = new RelayCommand(() => eject(Id, _ejectRoot!), () => _ejectRoot is not null);
        Verb = Strings.Get(record.Verb);
        OutcomeText = OutcomeOf(record);
    }


    /// <summary>Which operation in the tracker this is a view of.</summary>
    public long Id { get; }

    public ICommand ShowCommand { get; }

    public ICommand CancelCommand { get; }

    public ICommand RetryCommand { get; }

    public ICommand DismissCommand { get; }

    /// <summary>"Извлечь E:" - a removable drive the operation touched, while it is there.</summary>
    public ICommand EjectCommand { get; }

    public bool CanEject => _ejectRoot is not null;

    /// <summary>"Извлечь E:", or empty when there is nothing to eject.</summary>
    public string EjectText => _ejectRoot is null ? "" : string.Format(Strings.ActionEjectDrive, _ejectRoot.TrimEnd(Path.DirectorySeparatorChar));

    /// <summary>What the finished operation left behind; null while it runs.</summary>
    public OperationRecord? Record { get; }

    public bool IsFinished => Record is not null;

    public OperationOutcome Outcome => Record?.Outcome ?? OperationOutcome.Done;

    /// <summary>Segoe MDL2: a tick, a warning, an error, a cross - the outcome at a glance.</summary>
    public string OutcomeGlyph => Outcome switch {
        OperationOutcome.Problems => "",
        OperationOutcome.Failed => "",
        OperationOutcome.Cancelled => "",
        _ => "",
    };

    /// <summary>"готово 12", "готово 10, с ошибкой 1, пропущено 2".</summary>
    public string OutcomeText { get; } = "";

    /// <summary>
    /// "IMG_0042.CR3 - no room on E:" for each problem, up to ten, then a
    /// count of the rest. Filled in after the row appears: naming who holds
    /// a file asks Restart Manager, which is not for the UI thread.
    /// </summary>
    public IReadOnlyList<string> ProblemLines {
        get => _problemLines;
        private set {
            if (SetField(ref _problemLines, value)) {
                Raise(nameof(HasProblemLines));
            }
        }
    }

    public bool HasProblemLines => _problemLines.Count > 0;

    /// <summary>"Копирование", "В корзину" - the tracker's key, translated.</summary>
    public string Verb {
        get => _verb;
        private set => SetField(ref _verb, value);
    }

    /// <summary>The name of the file in flight, without its folder.</summary>
    public string CurrentFile {
        get => _currentFile;
        private set => SetField(ref _currentFile, value);
    }

    /// <summary>"Файлов: 3 из 12".</summary>
    public string ItemsText {
        get => _itemsText;
        private set => SetField(ref _itemsText, value);
    }

    /// <summary>"120.4 MB из 4.20 GB", or empty when the operation moves no bytes.</summary>
    public string BytesText {
        get => _bytesText;
        private set => SetField(ref _bytesText, value);
    }

    /// <summary>"12.4 MB/с", or empty until there is enough to average.</summary>
    public string SpeedText {
        get => _speedText;
        private set => SetField(ref _speedText, value);
    }

    /// <summary>"осталось ~ 1 мин 20 с", or empty when it cannot be told.</summary>
    public string RemainingText {
        get => _remainingText;
        private set => SetField(ref _remainingText, value);
    }

    /// <summary>0..100, by bytes where there are bytes and by items where there are not.</summary>
    public double Percent {
        get => _percent;
        private set {
            if (SetField(ref _percent, value)) {
                Raise(nameof(PercentText));
            }
        }
    }

    /// <summary>Empty while the sources are weighed: <see cref="ItemsText"/> says "counting" then.</summary>
    public string PercentText => _weighing ? "" : string.Format(Strings.OperationPercent, (int)Math.Round(Percent));

    /// <summary>False for a delete, and for an operation whose sources could not be weighed.</summary>
    public bool HasBytes {
        get => _hasBytes;
        private set => SetField(ref _hasBytes, value);
    }

    /// <summary>Cancel has been asked for and the operation is winding down.</summary>
    public bool Cancelling {
        get => _cancelling;
        private set => SetField(ref _cancelling, value);
    }

    /// <summary>One line for a narrow place: the verb and the percentage - or, before there are numbers, the verb and "counting".</summary>
    public string Summary => _weighing
        ? string.Format(Strings.OperationWeighing, Verb)
        : string.Format(Strings.OperationOne, Verb, (int)Math.Round(Percent));


    /// <summary>Takes in a fresh snapshot. <paramref name="nowUtc"/> feeds the speed average.</summary>
    public void Update(OperationSnapshot snapshot, DateTime nowUtc) {
        Verb = Strings.Get(snapshot.Verb);
        CurrentFile = string.IsNullOrEmpty(snapshot.CurrentPath) ? "" : Path.GetFileName(snapshot.CurrentPath);
        _weighing = snapshot.IsWeighing;
        ItemsText = _weighing
            ? Strings.ProgressWeighing
            : string.Format(Strings.OperationItems, snapshot.Completed, snapshot.Total);
        if (snapshot.Attempt > 1) {
            ItemsText += ", " + string.Format(Strings.OperationRetrying, snapshot.Attempt, RetryPolicy.Default.Attempts);
        }
        Percent = snapshot.Percent;
        Raise(nameof(PercentText));
        Raise(nameof(Summary));

        // Work units are not bytes and must never be written as megabytes;
        // the percentage above is all an extraction can honestly show.
        HasBytes = snapshot.HasBytes && !snapshot.BytesAreWork;
        if (!HasBytes || _weighing) {
            BytesText = "";
            SpeedText = "";
            RemainingText = "";

            return;
        }

        BytesText = string.Format(
            Strings.OperationBytes,
            SizeFormatter.Format(snapshot.BytesDone),
            SizeFormatter.Format(snapshot.BytesTotal));

        _rate.Add(nowUtc, snapshot.BytesDone);
        SpeedText = _rate.BytesPerSecond is { } speed
            ? string.Format(Strings.OperationSpeed, SizeFormatter.Format((long)speed))
            : "";
        RemainingText = _rate.Remaining(snapshot.BytesTotal - snapshot.BytesDone) is { } left
            ? string.Format(Strings.OperationRemaining, DurationFormat.Format(left))
            : "";
    }

    /// <summary>
    /// Names the problems of a finished operation, each with its reason in
    /// the user's words. Slow - <paramref name="describe"/> may ask who holds
    /// a file - so off the UI thread; the lines arrive through
    /// <see cref="ShowProblems"/>.
    /// </summary>
    /// <param name="describe">The error, the source, where it was going - and back the reason in words.</param>
    public static IReadOnlyList<string> DescribeProblems(OperationRecord record, Func<Exception, string, string?, string> describe) {
        var problems = record.Items.Where(i => i.IsProblem).ToList();
        var lines = problems
            .Take(ProblemLinesShown)
            .Select(i => string.Format(Strings.OperationProblemLine, NameOf(i.Source), describe(i.Error!, i.Source, i.Destination)))
            .ToList();
        if (problems.Count > ProblemLinesShown) {
            lines.Add(string.Format(Strings.AndMore, problems.Count - ProblemLinesShown));
        }

        return lines;
    }

    /// <summary>What <see cref="DescribeProblems"/> came back with. UI thread.</summary>
    public void ShowProblems(IReadOnlyList<string> lines) {
        ProblemLines = lines;
    }

    /// <summary>The removable drive "Извлечь" is for, or null when none the operation touched is there. UI thread.</summary>
    public void ShowEjectable(string? root) {
        if (_ejectRoot == root) {
            return;
        }

        _ejectRoot = root;
        Raise(nameof(CanEject));
        Raise(nameof(EjectText));
        CommandManager.InvalidateRequerySuggested();
    }


    private static string OutcomeOf(OperationRecord record) {
        var parts = new List<string>();
        if (record.Done > 0 || (record.Failed == 0 && record.Skipped == 0 && record.Cancelled == 0)) {
            parts.Add(string.Format(Strings.OperationOutcomeDone, record.Done));
        }
        if (record.Failed > 0) {
            parts.Add(string.Format(Strings.OperationOutcomeFailed, record.Failed));
        }
        if (record.Skipped > 0) {
            parts.Add(string.Format(Strings.OperationOutcomeSkipped, record.Skipped));
        }
        if (record.Cancelled > 0) {
            parts.Add(string.Format(Strings.OperationOutcomeCancelled, record.Cancelled));
        }
        if (record.Verified > 0) {
            parts.Add(string.Format(Strings.OperationOutcomeVerified, record.Verified));
        }

        return string.Join(", ", parts);
    }

    private static string NameOf(string path) {
        return Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
    }
}
