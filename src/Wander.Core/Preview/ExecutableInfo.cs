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
/// version resource and the PE header - the file's first kilobytes. Any of
/// it may be missing; the card shows what there is. The signature is read
/// apart (<see cref="SignatureInfo"/>).
/// </summary>
public sealed record ExecutableInfo(
    string? Description, string? Company, string? Product, string? FileVersion, string? ProductVersion,
    string? Copyright, PeFacts? Pe);


/// <summary>What a program's signature says, and who signed it - the certificate's name - when it holds.</summary>
public sealed record SignatureInfo(SignatureState State, string? Signer);


/// <summary>Reads <see cref="ExecutableInfo"/> for a file; the version resource and the signature are Windows'.</summary>
public interface IExecutableInfoReader {
    /// <summary>The version resource and the header, at once. Null when the file cannot be read at all.</summary>
    ExecutableInfo? Read(string path);

    /// <summary>
    /// The signature. Checking it hashes the whole file - seconds for an
    /// installer of a gigabyte - and cannot be stopped once started: the
    /// caller decides when that is worth it (2026-09-24).
    /// </summary>
    SignatureInfo ReadSignature(string path);
}
