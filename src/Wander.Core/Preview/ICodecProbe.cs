namespace Wander.Core.Preview;

/// <summary>
/// An extension from the Microsoft Store Windows reads a format with
/// (<see cref="StoreCodecs"/>) - the one a file needs and Windows lacks.
/// </summary>
public enum MissingCodec {
    /// <summary>Nothing is missing: the file itself is at fault.</summary>
    None,

    /// <summary>HEIF Image Extensions - what reads the container of a <c>.heic</c> / <c>.heif</c>, and of a <c>.avif</c>.</summary>
    Heif,

    /// <summary>HEVC Video Extensions - what decodes the picture inside a <c>.heic</c>, and HEVC video; paid, or free from the device's maker.</summary>
    Hevc,

    /// <summary>AV1 Video Extension - the picture inside a <c>.avif</c>, and AV1 video.</summary>
    Av1,

    /// <summary>JPEG XL Image Extension - <c>.jxl</c>; Windows 11 24H2 and later.</summary>
    JpegXl,

    /// <summary>WebP Image Extension - <c>.webp</c>; comes with Windows, and can be removed.</summary>
    Webp,

    /// <summary>Raw Image Extension - a RAW's sensor data, and the preview of a container Wander does not read.</summary>
    Raw,

    /// <summary>VP9 Video Extensions.</summary>
    Vp9,

    /// <summary>MPEG-2 Video Extension - MPEG-1 and MPEG-2 video, where Windows has no decoder of its own.</summary>
    Mpeg2,

    /// <summary>Web Media Extensions - Ogg: Vorbis sound, Theora video.</summary>
    WebMedia,
}


/// <summary>
/// Whether Windows has an extension from the Microsoft Store. The answer is
/// the system's registry of decoders, not a decode, and costs tens of
/// milliseconds - off the UI thread. An answer may be kept for a while
/// (the thumbnails ask once per picture); an extension installed meanwhile
/// is found when it runs out.
/// </summary>
public interface ICodecProbe {
    /// <summary>
    /// Whether <paramref name="codec"/> is there; <see cref="MissingCodec.None"/>
    /// always is. A probe that cannot tell says yes: "an extension is
    /// missing" must not be said on a guess.
    /// </summary>
    bool Has(MissingCodec codec);
}
