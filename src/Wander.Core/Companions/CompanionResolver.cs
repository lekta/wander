using Wander.Core.FileSystem;

namespace Wander.Core.Companions;

/// <summary>
/// Decides which files in a folder are companions ("integrated items") of
/// which other files, and what a companion is called once its main file is
/// renamed. Pure matching over names — no I/O beyond the
/// <see cref="IFileSystem.FileExists"/> probes in
/// <see cref="FindCompanions"/>, so the interesting half is testable
/// without touching a disk.
///
/// <para>
/// The rule set is data, not code: <see cref="Default"/> registers Unity
/// <c>.meta</c> and RawTherapee <c>.pp3</c>, and every further format is
/// one more <see cref="CompanionRule"/>.
/// </para>
/// </summary>
public sealed class CompanionResolver {
    /// <summary>Formats Wander understands out of the box.</summary>
    public static readonly CompanionResolver Default = new(new[] {
        new CompanionRule(".meta", CompanionNaming.Appended, "Unity .meta"),
        new CompanionRule(".pp3", CompanionNaming.Appended, "RawTherapee .pp3"),
    });


    private readonly IReadOnlyList<CompanionRule> _rules;


    public CompanionResolver(IReadOnlyList<CompanionRule> rules) {
        _rules = rules;
    }


    public IReadOnlyList<CompanionRule> Rules => _rules;


    /// <summary>The rule a companion path belongs to, or null if it isn't one.</summary>
    public CompanionRule? RuleFor(string path) {
        string name = Path.GetFileName(path);

        return _rules.FirstOrDefault(r => r.TryMatch(name, out _));
    }


    /// <summary>
    /// Folder listing with companions folded into their main files: the
    /// companions drop out of the list and each main file carries their
    /// paths in <see cref="FileSystemEntry.Companions"/>.
    ///
    /// <para>
    /// A companion whose main file isn't in <paramref name="entries"/> stays
    /// visible as an ordinary file — an orphaned <c>.meta</c> is exactly the
    /// thing a user needs to see, and a sidecar next to a hidden-by-filter
    /// main file must not vanish silently too.
    /// </para>
    /// </summary>
    public IReadOnlyList<FileSystemEntry> Collapse(IReadOnlyList<FileSystemEntry> entries) {
        var byName = new Dictionary<string, FileSystemEntry>(entries.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var e in entries) {
            byName[e.Name] = e;
        }

        Dictionary<string, FileSystemEntry>? mainOf = null;
        foreach (var e in entries) {
            // A directory is never a sidecar (it can well *own* one: Unity
            // writes Scripts.meta next to the folder Scripts).
            if (e.Kind != EntryKind.File) {
                continue;
            }
            if (FindMain(e, entries, byName) is { } main) {
                mainOf ??= new Dictionary<string, FileSystemEntry>(StringComparer.OrdinalIgnoreCase);
                mainOf[e.Name] = main;
            }
        }

        if (mainOf is null) {
            return entries;
        }

        // A companion of a companion (a.png.meta + a.png.meta.pp3) would
        // otherwise be folded into a row that is itself folded away, i.e.
        // disappear entirely. Chains are left expanded instead.
        var companionsOf = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (companionName, main) in mainOf) {
            if (mainOf.ContainsKey(main.Name)) {
                continue;
            }
            if (!companionsOf.TryGetValue(main.Name, out var list)) {
                list = new List<string>();
                companionsOf[main.Name] = list;
            }
            list.Add(byName[companionName].FullPath);
        }

        if (companionsOf.Count == 0) {
            return entries;
        }

        // Dictionary order is not a promise; the footer lists companions in
        // this order, so make it a stable one.
        foreach (var list in companionsOf.Values) {
            list.Sort(StringComparer.OrdinalIgnoreCase);
        }

        var folded = new HashSet<string>(
            companionsOf.SelectMany(kv => kv.Value).Select(Path.GetFileName)!,
            StringComparer.OrdinalIgnoreCase);

        var result = new List<FileSystemEntry>(entries.Count - folded.Count);
        foreach (var e in entries) {
            if (folded.Contains(e.Name)) {
                continue;
            }
            result.Add(companionsOf.TryGetValue(e.Name, out var list)
                ? e with { Companions = list }
                : e);
        }

        return result;
    }


    /// <summary>
    /// Companions of <paramref name="mainPath"/> that actually exist on
    /// disk. This is the operation-side entry point: move / copy / delete /
    /// drag expand their path list through it.
    /// </summary>
    public IReadOnlyList<string> FindCompanions(string mainPath, IFileSystem fs) {
        string? dir = Path.GetDirectoryName(mainPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrEmpty(dir)) {
            return Array.Empty<string>();
        }

        string name = Path.GetFileName(mainPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        List<string>? found = null;
        foreach (var rule in _rules) {
            string candidate = Path.Combine(dir, rule.CompanionNameFor(name));
            if (string.Equals(candidate, mainPath, StringComparison.OrdinalIgnoreCase) || !fs.FileExists(candidate)) {
                continue;
            }
            found ??= new List<string>();
            if (!found.Contains(candidate, StringComparer.OrdinalIgnoreCase)) {
                found.Add(candidate);
            }
        }

        return (IReadOnlyList<string>?)found ?? Array.Empty<string>();
    }


    /// <summary>
    /// Every rename a "rename this file" gesture really implies: the main
    /// file first, then each existing companion under its new name. Handing
    /// the whole plan to <see cref="FileOperationService.RenameMany"/> is
    /// what makes the group land as a single undo step.
    /// </summary>
    public IReadOnlyList<(string Path, string NewName)> RenamePlan(string mainPath, string newMainName, IFileSystem fs) {
        var plan = new List<(string, string)> { (mainPath, newMainName) };
        foreach (string companion in FindCompanions(mainPath, fs)) {
            var rule = RuleFor(companion);
            if (rule is not null) {
                plan.Add((companion, rule.CompanionNameFor(newMainName)));
            }
        }

        return plan;
    }


    private FileSystemEntry? FindMain(
        FileSystemEntry candidate,
        IReadOnlyList<FileSystemEntry> entries,
        Dictionary<string, FileSystemEntry> byName) {

        foreach (var rule in _rules) {
            if (!rule.TryMatch(candidate.Name, out string key)) {
                continue;
            }

            if (rule.Naming == CompanionNaming.Appended) {
                if (byName.TryGetValue(key, out var main) && !ReferenceEquals(main, candidate)) {
                    return main;
                }
                continue;
            }

            // Replaced: the key is a stem, so several files could claim it
            // (IMG.CR2 and IMG.jpg both stem to "IMG"). An ambiguous sidecar
            // is left alone rather than attached to a guess.
            FileSystemEntry? single = null;
            foreach (var e in entries) {
                if (ReferenceEquals(e, candidate) || e.Kind != EntryKind.File) {
                    continue;
                }
                if (!string.Equals(Path.GetFileNameWithoutExtension(e.Name), key, StringComparison.OrdinalIgnoreCase)) {
                    continue;
                }
                if (single is not null) {
                    single = null;
                    break;
                }
                single = e;
            }
            if (single is not null) {
                return single;
            }
        }

        return null;
    }
}
