using System.Runtime.InteropServices;
using Wander.Core.Preview;

namespace Wander.Platform.Windows.Preview;

/// <summary>
/// The shape of whatever Media Foundation decodes - MP3, AAC and M4A, FLAC,
/// WMA, ALAC, compressed WAVs, Ogg and Opus with the Web Media extension -
/// read through <c>IMFSourceReader</c> as 32-bit floats at the track's own
/// rate and folded into a <see cref="WaveformBuilder"/> as they come, so a
/// long track costs time, not memory (decision 2026-10-27, over the WinRT
/// transcoder, which wants the whole track in memory and an async hop).
///
/// <para>
/// Called on raw pointers, a vtable slot at a time, the way
/// <c>WindowsCodecProbe</c> calls MFTEnumEx: four methods of four
/// interfaces are needed, and declaring the forty-odd slots in front of
/// them as stubs would be most of the file.
/// </para>
/// </summary>
internal static class MediaFoundationShape {
    /// <summary>MF_MT_MAJOR_TYPE.</summary>
    private static readonly Guid _majorTypeKey = new("48eba18e-f8c9-4687-bf11-0a74c9f96a8f");

    /// <summary>MF_MT_SUBTYPE.</summary>
    private static readonly Guid _subtypeKey = new("f7e34c9a-42e8-4714-b74b-cb29d72c35e5");

    /// <summary>MF_MT_AUDIO_NUM_CHANNELS.</summary>
    private static readonly Guid _channelsKey = new("37e48bf5-645e-4c5b-89de-ada9e29b696a");

    /// <summary>MF_MT_AUDIO_SAMPLES_PER_SECOND.</summary>
    private static readonly Guid _rateKey = new("5faeeae7-0290-4c31-9e8a-c534f68d9dba");

    /// <summary>MF_PD_DURATION.</summary>
    private static readonly Guid _durationKey = new("6c990d33-bb8e-477a-8598-0d5d96fcd88a");

    /// <summary>MFMediaType_Audio.</summary>
    private static readonly Guid _audioType = new("73647561-0000-0010-8000-00AA00389B71");

    /// <summary>MFAudioFormat_Float.</summary>
    private static readonly Guid _floatFormat = new("00000003-0000-0010-8000-00AA00389B71");

    private static readonly Lazy<bool> _started = new(Start);


