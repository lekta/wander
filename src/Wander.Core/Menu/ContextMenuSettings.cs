using Wander.Core.Persistence;
using Wander.Core.Shell;

namespace Wander.Core.Menu;

/// <summary>
/// The user's pruning of the context menu, in the shape the builder wants.
/// Projected from <see cref="AppSettings"/> (which persists the same data
/// as plain string lists, so it survives JSON round-trips and enum
/// reordering).
/// </summary>
public sealed record ContextMenuSettings {
    /// <summary>
    /// How many discovered third-party names are worth remembering. The list
    /// exists only so the settings dialog has checkboxes to offer, and some
    /// handlers put volatile text in their top-level label — TortoiseGit
    /// shows «Git Commit -&gt; "master"...», one row per branch. Left alone
    /// that grows <c>state.json</c> forever.
    /// </summary>
    public const int MaxKnownShellExtensions = 100;


    /// <summary>Out-of-the-box configuration: nothing hidden, extensions on.</summary>
    public static readonly ContextMenuSettings Default = new();


    /// <summary>Master switch for third-party handlers (7-Zip, TortoiseGit, …).</summary>
    public bool ShellExtensionsEnabled { get; init; } = true;

    /// <summary>Built-in entries the user chose not to see.</summary>
    public IReadOnlySet<MenuCommandId> HiddenItems { get; init; } = new HashSet<MenuCommandId>();

    /// <summary>
    /// Third-party entries the user blocked, by <see cref="ShellEntryKey"/> —
    /// the canonical verb where there is one, the normalised label
    /// otherwise. Command ids are per-session and CLSIDs aren't exposed
    /// through <c>IContextMenu</c>, so this is the whole of the identity
    /// available to us.
    /// </summary>
    public IReadOnlySet<string> BlockedShellExtensions { get; init; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);


    /// <summary>Reads the persisted lists into lookup-friendly sets.</summary>
    public static ContextMenuSettings From(AppSettings settings) {
        var hidden = new HashSet<MenuCommandId>();
        foreach (string name in settings.HiddenContextMenuItems) {
            // Unknown names are ignored rather than rejected: they're what a
            // downgrade or a renamed enum member leaves behind, and dropping
            // them quietly beats failing to build a menu at all.
            if (Enum.TryParse(name, out MenuCommandId id) && id != MenuCommandId.None) {
                hidden.Add(id);
            }
        }

        var blocked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string name in settings.BlockedShellExtensions) {
            // Both forms. A verb is stored verbatim, dots and all
            // ("Git Commit..."), while a label written by an older build
            // carries Win32 decoration ("&7-Zip") — and the lookup should
            // not have to know which of the two it is holding.
            blocked.Add(name.Trim());
            blocked.Add(ShellEntryKey.Normalize(name));
        }
        blocked.Remove(string.Empty);

        return new ContextMenuSettings {
            ShellExtensionsEnabled = settings.ShellExtensionsEnabled,
            HiddenItems = hidden,
            BlockedShellExtensions = blocked,
        };
    }


    /// <inheritdoc cref="TrimKnownExtensions"/>
    public static IReadOnlyList<KnownShellEntry> TrimKnownEntries(
        IEnumerable<KnownShellEntry> known, IEnumerable<string> blocked) {

        var kept = TrimKnownExtensions(known.Select(e => e.Key), blocked)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        return known
            .Where(e => kept.Contains(e.Key.Trim()) && seen.Add(e.Key.Trim()))
            .ToArray();
    }


    /// <summary>
    /// Trims the remembered list for persistence: de-duplicated, oldest
    /// dropped first — but a blocked key is never dropped, because losing it
    /// would silently switch a handler the user turned off back on. If more
    /// than <see cref="MaxKnownShellExtensions"/> keys are blocked, all of
    /// them are still kept: an honest list beats a tidy one that lies.
    /// </summary>
    public static IReadOnlyList<string> TrimKnownExtensions(
        IEnumerable<string> known, IEnumerable<string> blocked) {

        var blockedNames = new HashSet<string>(blocked.Select(k => k.Trim()), StringComparer.OrdinalIgnoreCase);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var names = new List<string>();
        foreach (string raw in known) {
            string name = raw.Trim();
            if (name.Length > 0 && seen.Add(name)) {
                names.Add(name);
            }
        }

        int excess = names.Count - MaxKnownShellExtensions;
        if (excess <= 0) {
            return names;
        }

        // Oldest first — the list is append-ordered, so the front is what
        // has been sitting around unused the longest.
        var dropped = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string name in names) {
            if (excess == 0) {
                break;
            }
            if (blockedNames.Contains(name)) {
                continue;
            }
            dropped.Add(name);
            excess--;
        }

        return names.Where(name => !dropped.Contains(name)).ToArray();
    }


    /// <inheritdoc cref="ShellEntryKey.Normalize"/>
    public static string NormalizeName(string header) {
        return ShellEntryKey.Normalize(header);
    }


    public bool IsHidden(MenuCommandId id) {
        return id != MenuCommandId.None && HiddenItems.Contains(id);
    }

    /// <summary>
    /// Whether a row the shell reported was switched off.
    ///
    /// <para>
    /// Both handles are checked, not just the current one: blocklists
    /// written before the key became verb-first hold labels, and a settings
    /// file is not worth a migration step when the lookup can simply ask
    /// twice. The label is also what a user recognises, so a block set from
    /// a hand-edited <c>state.json</c> keeps working.
    /// </para>
    /// </summary>
    public bool IsBlocked(string? verb, string? header) {
        if (BlockedShellExtensions.Count == 0) {
            return false;
        }

        return BlockedShellExtensions.Contains(ShellEntryKey.For(verb, header))
            || BlockedShellExtensions.Contains(ShellEntryKey.Normalize(header));
    }
}
