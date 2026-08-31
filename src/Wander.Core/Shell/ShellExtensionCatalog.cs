using Wander.Core.Persistence;

namespace Wander.Core.Shell;

/// <summary>One line of the settings table.</summary>
public sealed record ShellExtensionRow {
    /// <summary>What the blocklist stores — see <see cref="ShellEntryKey"/>.</summary>
    public string Key { get; init; } = string.Empty;

    /// <summary>
    /// The other keys this one row also stands for.
    ///
    /// <para>
    /// Normally empty. It fills up where two registry entries are the same
    /// row to anyone reading the table — same caption, same application,
    /// same file types — which is what BitLocker's pairs look like: two
    /// verbs, one label, and no way to tell from the screen which is which.
    /// Ticking such a box has to switch off both, or the item stays in the
    /// menu and the checkbox reads as broken.
    /// </para>
    /// </summary>
    public IReadOnlyList<string> Aliases { get; init; } = Array.Empty<string>();

    /// <summary>Everything blocking this row has to switch off.</summary>
    public IEnumerable<string> AllKeys => Aliases.Count == 0 ? new[] { Key } : Aliases.Prepend(Key);

    /// <summary>Label for the "Пункт" column.</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>Owning application, or empty when the registry could not name one.</summary>
    public string AppName { get; init; } = string.Empty;

    /// <summary>
    /// What the row does, in the handler's own words. Empty for the many
    /// that publish nothing; the column then shows the scope instead of a
    /// blank, because "все файлы" is at least something.
    /// </summary>
    public string Help { get; init; } = string.Empty;

    /// <summary>Registry scopes it is installed on, already display-ready.</summary>
    public IReadOnlyList<string> Scopes { get; init; } = Array.Empty<string>();

    /// <summary>Checked in the table = the row is switched off in menus.</summary>
    public bool IsBlocked { get; init; }

    /// <summary>
    /// True when Wander has actually seen this row in a menu, as opposed to
    /// only finding it in the registry. Worth showing: a handler can be
    /// installed and still never draw anything.
    /// </summary>
    public bool IsSeen { get; init; }

    /// <inheritdoc cref="ShellHandler.IsSystem"/>
    public bool IsSystem { get; init; }
}