    /// <summary>
    /// The shape of <paramref name="path"/>; null when Media Foundation
    /// cannot open or decode it, or it runs longer than
    /// <paramref name="limit"/>.
    /// </summary>
    public static Waveform? Read(string path, TimeSpan? limit, CancellationToken cancel) {
        if (!_started.Value) {
            return null;
        }

        int com = CoInitializeEx(IntPtr.Zero, COINIT_MULTITHREADED);
        IntPtr reader = IntPtr.Zero;
        try {
            if (MFCreateSourceReaderFromURL(path, IntPtr.Zero, out reader) < 0) {
                return null;
            }

            var setSelection = Slot<SetStreamSelectionFn>(reader, 4);
            setSelection(reader, MF_SOURCE_READER_ALL_STREAMS, 0);
            if (setSelection(reader, MF_SOURCE_READER_FIRST_AUDIO_STREAM, 1) < 0
                || !AskForFloats(reader)) {
                return null;
            }

            TimeSpan? declared = Duration(reader);
            if (limit is { } most && declared > most) {
                return null;
            }

            (int channels, int rate) = Format(reader);
            if (channels <= 0 || rate <= 0) {
                return null;
            }

            return Decode(reader, channels, rate, declared, limit, cancel);
        } catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) {
            return null;
        } finally {
            if (reader != IntPtr.Zero) {
                Marshal.Release(reader);
            }
            if (com is ComInitialized or ComAlreadyInitialized) {
                CoUninitialize();
            }
        }
    }


    private static Waveform? Decode(
        IntPtr reader, int channels, int rate, TimeSpan? declared, TimeSpan? limit, CancellationToken cancel) {
        var readSample = Slot<ReadSampleFn>(reader, 9);
        var builder = new WaveformBuilder();
        long mostFrames = limit is { } most ? (long)(most.TotalSeconds * rate) : long.MaxValue;
        float[] floats = [];

        while (true) {
            cancel.ThrowIfCancellationRequested();

            if (readSample(reader, MF_SOURCE_READER_FIRST_AUDIO_STREAM, 0,
                    out _, out uint flags, out _, out IntPtr sample) < 0) {
                return null;
            }

            try {
                if ((flags & MF_SOURCE_READERF_ERROR) != 0) {
                    return null;
                }
                if ((flags & MF_SOURCE_READERF_CURRENTMEDIATYPECHANGED) != 0) {
                    (channels, rate) = Format(reader);
                    if (channels <= 0 || rate <= 0) {
                        return null;
                    }
                }
                if (sample != IntPtr.Zero) {
                    floats = Samples(sample, floats, out int count);
                    builder.Add(floats.AsSpan(0, count - count % channels), channels);
                    if (builder.Frames > mostFrames) {
                        return null;
                    }
                }
                if ((flags & MF_SOURCE_READERF_ENDOFSTREAM) != 0) {
                    break;
                }
            } finally {
                if (sample != IntPtr.Zero) {
                    Marshal.Release(sample);
                }
            }
        }

        return builder.Build(declared ?? TimeSpan.FromSeconds((double)builder.Frames / rate));
    }

    /// <summary>The floats of one sample, in <paramref name="into"/> or a bigger array that replaces it.</summary>
    private static float[] Samples(IntPtr sample, float[] into, out int count) {
        count = 0;
        if (Slot<ConvertToContiguousBufferFn>(sample, 41)(sample, out IntPtr buffer) < 0) {
            return into;
        }

        try {
            if (Slot<LockFn>(buffer, 3)(buffer, out IntPtr data, out _, out uint length) < 0) {
                return into;
            }

            try {
                count = (int)(length / sizeof(float));
                if (into.Length < count) {
                    into = new float[count];
                }

                Marshal.Copy(data, into, 0, count);
            } finally {
                Slot<UnlockFn>(buffer, 4)(buffer);
            }
        } finally {
            Marshal.Release(buffer);
        }

        return into;
    }

    /// <summary>Asks the reader to decode the audio stream to floats; false when it cannot.</summary>
    private static bool AskForFloats(IntPtr reader) {
        if (MFCreateMediaType(out IntPtr type) < 0) {
            return false;
        }

        try {
            var setGuid = Slot<SetGuidFn>(type, 24);
            var major = _majorTypeKey;
            var audio = _audioType;
            var subtype = _subtypeKey;
            var floats = _floatFormat;
            if (setGuid(type, ref major, ref audio) < 0 || setGuid(type, ref subtype, ref floats) < 0) {
                return false;
            }

            return Slot<SetCurrentMediaTypeFn>(reader, 7)(reader, MF_SOURCE_READER_FIRST_AUDIO_STREAM, IntPtr.Zero, type) >= 0;
        } finally {
            Marshal.Release(type);
        }
    }

    /// <summary>Channels and rate of what the reader hands out now; zeros when it will not say.</summary>
    private static (int Channels, int Rate) Format(IntPtr reader) {
        if (Slot<GetCurrentMediaTypeFn>(reader, 6)(reader, MF_SOURCE_READER_FIRST_AUDIO_STREAM, out IntPtr type) < 0) {
            return (0, 0);
        }

        try {
            var getUInt32 = Slot<GetUInt32Fn>(type, 7);
            var channelsKey = _channelsKey;
            var rateKey = _rateKey;
            if (getUInt32(type, ref channelsKey, out uint channels) < 0 || getUInt32(type, ref rateKey, out uint rate) < 0) {
                return (0, 0);
            }

            return ((int)channels, (int)rate);
        } finally {
            Marshal.Release(type);
        }
    }

    /// <summary>The length the file declares, or null when it declares none.</summary>
    private static TimeSpan? Duration(IntPtr reader) {
        var key = _durationKey;
        if (Slot<GetPresentationAttributeFn>(reader, 12)(reader, MF_SOURCE_READER_MEDIASOURCE, ref key, out var value) < 0
            || value.vt != VT_UI8 || value.value <= 0) {
            return null;
        }

        return TimeSpan.FromTicks(value.value);
    }

    private static bool Start() {
        try {
            return MFStartup(MF_VERSION, MFSTARTUP_NOSOCKET) >= 0;
        } catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) {
            // A Windows N without the Media Feature Pack has no mfplat.dll.
            return false;
        }
    }

    private static T Slot<T>(IntPtr instance, int slot) where T : Delegate {
        IntPtr table = Marshal.ReadIntPtr(instance);

        return Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(table, slot * IntPtr.Size));
    }


    // --- Interop -------------------------------------------------------

    // Names as the Windows docs spell them, to the end of the file.
    // ReSharper disable InconsistentNaming

    /// <summary>S_OK and S_FALSE of CoInitializeEx - both to be paired with CoUninitialize.</summary>
    private const int ComInitialized = 0;
    private const int ComAlreadyInitialized = 1;
    private const uint COINIT_MULTITHREADED = 0;
    private const uint MF_VERSION = 0x00020070;
    private const uint MFSTARTUP_NOSOCKET = 1;
    private const uint MF_SOURCE_READER_MEDIASOURCE = 0xFFFFFFFF;
    private const uint MF_SOURCE_READER_ALL_STREAMS = 0xFFFFFFFE;
    private const uint MF_SOURCE_READER_FIRST_AUDIO_STREAM = 0xFFFFFFFD;
    private const uint MF_SOURCE_READERF_ERROR = 0x1;
    private const uint MF_SOURCE_READERF_ENDOFSTREAM = 0x2;
    private const uint MF_SOURCE_READERF_CURRENTMEDIATYPECHANGED = 0x20;
    private const ushort VT_UI8 = 21;

    /// <summary>Only the VT_UI8 case is read; the size is the x64 PROPVARIANT's.</summary>
    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct PROPVARIANT {
        [FieldOffset(0)] public ushort vt;
        [FieldOffset(8)] public long value;
    }

    // IMFSourceReader: 4 SetStreamSelection, 6 GetCurrentMediaType,
    // 7 SetCurrentMediaType, 9 ReadSample, 12 GetPresentationAttribute.
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int SetStreamSelectionFn(IntPtr self, uint stream, int selected);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetCurrentMediaTypeFn(IntPtr self, uint stream, out IntPtr type);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int SetCurrentMediaTypeFn(IntPtr self, uint stream, IntPtr reserved, IntPtr type);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int ReadSampleFn(
        IntPtr self, uint stream, uint control, out uint actualStream, out uint flags, out long timestamp, out IntPtr sample);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetPresentationAttributeFn(IntPtr self, uint stream, ref Guid key, out PROPVARIANT value);

    // IMFAttributes (under IMFMediaType): 7 GetUINT32, 24 SetGUID.
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetUInt32Fn(IntPtr self, ref Guid key, out uint value);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int SetGuidFn(IntPtr self, ref Guid key, ref Guid value);

    // IMFSample: 41 ConvertToContiguousBuffer (after the 30 of IMFAttributes).
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int ConvertToContiguousBufferFn(IntPtr self, out IntPtr buffer);

    // IMFMediaBuffer: 3 Lock, 4 Unlock.
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int LockFn(IntPtr self, out IntPtr data, out uint maxLength, out uint currentLength);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int UnlockFn(IntPtr self);

    [DllImport("ole32.dll", ExactSpelling = true)]
    private static extern int CoInitializeEx(IntPtr reserved, uint coInit);

    [DllImport("ole32.dll", ExactSpelling = true)]
    private static extern void CoUninitialize();

    [DllImport("mfplat.dll", ExactSpelling = true)]
    private static extern int MFStartup(uint version, uint flags);

    [DllImport("mfplat.dll", ExactSpelling = true)]
    private static extern int MFCreateMediaType(out IntPtr type);

    [DllImport("mfreadwrite.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern int MFCreateSourceReaderFromURL(string url, IntPtr attributes, out IntPtr reader);
}
