using Wander.App.Resources;
using Wander.Core.Preview;

namespace Wander.App.Preview;

/// <summary>One line of a card in the pane: what it is, and what it says.</summary>
public sealed record PreviewFact(string Label, string Value);


/// <summary>
/// The lines of a program's card (PLAN B7), in words: what the version
/// resource, the header and the signature said. A fact the file does not
/// carry is not a line - an empty "Copyright:" says nothing.
/// </summary>
internal static class ExecutableCard {
    /// <param name="signature">
    /// The signature's line: what it says (<see cref="Signature"/>), or that
    /// it is being checked, or not checked yet. Always there, so the lines
    /// below it do not move when the answer comes.
    /// </param>
    public static IReadOnlyList<PreviewFact> Facts(ExecutableInfo info, string signature) {
        var facts = new List<PreviewFact>();
        // The product version when it differs: an installer of "7-Zip 24.08"
        // is file version 24.8.0.0, and both are worth knowing only apart.
        string? version = info.FileVersion;
        if (info.ProductVersion is { } product && product != version) {
            version = version is null ? product : $"{version}  ({product})";
        }
        Add(facts, Strings.PreviewExeVersion, version);
        Add(facts, Strings.PreviewExeProduct, info.Product);
        Add(facts, Strings.PreviewExeCompany, info.Company);
        Add(facts, Strings.PreviewExePlatform, Platform(info.Pe));
        Add(facts, Strings.PreviewExeSignature, signature);
        Add(facts, Strings.PreviewExeCopyright, info.Copyright);

        return facts;
    }

    /// <summary>What the signature says, as the card's line has it.</summary>
    public static string Signature(SignatureInfo signature) {
        return signature.State switch {
            SignatureState.Valid => signature.Signer is { } signer ? string.Format(Strings.PreviewExeSignedBy, signer) : Strings.PreviewExeSigned,
            SignatureState.Invalid => Strings.PreviewExeSignatureInvalid,
            _ => Strings.PreviewExeUnsigned,
        };
    }


    /// <summary>Processor, kind of program, .NET: "x64, console program, .NET" in the card's words.</summary>
    private static string? Platform(PeFacts? pe) {
        if (pe is null) {
            return null;
        }

        var parts = new List<string>();
        // Names of processors, not words: the same in every language.
        string? machine = pe.Machine switch {
            PeMachine.X86 => "x86",
            PeMachine.X64 => "x64",
            PeMachine.Arm => "ARM",
            PeMachine.Arm64 => "ARM64",
            _ => null,
        };
        if (machine is not null) {
            parts.Add(machine);
        }
        string? kind = pe.IsLibrary ? Strings.PreviewExeLibrary : pe.Subsystem switch {
            PeSubsystem.Windows => Strings.PreviewExeWindowsApp,
            PeSubsystem.Console => Strings.PreviewExeConsoleApp,
            PeSubsystem.Native => Strings.PreviewExeNative,
            PeSubsystem.Efi => Strings.PreviewExeEfi,
            _ => null,
        };
        if (kind is not null) {
            parts.Add(kind);
        }
        if (pe.IsDotNet) {
            parts.Add(".NET");
        }

        return parts.Count == 0 ? null : string.Join(", ", parts);
    }

    private static void Add(List<PreviewFact> facts, string label, string? value) {
        if (!string.IsNullOrWhiteSpace(value)) {
            facts.Add(new PreviewFact(label, value));
        }
    }
}
