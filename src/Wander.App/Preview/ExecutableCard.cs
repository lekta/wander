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
    public static IReadOnlyList<PreviewFact> Facts(ExecutableInfo info) {
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
        Add(facts, Strings.PreviewExeSignature, info.Signature switch {
            SignatureState.Valid => info.Signer is { } signer ? string.Format(Strings.PreviewExeSignedBy, signer) : Strings.PreviewExeSigned,
            SignatureState.Invalid => Strings.PreviewExeSignatureInvalid,
            _ => Strings.PreviewExeUnsigned,
        });
        Add(facts, Strings.PreviewExeCopyright, info.Copyright);

        return facts;
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
