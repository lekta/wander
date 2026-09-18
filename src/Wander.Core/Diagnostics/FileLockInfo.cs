namespace Wander.Core.Diagnostics;

/// <summary>A process that holds a file open, as the user is told about it.</summary>
public sealed record FileLockInfo(int ProcessId, string ProcessName) {
    /// <summary>Several holders on one line: "Windows PowerShell (PID 31424), Word (PID 812)".</summary>
    public static string Describe(IEnumerable<FileLockInfo> lockers) {
        return string.Join(", ", lockers.Select(l => $"{l.ProcessName} (PID {l.ProcessId})"));
    }
}
