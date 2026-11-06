using System.Windows;
using Wander.App.Resources;
using Wander.App.Util;
using Wander.App.ViewModels;
using Wander.Core.Actions;

namespace Wander.App.Dialogs;

/// <summary>
/// What <see cref="DeleteConfirmWindow"/> binds to: the words and rows of
/// <see cref="DeleteConfirmation"/>, refreshed as the background count
/// comes in. Decides nothing beyond what the buttons read and which of them
/// the keyboard can reach.
/// </summary>
public sealed class DeleteConfirmViewModel : ObservableObject {
    /// <summary>Width of the "inside" column, when there is one.</summary>
    private const double InsideColumn = 150;

    /// <summary>How tall a row of the table is.</summary>
    private const double RowHeight = 24;

    /// <summary>Rows shown before the table scrolls inside itself.</summary>
    private const int VisibleRows = 9;

    private readonly Dictionary<DeleteRow, DeleteRowItem> _items;
    private double _nameWidth = 200;


    public DeleteConfirmViewModel(DeleteConfirmation question) {
        Question = question;
        Rows = question.TableRows.Select(r => new DeleteRowItem(question, r)).ToList();
        _items = Rows.ToDictionary(i => i.Row);
    }


    public DeleteConfirmation Question { get; }

    public string Title => Question.Title;

    public string Headline => Question.Headline;

    /// <summary>The error mark rather than the warning one: nothing brings this back.</summary>
    public bool IsError => Question.Permanent;

    public bool IsSingle => Question.IsSingle;

    public bool IsList => !Question.IsSingle;

    public string? Path => Question.Path;

    public string Summary => Question.Summary(SizeFormatter.Format);

    public bool IsCounting => Question.IsCounting;

    public IReadOnlyList<string> Reasons => Question.ReasonLines;

    public bool HasReasons => Question.ReasonLines.Count > 0;

    public string? ReadOnlyLine => Question.ReadOnlyLine;

    public bool HasReadOnly => Question.ReadOnlyLine is not null;

    public string? RestLine => Question.RestLine;

    public bool HasRest => Question.RestLine is not null;

    public string? Warning => Question.Warning;

    public bool HasWarning => Question.Warning is not null;

    public IReadOnlyList<DeleteRowItem> Rows { get; }

    public bool HasTable => Rows.Count > 0;

    public bool ShowsHeader => Question.ShowsHeader;

    public bool ShowsInside => Question.ShowsInside;

    public GridLength InsideWidth => new(Question.ShowsInside ? InsideColumn : 0);

    /// <summary>The table scrolls inside itself past this height.</summary>
    public double TableMaxHeight => VisibleRows * RowHeight;

    /// <summary>The column names keep clear of the scroll bar, which only the rows have.</summary>
    public GridLength ScrollGutter => new(Rows.Count > VisibleRows ? SystemParameters.VerticalScrollBarWidth : 0);

    /// <summary>The name column at its narrowest: as wide as the longest name needs, within bounds - measured by the window, which knows the font.</summary>
    public double NameWidth {
        get => _nameWidth;
        set => SetField(ref _nameWidth, value);
    }

    public string ActionLabel => Question.Kind switch {
        DeleteQuestionKind.Permanent => Strings.ActionDelete,
        DeleteQuestionKind.Refused => Strings.ActionDeleteForGood,
        _ => Strings.ActionRecycle,
    };

    public string CancelLabel => Question.Kind == DeleteQuestionKind.Refused ? Strings.ActionAbort : Strings.ActionCancel;

    /// <summary>
    /// Whether Tab reaches the button that deletes: only for the bin, which
    /// Ctrl+Z undoes. Past the bin it is pressed with the mouse or not at
    /// all (decision of 2026-11-05).
    /// </summary>
    public bool ActionIsTabStop => !Question.Permanent;


    /// <summary>The count moved on: <paramref name="row"/>'s numbers, or every row's (null), and the totals.</summary>
    public void Changed(DeleteRow? row) {
        if (row is not null && _items.TryGetValue(row, out var item)) {
            item.Refresh();
        } else {
            foreach (var each in Rows) {
                each.Refresh();
            }
        }
        Raise(nameof(Summary));
        Raise(nameof(IsCounting));
    }
}


/// <summary>One row of the delete question's table, as the template binds it.</summary>
public sealed class DeleteRowItem : ObservableObject {
    private readonly DeleteConfirmation _question;


    public DeleteRowItem(DeleteConfirmation question, DeleteRow row) {
        _question = question;
        Row = row;
    }


    public DeleteRow Row { get; }

    public string Name => Row.Name;

    public string FullPath => Row.FullPath;

    public bool IsCompanion => Row.IsCompanion;

    public bool IsReadOnly => Row.IsReadOnly;

    /// <summary>A companion sits under its file; the companions of a single item have nothing above them to sit under.</summary>
    public Thickness Indent => new(Row.IsCompanion && !_question.IsSingle ? 30 : 8, 0, 8, 0);

    public bool IsCounting => Row.IsCounting;

    public string InsideText => _question.InsideOf(Row);

    public string SizeText => Row.IsCounting ? "" : SizeFormatter.Format(Row.Size);


    public void Refresh() {
        Raise(nameof(IsCounting));
        Raise(nameof(InsideText));
        Raise(nameof(SizeText));
    }
}
