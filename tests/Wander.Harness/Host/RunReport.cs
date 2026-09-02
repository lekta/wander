using System.IO;
using System.Text;

namespace Wander.Harness.Host;

/// <summary>
/// report.md next to the screenshots: one row per step with outcome and
/// time, every dialog answered, the metrics summary, WARN / ERROR lines
/// and the first-screen lines from the session log.
/// </summary>
public sealed class RunReport {
    private readonly RunContext _context;
    private readonly List<string> _steps = new();
    private readonly List<string> _notes = new();
    private string? _fatal;


    public RunReport(RunContext context) {
        _context = context;
    }


    public string? Metrics { get; set; }


    public void Step(int index, string verb, string outcome, long ms, string? detail = null) {
        _steps.Add($"| {index} | `{verb}` | {outcome} | {ms} | {detail ?? ""} |");
    }

    public void Note(string text) {
        _notes.Add(text);
    }

    public void Fatal(Exception ex) {
        _fatal = ex.ToString();
    }

    public void Write(CapturingLogger log, ScriptedDialogs dialogs) {
        var sb = new StringBuilder();
        sb.AppendLine($"# {_context.Scenario.Name}");
        sb.AppendLine();
        sb.AppendLine($"- sandbox: `{_context.SandboxRoot}`");
        sb.AppendLine($"- data: `{_context.DataDir}`");
        sb.AppendLine($"- log: `{log.FilePath}`");
        sb.AppendLine($"- result: **{(_fatal is null && !_steps.Any(s => s.Contains("| FAIL |")) ? "PASS" : "FAIL")}**");
        sb.AppendLine();

        sb.AppendLine("## Steps");
        sb.AppendLine();
        sb.AppendLine("| # | step | outcome | ms | detail |");
        sb.AppendLine("|---|---|---|---|---|");
        foreach (var row in _steps) {
            sb.AppendLine(row);
        }
        sb.AppendLine();

        if (_notes.Count > 0) {
            sb.AppendLine("## Notes");
            sb.AppendLine();
            foreach (var note in _notes) {
                sb.AppendLine("- " + note);
            }
            sb.AppendLine();
        }

        sb.AppendLine("## Dialogs");
        sb.AppendLine();
        var records = dialogs.Records;
        if (records.Count == 0) {
            sb.AppendLine("(none)");
        }
        foreach (var record in records) {
            sb.AppendLine("- " + record);
        }
        sb.AppendLine();

        sb.AppendLine("## Metrics");
        sb.AppendLine();
        sb.AppendLine(Metrics ?? "(no sampler)");
        sb.AppendLine();

        var lines = log.All();
        Section(sb, "First screen", lines.Where(l => l.Message.StartsWith("First screen", StringComparison.Ordinal)));
        Section(sb, "PERF ui.stall", lines.Where(l => l.Message.Contains("ui.stall", StringComparison.Ordinal)));
        Section(sb, "WARN / ERROR", lines.Where(l => l.Level != "INFO"));

        if (_fatal is not null) {
            sb.AppendLine("## Fatal");
            sb.AppendLine();
            sb.AppendLine("```");
            sb.AppendLine(_fatal);
            sb.AppendLine("```");
        }

        File.WriteAllText(Path.Combine(_context.OutDir, "report.md"), sb.ToString());
    }


    private static void Section(StringBuilder sb, string title, IEnumerable<LogLine> lines) {
        var list = lines.ToList();
        sb.AppendLine($"## {title} ({list.Count})");
        sb.AppendLine();
        foreach (var line in list.Take(50)) {
            sb.AppendLine($"- {line.AtMs} ms {line.Level}: {line.Message}");
        }
        if (list.Count > 50) {
            sb.AppendLine($"- ... {list.Count - 50} more");
        }
        sb.AppendLine();
    }
}
