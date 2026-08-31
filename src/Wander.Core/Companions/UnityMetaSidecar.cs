using System.Text;

namespace Wander.Core.Companions;

/// <summary>What Wander shows from a Unity <c>.meta</c> file.</summary>
/// <param name="Guid">
/// The asset GUID. This is the thing worth surfacing: scenes and prefabs
/// reference assets by GUID, so "which file is <c>3f2a…</c>" and "what is
/// this file's GUID" are questions a Unity user asks constantly.
/// </param>
/// <param name="Importer">Importer section name (<c>TextureImporter</c>, …), null when absent.</param>
/// <param name="IsFolderAsset">True for the <c>.meta</c> of a folder.</param>
public sealed record UnityMetaInfo(string? Guid, string? Importer, bool IsFolderAsset);


/// <summary>
/// Minimal reader for Unity's <c>.meta</c> sidecars. The file is YAML, but
/// only three top-level facts are wanted here and a YAML parser would be a
/// dependency bought for nothing — the keys of interest are always plain
/// unindented <c>key: value</c> lines Unity writes itself.
///
/// <para>Read-only: Wander never writes a <c>.meta</c>. Unity owns that file
/// and regenerates it on its own terms; a rewrite of ours could detach an
/// asset from every reference in every scene.</para>
/// </summary>
internal static class UnityMetaSidecar {
    public static UnityMetaInfo Read(byte[] content) {
        string text = new UTF8Encoding(false).GetString(StripBom(content));
        string? guid = null;
        string? importer = null;
        bool folder = false;

        foreach (string raw in text.Split('\n')) {
            string line = raw.TrimEnd('\r');
            // Top level only: nested mapping keys (indented) belong to the
            // importer's own settings and are none of our business.
            if (line.Length == 0 || char.IsWhiteSpace(line[0])) {
                continue;
            }

            int colon = line.IndexOf(':');
            if (colon <= 0) {
                continue;
            }

            string key = line[..colon].Trim();
            string value = line[(colon + 1)..].Trim();

            if (guid is null && key.Equals("guid", StringComparison.OrdinalIgnoreCase)) {
                guid = value.Length == 0 ? null : value;
            } else if (key.Equals("folderAsset", StringComparison.OrdinalIgnoreCase)) {
                folder = value.Equals("yes", StringComparison.OrdinalIgnoreCase)
                    || value.Equals("true", StringComparison.OrdinalIgnoreCase);
            } else if (importer is null && value.Length == 0 && key.EndsWith("Importer", StringComparison.Ordinal)) {
                importer = key;
            }
        }

        return new UnityMetaInfo(guid, importer, folder);
    }


    private static byte[] StripBom(byte[] content) {
        return content.Length >= 3 && content[0] == 0xEF && content[1] == 0xBB && content[2] == 0xBF
            ? content[3..]
            : content;
    }
}
