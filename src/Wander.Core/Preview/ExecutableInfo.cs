namespace Wander.Core.Preview;

/// <summary>Whether a program carries an Authenticode signature, and whether it holds.</summary>
public enum SignatureState {
    /// <summary>No signature in the file. System files are often signed in a catalog instead.</summary>
    None,
    Valid,

    /// <summary>A signature is there and does not hold: the file changed, the certificate is not trusted.</summary>
    Invalid,
}


/// <summary>
/// What the preview card says about a program or a library (PLAN B7): the
/// version resource, the PE header, the signature. Any of it may be
/// missing; the card shows what there is.
/// </summary>
/// <param name="Signer">Who signed it - the certificate's name - when it is signed.</param>
public sealed record ExecutableInfo(
    string? Description, string? Company, string? Product, string? FileVersion, string? ProductVersion,
    string? Copyright, PeFacts? Pe, SignatureState Signature, string? Signer);


/// <summary>Reads <see cref="ExecutableInfo"/> for a file; the version resource and the signature are Windows'.</summary>
public interface IExecutableInfoReader {
    /// <summary>Null when the file cannot be read at all.</summary>
    ExecutableInfo? Read(string path);
}
