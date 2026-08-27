using Wander.Core.FileSystem;
using Wander.Core.Preview;

namespace Wander.Core.Search;

/// <summary>
/// Anything that is text once you look at it — source, markup, notes,
/// config, logs, <c>.fb2</c>, <c>.svg</c>, a <c>.bat</c> from DOS days.
///
/// <para>
/// Membership is decided by <see cref="TextProbe"/> on the bytes rather
/// than by a list of extensions, for the reason that class exists: the
/// same extension is text in one project and a binary blob in the next,
/// and a list would be wrong in both directions — missing the
/// <c>.recipe</c> somebody's tool writes, and mangling the <c>.asset</c>
/// that happens to be serialized binary.
/// </para>
///
/// <para>
/// The decode goes through <see cref="EncodingProbe"/>, which is the whole
/// answer to "does search handle Cyrillic". A note saved in Windows-1251
/// and a note saved in UTF-8 both become the same string here, so one
/// query finds both — matching raw bytes would have found neither unless
/// the user happened to type in the file's own codepage.
/// </para>
/// </summary>
public sealed class PlainTextExtractor : IContentExtractor {
    private readonly IFileSystem _fs;


    public PlainTextExtractor(IFileSystem fs) {
        _fs = fs;
    }


    /// <summary>
    /// Cheap: one read and one decode, both linear in the file. Caching
    /// these would spend megabytes to save a millisecond.
    /// </summary>
    public bool IsExpensive => false;


    /// <summary>
    /// Willing to try anything. The probe inside <see cref="Extract"/> is
    /// the real gate, and it needs the bytes to answer — so this extractor
    /// is registered last and catches whatever the format-specific ones
    /// declined.
    /// </summary>
    public bool CanExtract(string path) {
        return true;
    }


    public string? Extract(string path, CancellationToken token) {
        byte[] bytes;
        try {
            bytes = _fs.ReadAllBytes(path);
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException) {
            return null;
        }

        token.ThrowIfCancellationRequested();

        var sample = bytes.Length > TextProbe.SampleSize
            ? bytes.AsSpan(0, TextProbe.SampleSize)
            : bytes.AsSpan();

        return TextProbe.LooksLikeText(sample) ? EncodingProbe.Decode(bytes) : null;
    }
}
