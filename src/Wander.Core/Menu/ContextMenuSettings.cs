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

    /// <summary>
    /// Fold every third-party entry under one "More options" submenu instead
    /// of listing them inline. Off by default — inline is what Windows 10
    /// does, and it is the layout people have muscle memory for. On is the
    /// escape hatch for a machine with four shell extensions installed.
    /// </summary>
    public bool ShellExtensionsInSubmenu { get; init; }

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
            ShellExtensionsInSubmenu = settings.ShellExtensionsInSubmenu,
            HiddenItems = hidden,
            BlockedShellExtensions = blocked,
        };
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
