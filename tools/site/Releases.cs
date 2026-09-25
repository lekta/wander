using System.ComponentModel;
using System.Diagnostics;

namespace Wander.Site;

/// <summary>A released version: its <c>v*</c> tag, the day it was tagged, whether the tag has a guide.</summary>
internal sealed record Release(string Tag, string Date, bool HasGuide) {
    public string Version => Tag[1..];
}


/// <summary>
/// The releases, newest first, straight from git: every <c>v*</c> tag is a
/// release (release.yml publishes one per tag). The newest is what the
/// download button offers; the site is rebuilt by a <c>site-*</c> tag after
/// a release, the network is never asked.
/// </summary>
internal static class Releases {
    public static List<Release> Read(string root, List<string> errors) {
        string? tags = Git(root, errors, null,
            "for-each-ref", "--sort=-v:refname", "--format=%(refname:short)%09%(creatordate:short)", "refs/tags/v*");
        if (tags is null) {
            return new();
        }

        var rows = tags.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.Split('\t'))
            .ToList();
        if (rows.Count == 0) {
            errors.Add("git: no v* tags - the download button has no release to offer");

            return new();
        }

        // One process for every tag: "<object> missing" when the tag predates the guide.
        string input = string.Concat(rows.Select(row => row[0] + ":docs/GUIDE.md\n"));
        string? found = Git(root, errors, input, "cat-file", "--batch-check");
        if (found is null) {
            return new();
        }

        var missing = found.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => line.EndsWith(" missing", StringComparison.Ordinal))
            .Select(line => line[..line.IndexOf(':', StringComparison.Ordinal)])
            .ToHashSet(StringComparer.Ordinal);

        return rows.Select(row => new Release(row[0], row[1], !missing.Contains(row[0]))).ToList();
    }


    private static string? Git(string root, List<string> errors, string? input, params string[] args) {
        var info = new ProcessStartInfo("git") {
            WorkingDirectory = root,
            UseShellExecute = false,
            RedirectStandardInput = input is not null,
            RedirectStandardOutput = true,
        };
        foreach (string arg in args) {
            info.ArgumentList.Add(arg);
        }

        try {
            using var git = Process.Start(info)!;
            if (input is not null) {
                git.StandardInput.Write(input);
                git.StandardInput.Close();
            }
            string output = git.StandardOutput.ReadToEnd();
            git.WaitForExit();
            if (git.ExitCode != 0) {
                errors.Add($"git {args[0]}: exit code {git.ExitCode}");

                return null;
            }

            return output;
        } catch (Win32Exception ex) {
            errors.Add("git: " + ex.Message);

            return null;
        }
    }
}
