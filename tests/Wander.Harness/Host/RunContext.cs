using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Wander.Harness.Host;

/// <summary>Where this run reads and writes.</summary>
public sealed record RunContext(Scenario Scenario, string SandboxRoot, string OutDir, string DataDir) {
    public string ScreenshotsDir => Path.Combine(OutDir, "screenshots");

    /// <summary><c>{sandbox}</c> and <c>{out}</c> in scenario paths.</summary>
    public string Expand(string path) {
        return path
            .Replace("{sandbox}", SandboxRoot, StringComparison.OrdinalIgnoreCase)
            .Replace("{out}", OutDir, StringComparison.OrdinalIgnoreCase);
    }
}


/// <summary>
/// A scenario file: a name, which sandbox it wants, and the steps. Each step
/// is <c>{"do": "verb", ...}</c>; the verb's parameters stay as raw JSON and
/// are read by <see cref="ScenarioRunner"/>, so a new verb is one switch arm.
/// </summary>
public sealed class Scenario {
    private static readonly JsonSerializerOptions _options = new() {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };


    public string Name { get; set; } = "scenario";

    /// <summary>Sandbox folder name under %TEMP%\wander-sandbox when none is given on the command line.</summary>
    public string Sandbox { get; set; } = "default";

    /// <summary>Profiles to generate when the sandbox does not exist yet.</summary>
    public string[] Profiles { get; set; } = { "photos", "raw" };

    /// <summary>Stop at the first failed step (default) or run everything and report.</summary>
    public bool StopOnFailure { get; set; } = true;

    /// <summary>Per-step timeout unless the step says otherwise.</summary>
    public int StepTimeoutMs { get; set; } = 30_000;

    public List<JsonElement> Steps { get; set; } = new();


    public static Scenario Load(string path) {
        string json = File.ReadAllText(path);
        var scenario = JsonSerializer.Deserialize<Scenario>(json, _options)
            ?? throw new InvalidDataException($"Not a scenario: {path}");
        if (scenario.Name == "scenario") {
            scenario.Name = Path.GetFileNameWithoutExtension(path);
        }

        return scenario;
    }
}


/// <summary>Reads step parameters with defaults; a missing key is not an error unless the verb says so.</summary>
public static class StepJson {
    public static string? Str(this JsonElement step, string key) {
        return step.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    public static string Require(this JsonElement step, string key) {
        return step.Str(key) ?? throw new InvalidDataException($"step '{step.Str("do")}' needs \"{key}\"");
    }

    public static int Int(this JsonElement step, string key, int fallback) {
        return step.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetInt32() : fallback;
    }

    public static bool? Bool(this JsonElement step, string key) {
        if (!step.TryGetProperty(key, out var value)) {
            return null;
        }

        return value.ValueKind switch {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null,
        };
    }

    public static string[] Strings(this JsonElement step, string key) {
        if (!step.TryGetProperty(key, out var value)) {
            return Array.Empty<string>();
        }
        if (value.ValueKind == JsonValueKind.String) {
            return new[] { value.GetString()! };
        }
        if (value.ValueKind == JsonValueKind.Array) {
            return value.EnumerateArray().Select(v => v.GetString() ?? "").ToArray();
        }

        return Array.Empty<string>();
    }
}
