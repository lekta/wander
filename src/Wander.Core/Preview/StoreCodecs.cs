using Wander.Core.Icons;

namespace Wander.Core.Preview;

/// <summary>
/// Which extensions from the Microsoft Store Windows reads a format with,
/// and the first of them it lacks. Asked when a file did not open - the
/// pane then names the extension and opens its page instead of saying
/// "no preview" - and before the shell is asked for a thumbnail: without
/// the codec the shell answers with the type's icon and writes it into its
/// own cache.
/// </summary>
public static class StoreCodecs {
    private static readonly MissingCodec[] _heic = { MissingCodec.Heif, MissingCodec.Hevc };
    private static readonly MissingCodec[] _avif = { MissingCodec.Heif, MissingCodec.Av1 };
    private static readonly MissingCodec[] _jxl = { MissingCodec.JpegXl };
    private static readonly MissingCodec[] _webp = { MissingCodec.Webp };
    private static readonly MissingCodec[] _raw = { MissingCodec.Raw };
    private static readonly MissingCodec[] _webMedia = { MissingCodec.WebMedia };

    /// <summary>
    /// The Ogg container - the Web Media Extensions bring the reader of it
    /// along with Vorbis and Theora (their manifest, 2026-10-13); Opus
    /// inside it Windows decodes itself.
    /// </summary>
    private static readonly HashSet<string> _ogg = new(StringComparer.OrdinalIgnoreCase) {
        ".ogg", ".oga", ".ogv", ".ogx", ".ogm", ".opus",
    };


    /// <summary>
    /// The extensions a file is read with, told by its extension: pictures
    /// and the Ogg container. The container's first - without it the codec
    /// inside is never reached. Empty for a format Windows reads by itself,
    /// DNG among them: it has a decoder of its own.
    /// </summary>
    public static IReadOnlyList<MissingCodec> ForFile(string path) {
        string ext = Path.GetExtension(path);
        if (ImageFormats.Heif.Contains(ext)) {
            return _heic;
        }
        if (ImageFormats.Raw.Contains(ext)) {
            return ext.Equals(".dng", StringComparison.OrdinalIgnoreCase) ? Array.Empty<MissingCodec>() : _raw;
        }
        if (_ogg.Contains(ext)) {
            return _webMedia;
        }

        return ext.ToLowerInvariant() switch {
            ".avif" => _avif,
            ".jxl" => _jxl,
            ".webp" => _webp,
            _ => Array.Empty<MissingCodec>(),
        };
    }


    /// <summary>
    /// The extension a stream is decoded with, by its codec's name as
    /// <see cref="MediaProbe"/> gives it; <see cref="MissingCodec.None"/>
    /// for the codecs Windows decodes itself and the names it does not know.
    /// </summary>
    public static MissingCodec ForStream(string? codec) {
        return codec switch {
            "H.265" => MissingCodec.Hevc,
            "AV1" => MissingCodec.Av1,
            "VP9" => MissingCodec.Vp9,
            "MPEG-1" or "MPEG-2" => MissingCodec.Mpeg2,
            "Theora" or "Vorbis" => MissingCodec.WebMedia,
            _ => MissingCodec.None,
        };
    }


    /// <summary>The first of <paramref name="needed"/> Windows lacks, or <see cref="MissingCodec.None"/>.</summary>
    public static MissingCodec FirstMissing(IEnumerable<MissingCodec> needed, ICodecProbe probe) {
        foreach (var codec in needed) {
            if (!probe.Has(codec)) {
                return codec;
            }
        }

        return MissingCodec.None;
    }


    /// <summary>
    /// The first extension a clip or a track at <paramref name="path"/> is
    /// played with that Windows lacks, and what the words call it: the
    /// file's own (<see cref="ForFile"/>) by its extension, then each
    /// stream's by its codec. <c>(null, None)</c> when every one is there -
    /// the file itself is at fault.
    /// </summary>
    public static (string? Name, MissingCodec Missing) FirstMissingForMedia(
        string path, IEnumerable<string?> codecs, ICodecProbe probe) {
        var container = FirstMissing(ForFile(path), probe);
        if (container != MissingCodec.None) {
            return (Path.GetExtension(path).TrimStart('.').ToUpperInvariant(), container);
        }

        foreach (string? codec in codecs) {
            var needed = ForStream(codec);
            if (needed != MissingCodec.None && !probe.Has(needed)) {
                return (codec, needed);
            }
        }

        return (null, MissingCodec.None);
    }
}
