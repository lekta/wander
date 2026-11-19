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
/// <param name="LeftInSource">
/// A folder moved off its volume: the files that stayed at the source -
/// came or changed during the copy, would not go (<see cref="SourceSweep"/>).
/// Null when the source went whole.
/// </param>
public readonly record struct CopyReport(
    CopyEngine Engine, bool Verified, IReadOnlyList<string>? SkippedLinks = null, IReadOnlyList<SourceLeftover>? LeftInSource = null) {
    /// <summary>Nothing was copied: a rename, an empty folder made.</summary>
    public static readonly CopyReport None = new(CopyEngine.None, false);


    /// <summary>
    /// Two copies as one - the files of a folder: verified only when both
    /// were, Windows' engine named when either went through it.
    /// </summary>
    public CopyReport And(CopyReport other) {
        var links = Both(SkippedLinks, other.SkippedLinks);
        var left = Both(LeftInSource, other.LeftInSource);
        if (Engine == CopyEngine.None) {
            return other with { SkippedLinks = links, LeftInSource = left };
        }
        if (other.Engine == CopyEngine.None) {
            return this with { SkippedLinks = links, LeftInSource = left };
        }

        var engine = Engine == CopyEngine.System || other.Engine == CopyEngine.System ? CopyEngine.System : CopyEngine.Own;

        return new CopyReport(engine, Verified && other.Verified, links, left);
    }


    private static IReadOnlyList<T>? Both<T>(IReadOnlyList<T>? one, IReadOnlyList<T>? other) {
        return one is null ? other
            : other is null ? one
            : one.Concat(other).ToList();
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


/// <summary>
/// The undo of a folder moved off its volume that left files at the source
/// (<see cref="CopyReport.LeftInSource"/>), refused: the folder it would go
/// back into is there and not empty, and pouring the copy into it is a merge
/// the undo does not do. Nothing on disk was touched; the step stays, and
/// goes once the folder is cleared.
/// </summary>
public sealed class SourceNotEmptyException : IOException {
    public SourceNotEmptyException(string folder, int left)
        : base($"Not moved back: {folder} is there and not empty, {left} left in it by the move") {
        Folder = folder;
        Left = left;
    }


    public string Folder { get; }

    /// <summary>How many of the files the move left behind are still there.</summary>
    public int Left { get; }
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
