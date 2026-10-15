namespace Wander.Core.Operations;

/// <summary>
/// What "Повторить" on a finished operation does again: its items that did
/// not get there - failed or cancelled - and nothing else. A renamed or a
/// skipped item went where the user's answer sent it, and doing it again
/// would make a second copy of what is already there.
///
/// <para>
/// The item is the main file of its group; the companions are not in the
/// record and are gathered again on the way in, as a paste gathers them.
/// Whether the source was being moved does not matter: it did not get
/// there, so it is still where it was.
/// </para>
/// </summary>
public static class OperationRetry {
    /// <summary>The plan, or null when there is nothing to try again or the operation is not one that can be.</summary>
    public static RetryPlan? Plan(OperationRecord record) {
        bool delete = record.Verb is OperationVerbs.Recycle or OperationVerbs.DeletePermanently;
        if (!delete && record.Verb is not (OperationVerbs.Copy or OperationVerbs.Move)) {
            return null;
        }

        var items = new List<RetryItem>();
        foreach (var item in record.Items.Where(i => i.IsRetriable)) {
            if (delete) {
                items.Add(new RetryItem(item.Source, null));
                continue;
            }

            string? folder = item.Destination is { } destination ? Path.GetDirectoryName(destination) : record.Target;
            if (!string.IsNullOrEmpty(folder)) {
                items.Add(new RetryItem(item.Source, folder));
            }
        }

        return items.Count == 0 ? null : new RetryPlan(record.Verb, items);
    }
}


/// <summary>What a retry does: the operation's verb, and the items to do it to again.</summary>
public sealed record RetryPlan(string Verb, IReadOnlyList<RetryItem> Items) {
    public bool IsDelete => Verb is OperationVerbs.Recycle or OperationVerbs.DeletePermanently;
}


/// <param name="DestinationFolder">Where it was going; null for a delete.</param>
public sealed record RetryItem(string Source, string? DestinationFolder);
