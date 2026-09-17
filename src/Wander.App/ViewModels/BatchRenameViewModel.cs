using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Wander.App.Resources;
using Wander.Core.Icons;
using Wander.Core.Rename;

namespace Wander.App.ViewModels;

/// <summary>
/// The batch-rename window's state: the rules as editable properties, and
/// the "was / becomes" table worked out from them again on every change.
/// The working out is <see cref="RenamePlanner"/>'s; what is here is the
/// part that needs a UI - the wording of the rows, and reading shot dates
/// off the UI thread the first time the template asks for them.
///
/// <para>
/// The items arrive in the order the list shows them, and the counter runs
/// in that order: there is no reordering in here, and the window says so.
/// </para>
/// </summary>
public sealed class BatchRenameViewModel : ObservableObject {
    private readonly IReadOnlyList<RenameItem> _items;
    private readonly RenameContext _context;
    private readonly IImageMetadataReader? _metadata;
    private readonly CancellationTokenSource _cts = new();

    // Shot dates read so far, by path; a null value is a picture that has
    // none. Written on the UI thread only, once a read pass has come back,
    // so the planner reading it mid-preview never races the reader.
    private readonly Dictionary<string, DateTime?> _shotDates = new(StringComparer.OrdinalIgnoreCase);

    private RenamePreview _preview;
    private bool _shotDatesRequested;
    private bool _isReadingShotDates;


    /// <param name="items">What is being renamed, in the list's order.</param>
    /// <param name="context">The world as the planner sees it; the shot dates are filled in here.</param>
    /// <param name="metadata">Where <c>[X]</c> comes from; null leaves it on the modified date.</param>
    /// <param name="rules">What the window opens with: where it was left last time.</param>
    /// <param name="templateHistory">The last templates applied, newest first, for the template field's drop-down.</param>
    public BatchRenameViewModel(
        IReadOnlyList<RenameItem> items, RenameContext context, IImageMetadataReader? metadata,
        RenameRules rules, IReadOnlyList<string> templateHistory) {
        _items = items;
        _metadata = metadata;
        _context = context with { ShotDate = ShotDateOf };
        TemplateHistory = templateHistory;

        _useFind = rules.Find.Length > 0;
        _find = rules.Find;
        _replace = rules.Replace;
        _findIgnoreCase = rules.FindIgnoreCase;
        _findIsRegex = rules.FindIsRegex;
        _template = rules.Template;
        _counterStart = rules.CounterStart;
        _counterStep = rules.CounterStep;
        _counterWidth = Math.Clamp(rules.CounterWidth, 1, RenameRules.MaxCounterWidth);
        _nameCase = rules.NameCase;
        _extensionCase = rules.ExtensionCase;

        Recompute();
        RequestShotDates();
    }


    public string Title => string.Format(Strings.BatchRenameTitle, _items.Count);

    public BulkObservableCollection<BatchRenameRow> Rows { get; } = new();

    public IReadOnlyList<string> TemplateHistory { get; }

    public IReadOnlyList<CaseChoice> CaseChoices { get; } = new[] {
        new CaseChoice(NameCase.Unchanged, Strings.BatchRenameCaseUnchanged),
        new CaseChoice(NameCase.Lower, Strings.BatchRenameCaseLower),
        new CaseChoice(NameCase.Upper, Strings.BatchRenameCaseUpper),
        new CaseChoice(NameCase.Sentence, Strings.BatchRenameCaseSentence),
    };

    /// <summary>What OK applies: the table as it stands.</summary>
    public RenamePreview Preview => _preview;

    public bool CanApply => _preview.CanApply;

    public string Summary => string.Format(Strings.RenameSummary, _preview.Changed, _items.Count, _preview.Conflicts);

    /// <summary>Why the rules cannot be applied at all, in the runtime's own words after ours; null when they can.</summary>
    public string? RuleError => _preview.RuleErrorKey is { } key
        ? $"{Strings.Get(key)}: {_preview.RuleErrorDetail}"
        : null;

    public bool HasRuleError => _preview.RuleErrorKey is not null;

    /// <summary>True while EXIF dates are being read; until then <c>[X]</c> prints the modified date.</summary>
    public bool IsReadingShotDates {
        get => _isReadingShotDates;
        private set => SetField(ref _isReadingShotDates, value);
    }

    /// <summary>The rules as the fields say now - what the planner reads and what is remembered.</summary>
    public RenameRules Rules => new() {
        Find = _useFind ? _find : string.Empty,
        Replace = _replace,
        FindIgnoreCase = _findIgnoreCase,
        FindIsRegex = _findIsRegex,
        Template = _template,
        CounterStart = _counterStart,
        CounterStep = _counterStep,
        CounterWidth = _counterWidth,
        NameCase = _nameCase,
        ExtensionCase = _extensionCase,
    };


    // --- Rules ---------------------------------------------------------------

    private bool _useFind;
    /// <summary>
    /// Find / replace is on. Off, the two fields keep what was typed in
    /// them for this window, but the rule is not applied and not remembered.
    /// </summary>
    public bool UseFind {
        get => _useFind;
        set => SetRule(ref _useFind, value);
    }

