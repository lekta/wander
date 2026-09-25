using System.Runtime.InteropServices;
using Wander.Core.Preview;

namespace Wander.Platform.Windows.Preview;

/// <summary>
/// Asks Media Foundation's registry of decoders which Store extension is
/// missing (PLAN B10). Both halves of a HEIC are there as decoders once
/// installed (stand 2026-09-25): WIC's HEIF decoder is a stub in
/// <c>windowscodecs.dll</c> that looks the Store codec up by its own class
/// as an input type, and the HEVC one is an ordinary video decoder. WIC's
/// own list of decoders is no answer - it names the HEIF decoder with or
/// without the extension behind it.
/// </summary>
public sealed class WindowsCodecProbe : ICodecProbe {
    /// <summary>MFT_CATEGORY_VIDEO_DECODER.</summary>
    private static readonly Guid _videoDecoders = new("d6c02d4b-6833-45b4-971a-05a4b04bab91");

    /// <summary>MFMediaType_Video.</summary>
    private static readonly Guid _video = new("73646976-0000-0010-8000-00AA00389B71");

    /// <summary>CLSID_WICHeifDecoder - the input type the HEIF extension registers under.</summary>
    private static readonly Guid _heif = new("E9A4A80A-44FE-4DE4-8971-7150B10A5199");

    /// <summary>MFVideoFormat_HEVC.</summary>
    private static readonly Guid _hevc = new("43564548-0000-0010-8000-00AA00389B71");


    public MissingCodec MissingForHeif() {
        if (!HasDecoder(_heif)) {
            return MissingCodec.Heif;
        }

        return HasDecoder(_hevc) ? MissingCodec.None : MissingCodec.Hevc;
    }


    /// <summary>
    /// Whether any decoder takes <paramref name="input"/>. A probe that
    /// fails says yes: it is only asked why a decode failed, and "an
    /// extension is missing" must not be said on a guess.
    /// </summary>
    private static bool HasDecoder(Guid input) {
        var type = new MFT_REGISTER_TYPE_INFO { guidMajorType = _video, guidSubtype = input };
        int hr = MFTEnumEx(_videoDecoders, MFT_ENUM_FLAG_ALL, ref type, IntPtr.Zero, out IntPtr activates, out uint count);
        if (hr < 0) {
            return true;
        }

        try {
            for (int i = 0; i < count; i++) {
                IntPtr activate = Marshal.ReadIntPtr(activates, i * IntPtr.Size);
                if (activate != IntPtr.Zero) {
                    Marshal.Release(activate);
                }
            }
        } finally {
            if (activates != IntPtr.Zero) {
                Marshal.FreeCoTaskMem(activates);
            }
        }

        return count > 0;
    }


    // --- Interop -------------------------------------------------------

    /// <summary>Synchronous, asynchronous, hardware, field-of-use, local and transcode-only decoders - the Store ones are synchronous.</summary>
    private const uint MFT_ENUM_FLAG_ALL = 0x3F;

    [StructLayout(LayoutKind.Sequential)]
    private struct MFT_REGISTER_TYPE_INFO {
        public Guid guidMajorType;
        public Guid guidSubtype;
    }

    [DllImport("mfplat.dll", ExactSpelling = true)]
    private static extern int MFTEnumEx(
        Guid guidCategory, uint flags, ref MFT_REGISTER_TYPE_INFO pInputType, IntPtr pOutputType,
        out IntPtr pppMFTActivate, out uint pnumMFTActivate);
}
