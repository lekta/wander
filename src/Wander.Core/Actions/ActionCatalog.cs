namespace Wander.Core.Actions;

/// <summary>
/// The catalog as one list, and the two things it is made of: the presets
/// in the code and the rows in <c>state.json</c>. Only what the user
/// touched is stored - a preset they switched off, and their own rows - so
/// an untouched preset follows the code: a new one appears on update, an
/// improved one is not shadowed by a stale copy.
///
/// <para>
/// Also where the presets' programs are. A preset names its tool
/// ("ffmpeg"); the user may have pointed Wander at it on the settings page
/// (<see cref="ToolPath"/>), otherwise it is looked for on <c>PATH</c> and in
/// the folders installers use (<see cref="IToolLocator"/>). A path the user
/// gave wins: it is the one explicit answer there is.
/// </para>
/// </summary>
public static class ActionCatalog {
    /// <summary>
    /// Presets first, in code order, each with what the user changed on it
    /// laid over - only the switch and a program of their own, so the
    /// command line and everything else follow the code and an improved
    /// preset reaches a user who switched it off once; then the user's rows
    /// in stored order. A stored preset the code no longer ships is dropped.
    /// A row without an id gets one - the menu finds actions by id.
    /// </summary>
    public static IReadOnlyList<CustomAction> Merge(IReadOnlyList<CustomAction> presets, IReadOnlyList<CustomAction> stored) {
        var presetIds = new HashSet<string>(presets.Select(p => p.Id), StringComparer.Ordinal);
        var storedById = new Dictionary<string, CustomAction>(StringComparer.Ordinal);
        foreach (var row in stored) {
            if (row.Id.Length > 0) {
                storedById.TryAdd(row.Id, row);
            }
        }

        var result = presets
            .Select(p => storedById.TryGetValue(p.Id, out var own) ? WithOverride(p, own) : p)
            .ToList();

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in stored) {
            if (row.IsPreset || presetIds.Contains(row.Id)) {
                continue;
            }
            if (row.Id.Length == 0) {
                result.Add(row with { Id = NewId() });
                continue;
            }
            if (seen.Add(row.Id)) {
                result.Add(row);
            }
        }

