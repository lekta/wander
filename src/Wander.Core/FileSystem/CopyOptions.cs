using Wander.Core.Operations;

namespace Wander.Core.FileSystem;

/// <summary>How a copy is to be made.</summary>
/// <param name="Verify">
/// Read the copy back once it is closed and compare it with what was read
/// from the source; a copy that differs is not kept
/// (<see cref="CopyMismatchException"/>).
/// </param>
/// <param name="SystemOnly">
/// Copy with Windows' own engine, never Wander's loop - the debug switch
/// "Копировать средствами Windows", for telling the two apart when a copy
/// is complained about.
/// </param>
public sealed record CopyOptions(bool Verify = false, bool SystemOnly = false) {
    public static readonly CopyOptions Plain = new();
}


/// <summary>What a copy went through: the engine that moved the bytes, and whether the copy was read back and matched.</summary>
/// <param name="SkippedLinks">
/// Links to folders - junctions, symbolic links - inside a copied folder,
/// left out: followed, one pointing above itself copies for ever. Null when
/// there were none.
/// </param>
public readonly record struct CopyReport(CopyEngine Engine, bool Verified, IReadOnlyList<string>? SkippedLinks = null) {
    /// <summary>Nothing was copied: a rename, an empty folder made.</summary>
    public static readonly CopyReport None = new(CopyEngine.None, false);


    /// <summary>
    /// Two copies as one - the files of a folder: verified only when both
    /// were, Windows' engine named when either went through it.
    /// </summary>
    public CopyReport And(CopyReport other) {
        var links = SkippedLinks is null ? other.SkippedLinks
            : other.SkippedLinks is null ? SkippedLinks
            : SkippedLinks.Concat(other.SkippedLinks).ToList();
        if (Engine == CopyEngine.None) {
            return other with { SkippedLinks = links };
        }
        if (other.Engine == CopyEngine.None) {
            return this with { SkippedLinks = links };
        }

        var engine = Engine == CopyEngine.System || other.Engine == CopyEngine.System ? CopyEngine.System : CopyEngine.Own;

        return new CopyReport(engine, Verified && other.Verified, links);
    }
}


/// <summary>
/// A copy read back did not match what was read from the source: a bad
/// card, a cable, a write that did not land. The copy is not kept, and the
/// item is tried again like any transient error (<see cref="TransientIo"/>).
/// </summary>
public sealed class CopyMismatchException : IOException {
    public CopyMismatchException(string source, string destination)
        : base($"The copy does not match the original ({source} -> {destination})") {
        SourcePath = source;
        DestinationPath = destination;
    }


    public string SourcePath { get; }

    public string DestinationPath { get; }
}


/// <summary>
/// A link to a folder inside a copied folder, left out of the copy - what
/// the item standing for it in the operation's record says.
/// </summary>
public sealed class LinkNotCopiedException : IOException {
    public LinkNotCopiedException(string path)
        : base($"A link to a folder is not copied: {path}") {
        LinkPath = path;
    }


    public string LinkPath { get; }
}


/// <summary>When a copy is read back to be checked - the setting "Проверять скопированное".</summary>
public enum VerifyPolicy {
    /// <summary>From flash drives, cards, phones and cameras: the copies that fail most and matter most. The default.</summary>
    Removable,

    Always,

    Never,
}


/// <summary>Whether a copy is checked: the setting and where the bytes come from.</summary>
public static class CopyVerification {
    /// <param name="sourceKind">The volume the source is on.</param>
    /// <param name="sourceIsDevice">The source is on a phone or a camera, read through the shell.</param>
    public static bool ShouldVerify(VerifyPolicy policy, VolumeKind sourceKind, bool sourceIsDevice = false) {
        return policy switch {
            VerifyPolicy.Always => true,
            VerifyPolicy.Never => false,
            _ => sourceIsDevice || sourceKind == VolumeKind.Removable,
        };
    }
}