/// <summary>
/// Turns "what the registry says is installed" plus "what Wander has
/// actually met" into the rows of the settings table.
///
/// <para>
/// Two sources because neither is complete on its own. The registry knows
/// the application and the file types but not what a COM handler will draw;
/// the menu knows exactly what was drawn, and what the handler says it does,
/// but nothing about where it came from. Merging gets both halves for the
/// rows where the two agree — which, for handlers that name their registry
/// key after themselves, is most of them.
/// </para>
///
/// <para>
/// <b>Merging is on the normalised key, not the raw one.</b> The same row
/// reaches us as "Git Clone" from a build that keyed on labels and as
/// "Git Clone..." from one that keys on verbs, and two lines for one menu
/// item with one checkbox each is worse than useless — tick the wrong one
/// and nothing happens. The verb-shaped key wins the merge, because that is
/// what the blocklist should be storing from now on.
/// </para>
///
/// <para>
/// A row that only one source knows about is still listed, and says so
/// through <see cref="ShellExtensionRow.IsSeen"/> — with one exception,
/// applied to both sources: an entry with no name, no application and no
/// description is dropped. A line reading
/// "{9F156763-7844-4DC4-B2B1-901F640F5155}" next to an empty checkbox is
/// not a setting, it is a dare. Blocking one keeps it, so the switch
/// that turns it back on never disappears.
/// </para>
/// </summary>
public static class ShellExtensionCatalog {
    public static IReadOnlyList<ShellExtensionRow> Build(
        IReadOnlyList<ShellHandler> handlers,
        IReadOnlyList<KnownShellEntry> seen,
        IReadOnlySet<string> blockedKeys,
        bool includeSystem = false) {

        var rows = new Dictionary<string, ShellExtensionRow>(StringComparer.OrdinalIgnoreCase);
        var order = new List<string>();

        foreach (var handler in handlers) {
            if (!Offerable(handler, seen, blockedKeys, includeSystem)) {
                continue;
            }

            Merge(rows, order, new ShellExtensionRow {
                Key = handler.Key,
                Title = handler.Title.Length > 0 ? handler.Title : handler.Key,
                AppName = handler.AppName,
                Scopes = handler.Scopes,
                IsSystem = handler.IsSystem,
            });
        }

        foreach (var entry in seen) {
            string key = entry.Key.Trim();
            if (key.Length == 0) {
                continue;
            }
            // The same rule as for registry rows, and for the same reason:
            // a line reading "{9F156763-…}" is not a setting. It reaches the
            // seen list when a handler publishes its CLSID as the verb, and
            // there is nothing to say about it afterwards.
            if (!blockedKeys.Contains(key) && LooksLikeClsid(entry.Title) && entry.Help.Length == 0) {
                continue;
            }

            Merge(rows, order, new ShellExtensionRow {
                Key = key,
                Title = entry.Title.Length > 0 ? entry.Title : key,
                Help = entry.Help,
                Scopes = entry.Scope.Length > 0 ? new[] { entry.Scope } : Array.Empty<string>(),
                IsSeen = true,
            });
        }

        // A blocked key neither source produced still deserves its row —
        // otherwise the user could never switch it back on.
        foreach (string key in blockedKeys) {
            Merge(rows, order, new ShellExtensionRow { Key = key, Title = key });
        }

        return Fold(order.Select(key => rows[key]))
            .Select(row => row with { IsBlocked = row.AllKeys.Any(blockedKeys.Contains) })
            // Seen first — those are the rows the user has an opinion about —
            // then by application, then by label.
            .OrderByDescending(row => row.IsSeen)
            .ThenBy(row => row.AppName.Length == 0)
            .ThenBy(row => row.AppName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(row => row.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }


    /// <summary>
    /// Collapses rows that are the same row on screen: same caption, same
    /// application, same file types, different registry key.
    ///
    /// <para>
    /// Merging by key happens first and catches the ordinary case — one
    /// handler registered on six scopes. This is the other one: Windows
    /// gives BitLocker two verbs for "Включить BitLocker" on a drive, and
    /// they arrive as two rows that differ in nothing a reader can see. Two
    /// identical lines with a checkbox each is worse than one: tick either
    /// and the item is still in the menu.
    /// </para>
    ///
    /// <para>
    /// Nothing is lost by it — the second key rides along as an alias and
    /// the checkbox switches off both. Rows without a caption are left
    /// alone: two rows nobody can describe are not "the same row", they are
    /// two rows nobody can describe.
    /// </para>
    /// </summary>
    private static IReadOnlyList<ShellExtensionRow> Fold(IEnumerable<ShellExtensionRow> rows) {
        var byFace = new Dictionary<string, int>(StringComparer.CurrentCultureIgnoreCase);
        var folded = new List<ShellExtensionRow>();

        foreach (var row in rows) {
            string face = Face(row);
            if (face.Length == 0 || !byFace.TryGetValue(face, out int at)) {
                if (face.Length > 0) {
                    byFace[face] = folded.Count;
                }
                folded.Add(row);

                continue;
            }

            var kept = folded[at];
            folded[at] = kept with {
                Aliases = kept.AllKeys.Append(row.Key).Skip(1)
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                Help = kept.Help.Length > 0 ? kept.Help : row.Help,
                IsSeen = kept.IsSeen || row.IsSeen,
                IsSystem = kept.IsSystem && row.IsSystem,
            };
        }

        return folded;
    }

    /// <summary>
    /// Everything about a row that shows on screen, as one string. Empty
    /// for a row whose caption is only a restatement of its key — there is
    /// no face there to match.
    /// </summary>
    private static string Face(ShellExtensionRow row) {
        if (row.Title.Length == 0 || Same(row.Title, row.Key)) {
            return string.Empty;
        }

        return string.Join(
            '\u0000',
            ShellEntryKey.Normalize(row.Title),
            row.AppName,
            string.Join(',', row.Scopes.OrderBy(s => s, StringComparer.OrdinalIgnoreCase)));
    }


    /// <summary>
    /// Whether an installed handler is worth a line in the table at all.
    /// Blocked or already-met entries always are — hiding one of those would
    /// take away the switch that turns it back on.
    /// </summary>
    private static bool Offerable(
        ShellHandler handler,
        IReadOnlyList<KnownShellEntry> seen,
        IReadOnlySet<string> blockedKeys,
        bool includeSystem) {

        if (handler.Key.Length == 0) {
            return false;
        }
        // A verb Wander never draws is a switch that does nothing.
        if (ShellVerbs.IsSuppressed(handler.Key)) {
            return false;
        }

        bool pinned = blockedKeys.Contains(handler.Key)
            || seen.Any(e => Same(e.Key, handler.Key));
        if (pinned) {
            return true;
        }

        // A handler registered under its CLSID has no name a drawn row
        // could ever match: the shell reports verbs and labels, never
        // CLSIDs, so the checkbox on such a row cannot switch anything off.
        // Naming the row after the DLL's product info was an attempt to
        // make it readable, and it produced the worst of both — a line
        // called «Операционная система Microsoft® Windows®» whose checkbox
        // does nothing. Blocked and already-drawn rows are pinned above and
        // never reach this.
        if (LooksLikeClsid(handler.Key)) {
            return false;
        }

        return includeSystem || !handler.IsSystem;
    }

    /// <summary>
    /// Folds a row into the table under its normalised key, keeping the best
    /// of both when the key is already there. "Best" is per-field: the
    /// verb-shaped key, whichever title is not just the key, the first
    /// non-empty application and description, and the union of the scopes.
    /// </summary>
    private static void Merge(
        Dictionary<string, ShellExtensionRow> rows,
        List<string> order,
        ShellExtensionRow row) {

        string id = ShellEntryKey.Normalize(row.Key);
        if (id.Length == 0) {
            id = row.Key;
        }

        if (!rows.TryGetValue(id, out var existing)) {
            order.Add(id);
            rows[id] = row;

            return;
        }

        rows[id] = existing with {
            // A key that survives normalisation unchanged is a label; one
            // that does not is a verb ("Git Commit..."), and the verb is the
            // handle that does not move when a branch does.
            Key = row.Key.Length > existing.Key.Length ? row.Key : existing.Key,
            Title = Better(existing.Title, existing.Key, row.Title, row.Key),
            AppName = existing.AppName.Length > 0 ? existing.AppName : row.AppName,
            Help = existing.Help.Length > 0 ? existing.Help : row.Help,
            Scopes = existing.Scopes.Concat(row.Scopes).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            IsSeen = existing.IsSeen || row.IsSeen,
            IsSystem = existing.IsSystem && row.IsSystem,
        };
    }

    /// <summary>
    /// Of two titles, the one that is not merely a restatement of its key.
    /// "Open Git Bash here" beats "git_shell" every time.
    /// </summary>
    private static string Better(string title, string key, string otherTitle, string otherKey) {
        bool isName = !Same(title, key);
        bool otherIsName = !Same(otherTitle, otherKey);

        if (isName == otherIsName) {
            return title.Length > 0 ? title : otherTitle;
        }

        return isName ? title : otherTitle;
    }

    private static bool Same(string a, string b) {
        return string.Equals(
            ShellEntryKey.Normalize(a), ShellEntryKey.Normalize(b), StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeClsid(string text) {
        string trimmed = text.Trim();

        return trimmed.Length > 2 && trimmed[0] == '{' && trimmed[^1] == '}';
    }
}
