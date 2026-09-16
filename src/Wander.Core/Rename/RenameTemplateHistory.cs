namespace Wander.Core.Rename;

/// <summary>
/// The last few templates the batch-rename window applied, newest first -
/// what the template field offers in its drop-down, remembered in
/// <c>state.json</c>. The default template is not one of them: it is what
/// the field holds when nothing is being done to the name, and it would
/// only take a place from one worth coming back to.
/// </summary>
public static class RenameTemplateHistory {
    public const int Capacity = 5;


    /// <summary>
    /// The history once <paramref name="template"/> has been applied: it
    /// moves to the front, and the oldest falls off past
    /// <see cref="Capacity"/>.
    /// </summary>
    public static IReadOnlyList<string> Add(IReadOnlyList<string> history, string template) {
        if (template.Length == 0 || template == RenameRules.IdentityTemplate) {
            return history;
        }

        return new[] { template }
            .Concat(history.Where(t => !string.Equals(t, template, StringComparison.Ordinal)))
            .Take(Capacity)
            .ToArray();
    }
}
