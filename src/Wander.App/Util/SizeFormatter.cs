namespace Wander.App.Util;

public static class SizeFormatter {
    public static string Format(long bytes) {
        return bytes switch {
            < 1024 => $"{bytes} B",
            < 1024L * 1024 => Scaled(bytes / 1024.0, "KB", 1),
            < 1024L * 1024 * 1024 => Scaled(bytes / (1024.0 * 1024), "MB", 1),
            < 1024L * 1024 * 1024 * 1024 => Scaled(bytes / (1024.0 * 1024 * 1024), "GB", 2),
            _ => Scaled(bytes / (1024.0 * 1024 * 1024 * 1024.0), "TB", 2),
        };
    }

    public static string Format(long? bytes) {
        return bytes is null ? "—" : Format(bytes.Value);
    }


    /// <summary>
    /// Decimals only while there is one digit before them: "4,7 MB" says
    /// something the "5" would not, "572,1 MB" says nothing "572" does not
    /// (2026-09-22). Checked after rounding, so 9.96 is "10", not "10,0".
    /// </summary>
    private static string Scaled(double value, string unit, int decimals) {
        return Math.Round(value, decimals) < 10
            ? value.ToString("F" + decimals) + " " + unit
            : value.ToString("F0") + " " + unit;
    }
}
