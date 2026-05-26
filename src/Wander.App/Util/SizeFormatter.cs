namespace Wander.App.Util;

public static class SizeFormatter {
    public static string Format(long bytes) {
        return bytes switch {
            < 1024 => $"{bytes} B",
            < 1024L * 1024 => $"{bytes / 1024.0:F1} KB",
            < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):F1} MB",
            < 1024L * 1024 * 1024 * 1024 => $"{bytes / (1024.0 * 1024 * 1024):F2} GB",
            _ => $"{bytes / (1024.0 * 1024 * 1024 * 1024.0):F2} TB",
        };
    }

    public static string Format(long? bytes) {
        return bytes is null ? "—" : Format(bytes.Value);
    }
}
