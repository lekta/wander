using System.Text;

namespace Wander.Core.Folders;

/// <summary>
/// What a folder's own <c>desktop.ini</c> says about it, as far as Wander
/// cares (H1, decision B17): the type Explorer was told the folder is -
/// <c>[ViewState] FolderType=Pictures</c> or <c>Photos</c> - taken as a hint
/// that it holds snapshots, for the automatic gallery. Read, never written:
/// what Wander remembers about folders is its own book (Z1).
/// </summary>
public static class DesktopIni {
    /// <summary>The file's name in a folder.</summary>
    public const string FileName = "desktop.ini";


    /// <summary>The file, as read off the disk, says the folder is one of pictures.</summary>
    public static bool SaysPictures(byte[] content) {
        return SaysPictures(Decode(content));
    }

    /// <summary>The file's text says the folder is one of pictures.</summary>
    public static bool SaysPictures(string text) {
        string? type = ValueOf(text, "ViewState", "FolderType");

        return type is not null
            && (type.Equals("Pictures", StringComparison.OrdinalIgnoreCase)
                || type.Equals("Photos", StringComparison.OrdinalIgnoreCase));
    }


    /// <summary>
    /// The file, as read off the disk, gives the folder an icon of its own -
    /// <c>[.ShellClassInfo] IconResource</c>, or the older <c>IconFile</c>.
    /// Such a folder keeps the shell's picture (PLAN F1, decision
    /// 2026-11-06): somebody chose that icon.
    /// </summary>
    public static bool SetsIcon(byte[] content) {
        return SetsIcon(Decode(content));
    }

    /// <summary>The file's text gives the folder an icon of its own.</summary>
    public static bool SetsIcon(string text) {
        return !string.IsNullOrEmpty(ValueOf(text, ".ShellClassInfo", "IconResource"))
            || !string.IsNullOrEmpty(ValueOf(text, ".ShellClassInfo", "IconFile"));
    }


    /// <summary>The first value of <paramref name="key"/> in <paramref name="section"/>, trimmed; null when it is not there.</summary>
    private static string? ValueOf(string text, string section, string key) {
        bool inSection = false;
        foreach (string raw in text.Split('\n')) {
            string line = raw.Trim();
            if (line.Length == 0 || line[0] == ';') {
                continue;
            }
            if (line[0] == '[') {
                int end = line.IndexOf(']');
                inSection = end > 1 && line[1..end].Trim().Equals(section, StringComparison.OrdinalIgnoreCase);

                continue;
            }

            int equals = line.IndexOf('=');
            if (!inSection || equals <= 0 || !line[..equals].Trim().Equals(key, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            return line[(equals + 1)..].Trim();
        }

        return null;
    }


    /// <summary>
    /// Explorer writes the file in UTF-16 with its mark, other programs in
    /// the ANSI code page or UTF-8; the keys are ASCII either way.
    /// </summary>
    private static string Decode(byte[] content) {
        if (content.Length >= 2 && content[0] == 0xFF && content[1] == 0xFE) {
            return Encoding.Unicode.GetString(content, 2, content.Length - 2);
        }
        if (content.Length >= 3 && content[0] == 0xEF && content[1] == 0xBB && content[2] == 0xBF) {
            return Encoding.UTF8.GetString(content, 3, content.Length - 3);
        }

        return Encoding.Latin1.GetString(content);
    }
}