    private string _find;
    public string Find {
        get => _find;
        set => SetRule(ref _find, value ?? string.Empty);
    }

    private string _replace;
    public string Replace {
        get => _replace;
        set => SetRule(ref _replace, value ?? string.Empty);
    }

    private bool _findIgnoreCase;
    public bool FindIgnoreCase {
        get => _findIgnoreCase;
        set => SetRule(ref _findIgnoreCase, value);
    }

    private bool _findIsRegex;
    public bool FindIsRegex {
        get => _findIsRegex;
        set => SetRule(ref _findIsRegex, value);
    }

    private string _template;
    public string Template {
        get => _template;
        set {
            if (SetRule(ref _template, value ?? string.Empty)) {
                RequestShotDates();
            }
        }
    }

    private int _counterStart;
    public int CounterStart {
        get => _counterStart;
        set => SetRule(ref _counterStart, value);
    }

    private int _counterStep;
    public int CounterStep {
        get => _counterStep;
        set => SetRule(ref _counterStep, value);
    }

    private int _counterWidth;
    public int CounterWidth {
        get => _counterWidth;
        set => SetRule(ref _counterWidth, Math.Clamp(value, 1, RenameRules.MaxCounterWidth));
    }

    private NameCase _nameCase;
    public NameCase NameCase {
        get => _nameCase;
        set => SetRule(ref _nameCase, value);
    }

    private NameCase _extensionCase;
    public NameCase ExtensionCase {
        get => _extensionCase;
        set => SetRule(ref _extensionCase, value);
    }


    // --- Lifetime ------------------------------------------------------------

    /// <summary>The window is going away: a read still running has nobody to report to.</summary>
    public void Stop() {
        _cts.Cancel();
    }


    // --- Helpers -------------------------------------------------------------

    private bool SetRule<T>(ref T field, T value, [CallerMemberName] string? name = null) {
        if (!SetField(ref field, value, name)) {
            return false;
        }
        Recompute();

        return true;
    }

    [MemberNotNull(nameof(_preview))]
    private void Recompute() {
        _preview = RenamePlanner.Preview(Rules, _items, _context);
        Rows.ReplaceAll(_preview.Rows.Select(BatchRenameRow.From).ToArray());
        Raise(nameof(Preview));
        Raise(nameof(CanApply));
        Raise(nameof(Summary));
        Raise(nameof(RuleError));
        Raise(nameof(HasRuleError));
    }

    private DateTime? ShotDateOf(string path) {
        return _shotDates.TryGetValue(path, out var date) ? date : null;
    }

    /// <summary>
    /// Starts the one read of shot dates, the first time the template has
    /// <c>[X]</c> in it. A RAW file takes a while to open, so the reading is
    /// on the pool, the rows show the modified date until it is done, and
    /// the table is worked out again when it is.
    /// </summary>
    private void RequestShotDates() {
        if (_shotDatesRequested || _metadata is null) {
            return;
        }
        if (!_template.Contains("[X]", StringComparison.Ordinal) && !_template.Contains("[X:", StringComparison.Ordinal)) {
            return;
        }

        _shotDatesRequested = true;
        var paths = _items
            .Where(item => !item.IsFolder && ImageFormats.IsImage(item.FullPath))
            .Select(item => item.FullPath)
            .ToArray();
        if (paths.Length > 0) {
            _ = ReadShotDatesAsync(_metadata, paths);
        }
    }

    private async Task ReadShotDatesAsync(IImageMetadataReader reader, IReadOnlyList<string> paths) {
        var token = _cts.Token;
        IsReadingShotDates = true;
        List<(string Path, DateTime? Date)> read;
        try {
            read = await Task.Run(() => {
                var found = new List<(string, DateTime?)>(paths.Count);
                foreach (string path in paths) {
                    token.ThrowIfCancellationRequested();
                    found.Add((path, reader.Read(path)?.DateTaken));
                }

                return found;
            }, token);
        } catch (OperationCanceledException) {
            return;
        } finally {
            IsReadingShotDates = false;
        }

        foreach (var (path, date) in read) {
            _shotDates[path] = date;
        }
        Recompute();
    }
}


/// <summary>One line of the window's table, worded.</summary>
public sealed record BatchRenameRow(string OldName, string NewName, string StatusText, bool IsConflict, bool IsUnchanged) {
    public static BatchRenameRow From(RenameRow row) {
        string status = row.Status switch {
            RenameRowStatus.Renamed => Strings.RenameStatusRenamed,
            RenameRowStatus.InvalidName => Strings.RenameStatusInvalidName,
            RenameRowStatus.DuplicateInBatch => Strings.RenameStatusDuplicateInBatch,
            RenameRowStatus.Collides => Strings.RenameStatusCollides,
            _ => Strings.RenameStatusUnchanged,
        };

        return new BatchRenameRow(row.OldName, row.NewName, status, row.IsConflict, row.Status == RenameRowStatus.Unchanged);
    }
}


/// <summary>A row of the two case pickers.</summary>
public sealed record CaseChoice(NameCase Value, string Title);
