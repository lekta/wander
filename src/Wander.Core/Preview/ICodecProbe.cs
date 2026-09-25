namespace Wander.Core.Preview;

/// <summary>A Store extension Windows lacks to open a picture format (PLAN B10).</summary>
public enum MissingCodec {
    /// <summary>Nothing is missing: the file itself is at fault.</summary>
    None,

    /// <summary>HEIF Image Extensions - what reads the container of a <c>.heic</c> / <c>.heif</c>.</summary>
    Heif,

    /// <summary>HEVC Video Extensions - what decodes the picture inside a <c>.heic</c>; paid, or free from the device's maker.</summary>
    Hevc,
}


/// <summary>
/// Which extension from the Microsoft Store Windows lacks for a format it
/// reads through one (PLAN B10). Asked only after a picture failed to
/// decode: the answer is the system's registry of decoders, not a decode,
/// and costs tens of milliseconds - off the UI thread. Not kept between
/// asks: an extension installed meanwhile is found the next time.
/// </summary>
public interface ICodecProbe {
    /// <summary>What a HEIF picture (<c>.heic</c>, <c>.heif</c>) needs that is not there.</summary>
    MissingCodec MissingForHeif();
}
