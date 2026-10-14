using System.Runtime.InteropServices;
using Wander.Core.Preview;

namespace Wander.Platform.Windows.Preview;

/// <summary>
/// Asks Media Foundation's registry of decoders which Store extension is
/// missing. Every image extension is there as a decoder once installed
/// (stand 2026-09-25 for HEIF, 2026-10-13 for WebP, RAW and JPEG XL): WIC's
/// decoder of the format is a stub in <c>windowscodecs.dll</c> that looks
/// the Store codec up by its own class as an input type; the video ones
/// are ordinary video decoders. WIC's own list of decoders is no answer -
/// it names the stub with or without the extension behind it.
///
/// <para>
/// An answer is kept for <see cref="KeptMs"/>: a question costs about 27 ms
/// (stand 2026-10-13), and the thumbnails ask it once per picture.
/// </para>
/// </summary>
public sealed class WindowsCodecProbe : ICodecProbe {
    private const long KeptMs = 60_000;

    /// <summary>MFT_CATEGORY_VIDEO_DECODER.</summary>
    private static readonly Guid _videoDecoders = new("d6c02d4b-6833-45b4-971a-05a4b04bab91");

    /// <summary>MFT_CATEGORY_AUDIO_DECODER.</summary>
    private static readonly Guid _audioDecoders = new("9ea73fb4-ef7a-4559-8d5d-719d8f0426c7");

    /// <summary>MFMediaType_Video.</summary>
    private static readonly Guid _video = new("73646976-0000-0010-8000-00AA00389B71");

    /// <summary>MFMediaType_Audio.</summary>
    private static readonly Guid _audio = new("73647561-0000-0010-8000-00AA00389B71");

    private readonly Dictionary<MissingCodec, (bool Has, long At)> _answers = new();
    private readonly Lock _lock = new();


    public bool Has(MissingCodec codec) {
        long now = Environment.TickCount64;
        lock (_lock) {
            if (_answers.TryGetValue(codec, out var kept) && now - kept.At < KeptMs) {
                return kept.Has;
            }
        }

        bool has = codec switch {
            // CLSID_WICHeifDecoder, the WIC stubs of WebP, RAW and JPEG XL -
            // the input types their extensions register under.
            MissingCodec.Heif => HasDecoder(_videoDecoders, _video, new Guid("E9A4A80A-44FE-4DE4-8971-7150B10A5199")),
            MissingCodec.Webp => HasDecoder(_videoDecoders, _video, new Guid("7693E886-51C9-4070-8419-9F70738EC8FA")),
            MissingCodec.Raw => HasDecoder(_videoDecoders, _video, new Guid("41945702-8302-44A6-9445-AC98E8AFA086")),
            MissingCodec.JpegXl => HasDecoder(_videoDecoders, _video, new Guid("FC6CEECE-AEF5-4A23-96EC-5984FFB486D9")),
            // MFVideoFormat_HEVC, _AV1, _VP90, _MPEG2; MFAudioFormat_Vorbis.
            MissingCodec.Hevc => HasDecoder(_videoDecoders, _video, new Guid("43564548-0000-0010-8000-00AA00389B71")),
            MissingCodec.Av1 => HasDecoder(_videoDecoders, _video, new Guid("31305641-0000-0010-8000-00AA00389B71")),
            MissingCodec.Vp9 => HasDecoder(_videoDecoders, _video, new Guid("30395056-0000-0010-8000-00AA00389B71")),
            MissingCodec.Mpeg2 => HasDecoder(_videoDecoders, _video, new Guid("E06D8026-DB46-11CF-B4D1-00805F6CBBEA")),
            MissingCodec.WebMedia => HasDecoder(_audioDecoders, _audio, new Guid("8D2FD10B-5841-4A6B-8905-588FEC1ADED9")),
            _ => true,
        };
        lock (_lock) {
            _answers[codec] = (has, now);
        }

        return has;
    }


    /// <summary>
    /// Whether any decoder in <paramref name="category"/> takes
    /// <paramref name="input"/>. A probe that fails says yes: it is only
    /// asked why a decode failed or may fail, and "an extension is missing"
    /// must not be said on a guess. A Windows N without the Media Feature
    /// Pack has no <c>mfplat.dll</c> at all.
    /// </summary>
    private static bool HasDecoder(Guid category, Guid major, Guid input) {
        var type = new MFT_REGISTER_TYPE_INFO { guidMajorType = major, guidSubtype = input };
        int hr;
        IntPtr activates;
        uint count;
        try {
            hr = MFTEnumEx(category, MFT_ENUM_FLAG_ALL, ref type, IntPtr.Zero, out activates, out count);
        } catch (DllNotFoundException) {
            return true;
        }
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

    // Names as the Windows docs spell them, to the end of the file.
    // ReSharper disable InconsistentNaming

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
