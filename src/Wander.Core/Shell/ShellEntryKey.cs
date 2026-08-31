namespace Wander.Core.Shell;

/// <summary>
/// The stable handle Wander uses to remember "the user switched this row
/// off". <c>IContextMenu</c> gives out command ids that are valid only
/// inside the session that produced them, and never says which handler a
/// row came from, so the identity has to be reconstructed from what the
/// row itself carries.
///
/// <para>
/// <b>Verb first, label only as a fallback.</b> The label is localised and
/// routinely has live data baked into it — TortoiseGit's row reads
/// «Git Commit -&gt; "master"...», branch name and all. Key it by label and
/// switching branches invents a brand-new "unknown extension" while the
/// block the user set silently stops matching. The verb of that same row is
/// <c>Git Commit...</c>, without the branch, and does not move.
/// </para>
///
/// <para>
/// Some handlers publish no verb at all (7-Zip's top-level popup is one),
/// and for those the normalised label is all there is. It is stable enough
/// in practice — those labels are application names, not sentences.
/// </para>
/// </summary>
public static class ShellEntryKey {
    /// <summary>
    /// Key for a row the shell reported. Empty only if the row has neither
    /// verb nor label, which means it is a separator.
    /// </summary>
    public static string For(string? verb, string? header) {
        string trimmed = verb?.Trim() ?? string.Empty;

        return trimmed.Length > 0 ? trimmed : Normalize(header);
    }


    /// <summary>
    /// Strips the decoration Win32 menus carry — the <c>&amp;</c> accelerator
    /// markers and a trailing ellipsis — so "&amp;7-Zip" and "7-Zip" are the
    /// same entry.
    /// </summary>
    public static string Normalize(string? header) {
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
}
