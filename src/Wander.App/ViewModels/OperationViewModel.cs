using System.IO;
using System.Windows.Input;
using Wander.App.Resources;
using Wander.App.Util;
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
/// </summary>
public sealed class OperationViewModel : ObservableObject {
    private readonly TransferRate _rate = new();

    private string _verb = "";
    private string _currentFile = "";
    private string _itemsText = "";
    private string _bytesText = "";
    private string _speedText = "";
    private string _remainingText = "";
    private double _percent;
    private bool _hasBytes;
    private bool _cancelling;


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
    }


    /// <summary>Which operation in the tracker this is a view of.</summary>
    public long Id { get; }

    public ICommand ShowCommand { get; }

    public ICommand CancelCommand { get; }

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

    public string PercentText => string.Format(Strings.OperationPercent, (int)Math.Round(Percent));

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

    /// <summary>One line for a narrow place: "Копирование: 45 %".</summary>
    public string Summary => string.Format(Strings.OperationOne, Verb, (int)Math.Round(Percent));


    /// <summary>Takes in a fresh snapshot. <paramref name="nowUtc"/> feeds the speed average.</summary>
    public void Update(OperationSnapshot snapshot, DateTime nowUtc) {
        Verb = Strings.Get(snapshot.Verb);
        CurrentFile = string.IsNullOrEmpty(snapshot.CurrentPath) ? "" : Path.GetFileName(snapshot.CurrentPath);
        ItemsText = string.Format(Strings.OperationItems, snapshot.Completed, snapshot.Total);
        Percent = snapshot.Percent;
        Raise(nameof(Summary));

        // Work units are not bytes and must never be written as megabytes;
        // the percentage above is all an extraction can honestly show.
        HasBytes = snapshot.HasBytes && !snapshot.BytesAreWork;
        if (!HasBytes) {
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
}