        return result;
    }


    /// <summary>
    /// What goes to <c>state.json</c>: the user's rows whole, and for a
    /// preset only what they changed on it (<see cref="Override"/>) - a
    /// preset nobody touched is not stored at all.
    /// </summary>
    public static IReadOnlyList<CustomAction> ToStored(IReadOnlyList<CustomAction> presets, IReadOnlyList<CustomAction> catalog) {
        var presetsById = presets.ToDictionary(p => p.Id, StringComparer.Ordinal);

        var stored = new List<CustomAction>();
        foreach (var row in catalog) {
            if (!presetsById.TryGetValue(row.Id, out var shipped)) {
                if (!row.IsPreset) {
                    stored.Add(row);
                }
                continue;
            }
            if (Override(shipped, row) is { } changed) {
                stored.Add(changed);
            }
        }

        return stored;
    }


    /// <summary>
    /// The user's changes to a preset, as the row that is stored: the id,
    /// the switch, and the program when it is not the shipped one. Null
    /// when nothing was changed. Everything else on a preset is the code's.
    /// </summary>
    public static CustomAction? Override(CustomAction shipped, CustomAction row) {
        bool ownProgram = !string.Equals(row.Program, shipped.Program, StringComparison.OrdinalIgnoreCase);
        if (row.Enabled == shipped.Enabled && !ownProgram) {
            return null;
        }

        return new CustomAction {
            Id = shipped.Id,
            IsPreset = true,
            Enabled = row.Enabled,
            Program = ownProgram ? row.Program : string.Empty,
        };
    }

    /// <summary>The shipped preset with a stored override laid over it - the reverse of <see cref="Override"/>.</summary>
    private static CustomAction WithOverride(CustomAction shipped, CustomAction stored) {
        return shipped with {
            Enabled = stored.Enabled,
            Program = stored.Program.Trim().Length > 0 ? stored.Program : shipped.Program,
        };
    }


    /// <summary>A new row of the user's own, runnable on anything once given a program.</summary>
    public static CustomAction NewAction(string title) {
        return new CustomAction { Id = NewId(), Title = title, Arguments = "{path}" };
    }

    /// <summary>
    /// The user's copy of a row - how a preset is edited. It keeps
    /// everything but the identity: a copy of a conversion is still a
    /// conversion and still needs its tool.
    /// </summary>
    public static CustomAction CopyOf(CustomAction source, string title) {
        return source with { Id = NewId(), Title = title, TitleKey = string.Empty, IsPreset = false };
    }


    /// <summary>
    /// The programs the settings page shows: the listed ones in their order,
    /// then any other a row of the catalog needs.
    /// </summary>
    public static IReadOnlyList<string> ToolNames(IReadOnlyList<CustomAction> catalog) {
        var names = ActionPresets.Tools.Select(t => t.Name).ToList();
        foreach (var row in catalog) {
            if (row.RequiredTool.Length > 0 && !names.Contains(row.RequiredTool, StringComparer.OrdinalIgnoreCase)) {
                names.Add(row.RequiredTool);
            }
        }

        return names;
    }

    /// <summary>
    /// Where each program of <see cref="ToolNames"/> is: the path the user
    /// gave, if they gave one - even when the file is gone, which is then
    /// said rather than quietly replaced - otherwise the locator's answer.
    /// </summary>
    public static IReadOnlyDictionary<string, ToolLocation> LocateTools(
        IReadOnlyList<CustomAction> catalog, IReadOnlyList<ToolPath> specified,
        Func<string, string?> find, Func<string, bool> exists) {

        var tools = new Dictionary<string, ToolLocation>(StringComparer.OrdinalIgnoreCase);
        foreach (string tool in ToolNames(catalog)) {
            string? given = SpecifiedPath(specified, tool);
            if (given is not null) {
                tools[tool] = new ToolLocation(tool, exists(given) ? ToolSource.Specified : ToolSource.SpecifiedMissing, given);
                continue;
            }

            tools[tool] = find(tool) is { } found
                ? new ToolLocation(tool, ToolSource.Found, found)
                : new ToolLocation(tool, ToolSource.Missing, null);
        }

        return tools;
    }

    /// <summary>The path the user gave a program; null when they gave none.</summary>
    public static string? SpecifiedPath(IReadOnlyList<ToolPath> specified, string tool) {
        return specified.LastOrDefault(p => SameTool(p.Tool, tool) && p.Path.Trim().Length > 0)?.Path.Trim();
    }

    /// <summary>The list with <paramref name="tool"/> pointed at <paramref name="path"/>; null or empty forgets it.</summary>
    public static IReadOnlyList<ToolPath> WithToolPath(IReadOnlyList<ToolPath> specified, string tool, string? path) {
        var result = specified.Where(p => !SameTool(p.Tool, tool)).ToList();
        if (!string.IsNullOrWhiteSpace(path)) {
            result.Add(new ToolPath { Tool = tool, Path = path.Trim() });
        }

        return result;
    }

    /// <summary>The programs <see cref="LocateTools"/> did not find - what the menus grey out.</summary>
    public static IReadOnlySet<string> Missing(IReadOnlyDictionary<string, ToolLocation> tools) {
        return tools.Values.Where(t => !t.IsAvailable).Select(t => t.Tool).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Whether a row's program is missing - the actions table marks it.</summary>
    public static bool NeedsTool(CustomAction action, IReadOnlyDictionary<string, ToolLocation> tools) {
        return action.RequiredTool.Length > 0
            && !(tools.TryGetValue(action.RequiredTool, out var location) && location.IsAvailable);
    }

    /// <summary>
    /// The row as it is started: a program given only as the tool's name is
    /// replaced by where the tool is, since the folders an installer uses are
    /// not always on <c>PATH</c>. A program of the row's own stays.
    /// </summary>
    public static CustomAction WithLocatedProgram(CustomAction action, IReadOnlyDictionary<string, ToolLocation> tools) {
        if (action.RequiredTool.Length == 0 || HasOwnProgram(action)) {
            return action;
        }

        return tools.TryGetValue(action.RequiredTool, out var location) && location.Executable is { } path
            ? action with { Program = path }
            : action;
    }

    /// <summary>The row runs something other than its tool's bare name - a path the user typed into it.</summary>
    public static bool HasOwnProgram(CustomAction action) {
        string program = action.Program.Trim();

        return program.Length > 0
            && !SameTool(program, action.RequiredTool)
            && !SameTool(program, action.RequiredTool + ".exe");
    }


    private static bool SameTool(string a, string b) {
        return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }

    private static string NewId() {
        return Guid.NewGuid().ToString("N");
    }
}
