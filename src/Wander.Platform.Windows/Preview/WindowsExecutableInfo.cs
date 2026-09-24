using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using Wander.Core.FileSystem;
using Wander.Core.Preview;

namespace Wander.Platform.Windows.Preview;

/// <summary>
/// <see cref="IExecutableInfoReader"/> on Windows: the version resource
/// through <see cref="FileVersionInfo"/>, the header through Core's
/// <see cref="PeHeader"/>, the signature through <c>WinVerifyTrust</c>.
///
/// <para>
/// The check never goes to the network: revocation is not asked, and
/// certificate URLs are read from the local cache only - a preview that
/// waits on a certificate server for every arrow key is not a preview.
/// Hence "valid" means "the file is as it was signed, by a chain this
/// machine trusts", which is what the card needs to say.
/// </para>
/// </summary>
public sealed class WindowsExecutableInfo : IExecutableInfoReader {
    private const uint WtdUiNone = 2;
    private const uint WtdRevokeNone = 0;
    private const uint WtdChoiceFile = 1;
    private const uint WtdStateActionVerify = 1;
    private const uint WtdStateActionClose = 2;
    private const uint WtdCacheOnlyUrlRetrieval = 0x1000;
    private const int TrustENoSignature = unchecked((int)0x800B0100);

    private static readonly Guid _genericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");


    public ExecutableInfo? Read(string path) {
        PeFacts? pe;
        try {
            using var file = SharedRead.Open(path);
            pe = PeHeader.Read(file);
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
            return null;
        }

        FileVersionInfo? version = null;
        try {
            version = FileVersionInfo.GetVersionInfo(path);
        } catch {
            // No resource section, or not a PE file (an .msi): the card
            // shows what the header and the signature say.
        }

        return new ExecutableInfo(
            Blank(version?.FileDescription), Blank(version?.CompanyName), Blank(version?.ProductName),
            Blank(version?.FileVersion), Blank(version?.ProductVersion), Blank(version?.LegalCopyright),
            pe);
    }

    public SignatureInfo ReadSignature(string path) {
        var signature = Verify(path);
        string? signer = null;
        if (signature != SignatureState.None) {
            try {
#pragma warning disable SYSLIB0057 // The signer's certificate is what is wanted, not a certificate file.
                using var certificate = X509Certificate.CreateFromSignedFile(path);
#pragma warning restore SYSLIB0057
                using var named = new X509Certificate2(certificate);
                signer = named.GetNameInfo(X509NameType.SimpleName, false);
            } catch {
                // Signed in a way the certificate cannot be lifted out of.
            }
        }

        return new SignatureInfo(signature, signer);
    }


    private static string? Blank(string? value) {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static SignatureState Verify(string path) {
        var fileInfo = new WinTrustFileInfo {
            cbStruct = (uint)Marshal.SizeOf<WinTrustFileInfo>(),
            pcwszFilePath = path,
        };
        IntPtr filePtr = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrustFileInfo>());
        Marshal.StructureToPtr(fileInfo, filePtr, false);
        try {
            var data = new WinTrustData {
                cbStruct = (uint)Marshal.SizeOf<WinTrustData>(),
                dwUIChoice = WtdUiNone,
                fdwRevocationChecks = WtdRevokeNone,
                dwUnionChoice = WtdChoiceFile,
                pFile = filePtr,
                dwStateAction = WtdStateActionVerify,
                dwProvFlags = WtdCacheOnlyUrlRetrieval,
            };
            var action = _genericVerifyV2;
            int result = WinVerifyTrust(IntPtr.Zero, ref action, ref data);
            data.dwStateAction = WtdStateActionClose;
            WinVerifyTrust(IntPtr.Zero, ref action, ref data);

            return result switch {
                0 => SignatureState.Valid,
                TrustENoSignature => SignatureState.None,
                _ => SignatureState.Invalid,
            };
        } catch {
            return SignatureState.None;
        } finally {
            Marshal.DestroyStructure<WinTrustFileInfo>(filePtr);
            Marshal.FreeHGlobal(filePtr);
        }
    }


    [DllImport("wintrust.dll", CharSet = CharSet.Unicode)]
    private static extern int WinVerifyTrust(IntPtr hwnd, ref Guid pgActionID, ref WinTrustData pWVTData);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustFileInfo {
        public uint cbStruct;
        public string pcwszFilePath;
        public IntPtr hFile;
        public IntPtr pgKnownSubject;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustData {
        public uint cbStruct;
        public IntPtr pPolicyCallbackData;
        public IntPtr pSIPClientData;
        public uint dwUIChoice;
        public uint fdwRevocationChecks;
        public uint dwUnionChoice;
        public IntPtr pFile;
        public uint dwStateAction;
        public IntPtr hWVTStateData;
        public IntPtr pwszURLReference;
        public uint dwProvFlags;
        public uint dwUIContext;
        public IntPtr pSignatureSettings;
    }
}
