using System.Collections.Immutable;
using Wander.Core.FileSystem;

namespace Wander.Core.Listing;

/// <summary>
/// The list's selection as facts: the rows by path, the main one, and the
/// caret - the row the keyboard stands on, or comes back to. The window's
/// model holds it; the list shows it and reports what the user does to it.
/// </summary>
/// <param name="Selection">The selected rows by path.</param>
/// <param name="Primary">The main one of them - what Enter opens and F2 renames; null with nothing selected.</param>
/// <param name="Caret">The row the keyboard is on, selected or not; null for none.</param>
public sealed record ListState(ImmutableArray<string> Selection, string? Primary, string? Caret) {
    public static readonly ListState Empty = new(ImmutableArray<string>.Empty, null, null);
}


/// <summary>Why the list's rows were laid down again - what decides what a row gone means.</summary>
public enum ListingReason {
    /// <summary>Another folder's rows: the one walked into.</summary>
    Arrival,

    /// <summary>The same folder's rows again - F5, the watcher, an operation, the filter, a setting.</summary>
    Relist,

    /// <summary>The same rows, some swapped for updated copies - a rating, a size. Nothing came or went.</summary>
    RowsReplaced,

    /// <summary>The folder's rows after search results: a result not in the listing did not leave the folder.</summary>
    ResultsLeft,
}


/// <summary>The list's selection after a landing, and what showing it takes.</summary>
/// <param name="List">The selection, its main row and the caret.</param>
/// <param name="Scroll">The main row is brought into view.</param>
/// <param name="Editor">The row whose name editor opens - a folder just created (L-12).</param>
public sealed record ListLanding(ListState List, bool Scroll, string? Editor);


/// <summary>
/// What is selected once the list's rows have landed (REDESIGN 4.5, module
/// 3): the rows an intent asked for, else the selection as it stood, by
/// path - a rename, ours or another program's, carries it to the new name
/// (decision B7). A selected row that is gone, for whatever reason - deleted
/// here or elsewhere, hidden by the filter or the settings - hands the
/// selection to the row that took its place (decision B6,
/// <see cref="CurrentRowFallback"/>); only search results leave nothing
/// behind them. Scrolling is for what was asked for, never for what merely
/// stayed. Where the keyboard goes is the keyboard's rules' to say.
/// </summary>
public static class ListingArrival {
    /// <param name="list">The selection before the rows landed.</param>
    /// <param name="before">The rows before, in order.</param>
    /// <param name="after">The rows now, in order.</param>
    /// <param name="reason">Why they were laid down.</param>
    /// <param name="intent">What the pending intent came to (<see cref="FolderSession.DecideArrival"/>).</param>
    /// <param name="renames">Rows renamed or moved since the last landing, old path to new.</param>
    public static ListLanding Land(
        ListState list, IReadOnlyList<string> before, IReadOnlyList<string> after,
        ListingReason reason, ArrivalDecision intent, IReadOnlyList<(string From, string To)> renames) {
        list = Follow(list, renames);

        switch (intent.Outcome) {
            case ArrivalOutcome.SelectRows:
                var rows = intent.Rows.Select(r => r.FullPath).ToImmutableArray();

                return new ListLanding(new ListState(rows, rows[0], rows[0]), Scroll: true, intent.RenameTarget);

            case ArrivalOutcome.SelectFolder:
                // A folder opened from a panel row is not a row of its own
                // listing: nothing in the list is selected.
                return new ListLanding(ListState.Empty, Scroll: false, Editor: null);
        }

        var settled = reason switch {
            ListingReason.Arrival => ListState.Empty,
            ListingReason.ResultsLeft => Settle(list, before, after, successor: false),
            _ => Settle(list, before, after, successor: true),
        };

        return new ListLanding(settled, Scroll: false, Editor: null);
    }


    /// <summary>
    /// The selection over the rows that stayed. Some of it left: the rest
    /// stays selected, the caret on a row gone goes to the main row. All of
    /// it left: the row that took the last one's place is selected, the
    /// caret on it. Nothing was selected: only a caret on a row gone moves.
    /// </summary>
    private static ListState Settle(ListState list, IReadOnlyList<string> before, IReadOnlyList<string> after, bool successor) {
        var standing = new HashSet<string>(after, StringComparer.OrdinalIgnoreCase);
        var kept = list.Selection.Where(standing.Contains).ToImmutableArray();
        bool caretStays = list.Caret is null || standing.Contains(list.Caret);

        if (kept.Length > 0) {
            string primary = list.Primary is { } main && standing.Contains(main) ? main : kept[0];

            return new ListState(kept, primary, caretStays ? list.Caret : primary);
        }

        if (list.Selection.Length > 0) {
            if (successor && CurrentRowFallback.After(before, list.Selection, after) is { } next) {
                return new ListState(ImmutableArray.Create(next), next, next);
            }

            return new ListState(ImmutableArray<string>.Empty, null, caretStays ? list.Caret : null);
        }

        if (caretStays) {
            return list;
        }

        return list with { Caret = successor ? CurrentRowFallback.After(before, new[] { list.Caret! }, after) : null };
    }

    /// <summary>The selection with every renamed or moved row under its new path.</summary>
    private static ListState Follow(ListState list, IReadOnlyList<(string From, string To)> renames) {
        foreach (var (from, to) in renames) {
            list = new ListState(
                list.Selection.Select(p => PathRewrite.Under(p, from, to) ?? p).ToImmutableArray(),
                Moved(list.Primary, from, to),
                Moved(list.Caret, from, to));
        }

        return list;
    }

    private static string? Moved(string? path, string from, string to) {
        return path is null ? null : PathRewrite.Under(path, from, to) ?? path;
    }
}
