using System.Globalization;
using System.IO;
using System.Text;

namespace Wander.Harness.Host;

/// <summary>
/// One line per batch in <c>artifacts\test-runs.tsv</c>: when it ran, how
/// many passed out of how many, how long it took, and the verdict. Every
/// batch writes here - the Core tests through <c>tools\run-tests.ps1</c>,
/// <c>selfcheck</c> and every scenario through this class - so the file is
/// the one place to read a trend from. Tab-separated, header on first
/// write, appended and never rewritten; docs/QA.md, the batch-journal section.
/// </summary>
public static class RunJournal {
    public const string FileName = "test-runs.tsv";


    public static void Append(string batch, int passed, int total, TimeSpan elapsed, string status) {
        // Relative to the working directory, like the default --out: the
        // journal sits next to the run folders it describes.
        string path = Path.GetFullPath(Path.Combine("artifacts", FileName));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var sb = new StringBuilder();
        if (!File.Exists(path)) {
            sb.Append("when\tbatch\tpassed\ttotal\tseconds\tstatus\r\n");
        }
        sb.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))
            .Append('\t').Append(batch)
            .Append('\t').Append(passed.ToString(CultureInfo.InvariantCulture))
            .Append('\t').Append(total.ToString(CultureInfo.InvariantCulture))
            .Append('\t').Append(elapsed.TotalSeconds.ToString("F1", CultureInfo.InvariantCulture))
            .Append('\t').Append(status)
            .Append("\r\n");
        // UTF-8 without BOM, the same as the script writes.
        File.AppendAllText(path, sb.ToString(), new UTF8Encoding(false));
    }
}
