using Wander.Core.FileSystem;
using Wander.Core.Icons;

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
        // darktable appends: IMG_1234.CR2 -> IMG_1234.CR2.xmp. Ahead of the
        // replaced rule on purpose - by stem, IMG.CR2.xmp would go to
        // IMG.CR2.pp3 (stem "IMG.CR2") and stay on screen as a sidecar of
        // a sidecar. Its duplicates (IMG_1234_01.CR2.xmp) come with it.
        new CompanionRule(".xmp", CompanionNaming.Appended, "darktable .xmp", Versions: true),
        // XMP replaces the extension: IMG_1234.CR2 -> IMG_1234.xmp. Adobe
        // and exiftool write it, which makes it the widest reaching
        // sidecar of them all.
        new CompanionRule(".xmp", CompanionNaming.Replaced, "XMP"),
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
    /// The rule that names <paramref name="companionPath"/> after
    /// <paramref name="mainName"/>, or null. The suffix alone does not
    /// say it: IMG.CR2.xmp and IMG.xmp are both XMP, and only the main
    /// file's name tells which one renames to NEW.CR2.xmp and which to
    /// NEW.xmp.
    /// </summary>
    public CompanionRule? RuleFor(string companionPath, string mainName) {
        string name = Path.GetFileName(companionPath);

        return _rules.FirstOrDefault(r => string.Equals(r.CompanionNameFor(mainName), name, StringComparison.OrdinalIgnoreCase));
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
            // IMG.xmp next to IMG.jpg and IMG.CR2 is the RAW's (see
            // Owner); the JPEG must not take it along.
            if (rule.Naming == CompanionNaming.Replaced && !ImageFormats.IsRaw(name) && RawSiblingExists(dir, name, fs)) {
                continue;
            }
            found ??= new List<string>();
            if (!found.Contains(candidate, StringComparer.OrdinalIgnoreCase)) {
                found.Add(candidate);
            }
        }

        // Versions are an editor's of a picture; one folder query per picture
        // dropped or pasted, none for everything else.
        foreach (var rule in _rules.Where(r => r.Versions && ImageFormats.IsImage(name))) {
            string pattern = Path.GetFileNameWithoutExtension(name) + "_*" + Path.GetExtension(name) + rule.Suffix;
            foreach (string version in fs.FileNamesLike(dir, pattern)) {
                // IMG_01.CR2.xmp beside a file IMG_01.CR2 is that file's own.
                if (rule.TryVersion(version, out string of, out _)
                    && string.Equals(of, name, StringComparison.OrdinalIgnoreCase)
                    && rule.TryMatch(version, out string exact) && !fs.FileExists(Path.Combine(dir, exact))) {
                    found ??= new List<string>();
                    found.Add(Path.Combine(dir, version));
                }
            }
        }

        return (IReadOnlyList<string>?)found ?? Array.Empty<string>();
    }


    /// <summary>
    /// Folds a flat list of paths back into groups: each path that is a
    /// companion of another path <em>in the same list</em> joins it instead
    /// of standing alone.
    ///
    /// <para>
    /// This is what turns a clipboard or a drop payload — both of which are
    /// only ever a list of strings — back into the shape a batch operation
    /// wants, so a conflict is asked about once per group. It also does the
    /// right thing for a drag out of Explorer where the user selected the
    /// asset and its sidecar by hand.
    /// </para>
    /// </summary>
    public IReadOnlyList<BatchGroup> Group(IReadOnlyList<string> paths) {
        // Companions live next to their main file, so matching is per folder.
        var byFolder = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        foreach (string path in paths) {
            string folder = Path.GetDirectoryName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) ?? "";
            if (!byFolder.TryGetValue(folder, out var names)) {
                names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                byFolder[folder] = names;
            }
            names[Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))] = path;
        }

        var mainOf = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var names in byFolder.Values) {
            foreach (var (name, path) in names) {
                if (MainForCompanion(name, names) is { } main && !string.Equals(main, path, StringComparison.OrdinalIgnoreCase)) {
                    mainOf[path] = main;
                }
            }
        }

        var companionsOf = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (companion, main) in mainOf) {
            // Same chain rule as Collapse: a companion of a companion stays
            // its own item rather than disappearing into a nested group.
            if (mainOf.ContainsKey(main)) {
                continue;
            }
            if (!companionsOf.TryGetValue(main, out var list)) {
                list = new List<string>();
                companionsOf[main] = list;
            }
            list.Add(companion);
        }

        var groups = new List<BatchGroup>(paths.Count);
        foreach (string path in paths) {
            if (companionsOf.ContainsKey(path)) {
                var list = companionsOf[path];
                list.Sort(StringComparer.OrdinalIgnoreCase);
                groups.Add(new BatchGroup(path, list));
            } else if (!mainOf.ContainsKey(path) || mainOf.ContainsKey(mainOf[path])) {
                groups.Add(BatchGroup.Single(path));
            }
        }

        return groups;
    }


    /// <summary>
    /// Every rename a "rename this file" gesture really implies: the main
    /// file first, then each existing companion under its new name. Handing
    /// the whole plan to <see cref="FileOperationService.RenameMany"/> is
    /// what makes the group land as a single undo step.
    /// </summary>
    public IReadOnlyList<(string Path, string NewName)> RenamePlan(string mainPath, string newMainName, IFileSystem fs) {
        return RenamePlan(mainPath, newMainName, FindCompanions(mainPath, fs));
    }


    /// <summary>
    /// The same plan for companions the caller already knows about — the
    /// folder listing puts them on the row, so the common case needs no
    /// disk access at all.
    /// </summary>
    public IReadOnlyList<(string Path, string NewName)> RenamePlan(
        string mainPath, string newMainName, IReadOnlyList<string>? companions) {

        var plan = new List<(string, string)> { (mainPath, newMainName) };
        string mainName = Path.GetFileName(mainPath);
        foreach (string companion in companions ?? Array.Empty<string>()) {
            if (RuleFor(companion, mainName) is { } rule) {
                plan.Add((companion, rule.CompanionNameFor(newMainName)));
            } else if (VersionOf(Path.GetFileName(companion), mainName) is { } version) {
                plan.Add((companion, version.Rule.VersionNameFor(newMainName, version.Number)));
            }
        }

        return plan;
    }


    /// <summary>
    /// The path in <paramref name="siblings"/> that <paramref name="name"/>
    /// is a companion of, or null. Shares the ambiguity rule with
    /// <see cref="Collapse"/>: a stem-matched sidecar with more than one
    /// candidate belongs to none of them.
    /// </summary>
    private string? MainForCompanion(string name, Dictionary<string, string> siblings) {
        foreach (var rule in _rules) {
            if (!rule.TryMatch(name, out string key)) {
                continue;
            }

            if (rule.Naming == CompanionNaming.Appended) {
                if (siblings.TryGetValue(key, out string? main)) {
                    return main;
                }
                if (rule.TryVersion(name, out string of, out _) && siblings.TryGetValue(of, out main)) {
                    return main;
                }
                continue;
            }

            var candidates = new List<string>();
            foreach (var (candidate, path) in siblings) {
                if (string.Equals(candidate, name, StringComparison.OrdinalIgnoreCase)) {
                    continue;
                }
                if (string.Equals(Path.GetFileNameWithoutExtension(candidate), key, StringComparison.OrdinalIgnoreCase)) {
                    candidates.Add(path);
                }
            }
            if (Owner(candidates, path => path) is { } owner) {
                return owner;
            }
        }

        return null;
    }

    /// <summary>
    /// Which of the files sharing a stem a replaced-extension sidecar
    /// belongs to. One candidate: that one. Several: the RAW among them,
    /// if there is exactly one - a folder holding IMG.CR2 and IMG.jpg is
    /// the camera's RAW+JPEG pair or a JPEG developed from the RAW, and the
    /// XMP is the RAW's in both cases. Otherwise nobody's: attaching a
    /// sidecar to a guess would hide it under the wrong file.
    /// </summary>
    private static T? Owner<T>(IReadOnlyList<T> candidates, Func<T, string> nameOf) where T : class {
        if (candidates.Count == 1) {
            return candidates[0];
        }
        if (candidates.Count == 0) {
            return null;
        }

        var raw = candidates.Where(c => ImageFormats.IsRaw(nameOf(c))).ToList();

        return raw.Count == 1 ? raw[0] : null;
    }

    /// <summary>The numbered-version rule and number that make <paramref name="name"/> a companion of <paramref name="mainName"/>, or null.</summary>
    private (CompanionRule Rule, string Number)? VersionOf(string name, string mainName) {
        foreach (var rule in _rules) {
            if (rule.TryVersion(name, out string of, out string number) && string.Equals(of, mainName, StringComparison.OrdinalIgnoreCase)) {
                return (rule, number);
            }
        }

        return null;
    }

    /// <summary>A RAW file with the same stem as <paramref name="name"/> exists in <paramref name="dir"/>.</summary>
    private static bool RawSiblingExists(string dir, string name, IFileSystem fs) {
        string stem = Path.GetFileNameWithoutExtension(name);
        foreach (string extension in ImageFormats.Raw) {
            if (fs.FileExists(Path.Combine(dir, stem + extension))) {
                return true;
            }
        }

        return false;
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
                // darktable's duplicate, IMG_01.CR2.xmp: no IMG_01.CR2 to own it.
                if (rule.TryVersion(candidate.Name, out string of, out _)
                    && byName.TryGetValue(of, out main) && main.Kind == EntryKind.File) {
                    return main;
                }
                continue;
            }

            // Replaced: the key is a stem, so several files could claim it
            // (IMG.CR2 and IMG.jpg both stem to "IMG") - see Owner.
            var candidates = new List<FileSystemEntry>();
            foreach (var e in entries) {
                if (ReferenceEquals(e, candidate) || e.Kind != EntryKind.File) {
                    continue;
                }
                if (string.Equals(Path.GetFileNameWithoutExtension(e.Name), key, StringComparison.OrdinalIgnoreCase)) {
                    candidates.Add(e);
                }
            }
            if (Owner(candidates, e => e.Name) is { } owner) {
                return owner;
            }
        }

        return null;
    }
}
