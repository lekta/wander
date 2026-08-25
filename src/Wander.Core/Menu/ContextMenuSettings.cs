using Wander.Core.Persistence;

namespace Wander.Core.Menu;

/// <summary>
/// The user's pruning of the context menu, in the shape the builder wants.
/// Projected from <see cref="AppSettings"/> (which persists the same data
/// as plain string lists, so it survives JSON round-trips and enum
/// reordering).
/// </summary>
public sealed record ContextMenuSettings {
    /// <summary>Out-of-the-box configuration: nothing hidden, extensions on.</summary>
    public static readonly ContextMenuSettings Default = new();


    /// <summary>Master switch for third-party handlers (7-Zip, TortoiseGit, …).</summary>
    public bool ShellExtensionsEnabled { get; init; } = true;

    /// <summary>Built-in entries the user chose not to see.</summary>
    public IReadOnlySet<MenuCommandId> HiddenItems { get; init; } = new HashSet<MenuCommandId>();

    /// <summary>
    /// Third-party entries the user blocked, by normalised header text —
    /// the only stable handle we have, since shell command ids are
    /// per-session and CLSIDs aren't exposed through <c>IContextMenu</c>.
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
            blocked.Add(NormalizeName(name));
        }

        return new ContextMenuSettings {
            ShellExtensionsEnabled = settings.ShellExtensionsEnabled,
            HiddenItems = hidden,
            BlockedShellExtensions = blocked,
        };
    }


    /// <summary>
    /// How many discovered third-party names are worth remembering. The list
    /// exists only so the settings dialog has checkboxes to offer, and some
    /// handlers put volatile text in their top-level label — TortoiseGit
    /// shows «Git Commit -&gt; "master"...», one row per branch. Left alone
    /// that grows <c>state.json</c> forever.
    /// </summary>
    public const int MaxKnownShellExtensions = 100;


    /// <summary>
    /// Trims the remembered-names list for persistence: normalised,
    /// de-duplicated, oldest dropped first — but a blocked name is never
    /// dropped, because losing it would silently switch a handler the user
    /// turned off back on. If more than <see cref="MaxKnownShellExtensions"/>
    /// names are blocked, all of them are still kept: an honest list beats a
    /// tidy one that lies.
    /// </summary>
    public static IReadOnlyList<string> TrimKnownExtensions(
        IEnumerable<string> known, IEnumerable<string> blocked) {

        var blockedNames = new HashSet<string>(blocked.Select(NormalizeName), StringComparer.OrdinalIgnoreCase);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var names = new List<string>();
        foreach (string raw in known) {
            string name = NormalizeName(raw);
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


    /// <summary>
    /// Strips the decoration Win32 menus carry — the <c>&amp;</c> accelerator
    /// markers and a trailing ellipsis — so "&amp;7-Zip" and "7-Zip" are the
    /// same blocklist entry.
    /// </summary>
    public static string NormalizeName(string header) {
        if (string.IsNullOrEmpty(header)) {
            return string.Empty;
        }

        string text = header.Replace("&", "").Trim();
        if (text.EndsWith("...", StringComparison.Ordinal)) {
            text = text[..^3].TrimEnd();
        } else if (text.EndsWith("…", StringComparison.Ordinal)) {
            text = text[..^1].TrimEnd();
        }

        return text;
    }


    public bool IsHidden(MenuCommandId id) {
        return id != MenuCommandId.None && HiddenItems.Contains(id);
    }

    public bool IsBlocked(string header) {
        return BlockedShellExtensions.Contains(NormalizeName(header));
    }
}
