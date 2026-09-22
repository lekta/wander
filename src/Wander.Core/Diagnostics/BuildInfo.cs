using System.Diagnostics;
using System.Globalization;
using System.Reflection;

namespace Wander.Core.Diagnostics;

/// <summary>
/// Who built this, from what, and when — read off the running assembly.
///
/// <para>
/// One place rather than two because two consumers need it and they sit on
/// opposite sides of the layering: the session log is written by the
/// platform layer before the UI exists, and the «О Wander» row is written
/// by the app. Neither can call the other, so the answer lives down here,
/// where reflection over the entry assembly is the only thing it needs.
/// </para>
/// </summary>
public static class BuildInfo {
    /// <summary>
    /// The whole informational version, build metadata and all —
    /// <c>0.2.1-beta+&lt;40 hex&gt;</c>. What the crash bundle wants, and what
    /// <c>state.json</c> compares against to notice an update.
    /// </summary>
    public static string InformationalVersion { get; } = ReadInformationalVersion();

    /// <summary>Just the version: everything before the <c>+</c>.</summary>
    public static string Version { get; } = SplitVersion(InformationalVersion);

    /// <summary>
    /// This machine's build counter - the fourth number of
    /// <c>AssemblyFileVersion</c>, stamped by the csproj from
    /// <c>build-number.txt</c> (PLAN AH). 0 for a release build and for CI,
    /// where the counter does not exist: three numbers name the build there.
    /// </summary>
    public static int BuildNumber { get; } = ReadBuildNumber();

    /// <summary>
    /// The commit, cut to the five characters that fit in a line and still
    /// name one build. Empty when the SDK found no git metadata — a source
    /// drop, or a build from a tarball.
    /// </summary>
    public static string Commit { get; } = SplitCommit(InformationalVersion, 5);

    /// <summary>
    /// Build date as the csproj stamped it (<c>yyyy-MM-dd</c>, UTC). Version
    /// and commit alone do not answer "is this from before or after that
    /// fix" for anyone running off master.
    /// </summary>
    public static string BuildDate { get; } = ReadBuildDate();

    /// <summary>
    /// <c>true</c> for a Debug build of the running application.
    ///
    /// <para>
    /// Read off the entry assembly's <see cref="DebuggableAttribute"/>
    /// rather than from <c>#if DEBUG</c> here, because <c>#if</c> would
    /// answer for <em>this</em> assembly and the question is about the
    /// application. Disabled optimizations is what separates the two
    /// configurations; the debugger being attached or not is a different
    /// question and not this one.
    /// </para>
    /// </summary>
    public static bool IsDebug { get; } = ReadIsDebug();

    /// <summary>
    /// The one line that names this build:
    /// <c>v0.2.1-beta.137 D, 96e5e, 31.08.26</c>. Version, build number,
    /// configuration, commit, date — everything a bug report has to carry,
    /// short enough to sit in a menu row and in the first line of the log.
    /// The build number is left out where there is none (a release).
    /// </summary>
    public static string Line { get; } = BuildLine();


    private static string ReadInformationalVersion() {
        var asm = Assembly.GetEntryAssembly();

        return asm?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? asm?.GetName().Version?.ToString()
            ?? "unknown";
    }

    private static string SplitVersion(string informational) {
        int plus = informational.IndexOf('+');

        return plus < 0 ? informational : informational[..plus];
    }

    private static string SplitCommit(string informational, int length) {
        int plus = informational.IndexOf('+');
        if (plus < 0 || plus + 1 >= informational.Length) {
            return string.Empty;
        }

        string sha = informational[(plus + 1)..];

        return sha.Length > length ? sha[..length] : sha;
    }

    private static int ReadBuildNumber() {
        string? version = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version;
        string[] parts = version?.Split('.') ?? [];

        return parts.Length >= 4
            && int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int number)
            ? number
            : 0;
    }

    private static string ReadBuildDate() {
        var asm = Assembly.GetEntryAssembly();
        foreach (var meta in asm?.GetCustomAttributes<AssemblyMetadataAttribute>() ?? []) {
            if (meta.Key == "BuildDate") {
                return meta.Value ?? string.Empty;
            }
        }

        return string.Empty;
    }

    private static bool ReadIsDebug() {
        var debuggable = Assembly.GetEntryAssembly()?.GetCustomAttribute<DebuggableAttribute>();

        return debuggable is not null
            && debuggable.DebuggingFlags.HasFlag(DebuggableAttribute.DebuggingModes.DisableOptimizations);
    }

    private static string BuildLine() {
        var parts = new List<string> {
            $"v{Version}{(BuildNumber > 0 ? "." + BuildNumber : string.Empty)} {(IsDebug ? 'D' : 'R')}",
        };

        // Both are empty in a build with no git metadata and no stamp; the
        // line then says just the version rather than trailing separators.
        if (Commit.Length > 0) {
            parts.Add(Commit);
        }
        if (ShortDate() is { Length: > 0 } date) {
            parts.Add(date);
        }

        return string.Join(", ", parts);
    }

    /// <summary>
    /// The stamp as <c>dd.MM.yy</c>. Falls back to the raw stamp if it is
    /// not the ISO date the csproj writes — a wrong-looking date is still
    /// information, a swallowed one is not.
    /// </summary>
    private static string ShortDate() {
        if (BuildDate.Length == 0) {
            return string.Empty;
        }

        return DateTime.TryParseExact(
            BuildDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var stamped)
            ? stamped.ToString("dd.MM.yy", CultureInfo.InvariantCulture)
            : BuildDate;
    }
}
