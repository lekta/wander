namespace Wander.Core.Preview;

/// <summary>
/// The names and the codec headers the three container readers share:
/// Windows' four-character codes and wave format tags (AVI, and Matroska's
/// compatibility modes), and the configuration records MP4 and Matroska
/// both carry for HEVC, AV1 and AAC.
/// </summary>
internal static class MediaCodecs {
    /// <summary>The name for a video four-character code; the code itself when it is not one of these.</summary>
    public static string VideoFourcc(string fourcc) {
        string code = fourcc.TrimEnd('\0', ' ');

        return code.ToUpperInvariant() switch {
            "" => "RGB",
            "H264" or "X264" or "AVC1" or "DAVC" or "VSSH" => "H.264",
            "HEVC" or "H265" or "HVC1" or "HEV1" or "X265" => "H.265",
            "XVID" => "Xvid",
            "DIVX" or "DX50" or "DIV4" or "DIV5" or "DIV6" => "DivX",
            "DIV3" => "DivX 3",
            "FMP4" or "MP4V" or "M4S2" => "MPEG-4",
            "MP42" or "MP43" or "MPG4" => "MS MPEG-4",
            "MJPG" or "AVRN" or "LJPG" => "Motion JPEG",
            "DVSD" or "DV25" or "DV50" or "CDVC" or "DVHD" => "DV",
            "WMV1" or "WMV2" or "WMV3" => "WMV",
            "WVC1" => "VC-1",
            "MPG1" => "MPEG-1",
            "MPG2" or "MPEG" => "MPEG-2",
            "VP80" => "VP8",
            "VP90" => "VP9",
            "AV01" => "AV1",
            "HFYU" => "HuffYUV",
            "FFV1" => "FFV1",
            "CVID" => "Cinepak",
            "IV31" or "IV32" or "IV41" or "IV50" => "Indeo",
            _ => code,
        };
    }


    /// <summary>The name for a Windows wave format tag (<c>WAVEFORMATEX.wFormatTag</c>).</summary>
    public static string WaveFormat(int tag) {
        return tag switch {
            0x0001 or 0x0003 => "PCM",
            0x0002 or 0x0011 => "ADPCM",
            0x0006 => "A-law",
            0x0007 => "µ-law",
            0x0050 => "MP2",
            0x0055 => "MP3",
            0x00FF or 0x1600 or 0x1601 or 0x1602 or 0x706D or 0x4143 => "AAC",
            0x0160 or 0x0161 => "WMA",
            0x0162 => "WMA Pro",
            0x0163 => "WMA Lossless",
            0x2000 => "AC-3",
            0x2001 => "DTS",
            0xF1AC => "FLAC",
            >= 0x674F and <= 0x6771 => "Vorbis",
            _ => $"0x{tag:X4}",
        };
    }


    /// <summary>
    /// A wave format's name from a <c>WAVEFORMATEX</c> - the tag, or for
    /// <c>WAVE_FORMAT_EXTENSIBLE</c> the tag the sub-format GUID starts with.
    /// </summary>
    public static string WaveFormat(byte[] data, int start, int end) {
        int tag = Bytes.U16Le(data, start);
        if (tag == 0xFFFE && start + 26 <= end) {
            tag = Bytes.U16Le(data, start + 24);
        }

        return WaveFormat(tag);
    }


    /// <summary>"HDR10" or "HLG" for the transfer characteristic (ITU-T H.273) a stream declares; null for the rest.</summary>
    public static string? Transfer(int characteristics) {
        return characteristics switch {
            16 => "HDR10",
            18 => "HLG",
            _ => null,
        };
    }


    /// <summary>Luma bit depth from an <c>HEVCDecoderConfigurationRecord</c> (<c>hvcC</c>).</summary>
    public static int? HevcBitDepth(byte[] data, int start, int end) {
        return start + 18 <= end ? (data[start + 17] & 0x07) + 8 : null;
    }


    /// <summary>Bit depth from an <c>AV1CodecConfigurationRecord</c> (<c>av1C</c>).</summary>
    public static int? Av1BitDepth(byte[] data, int start, int end) {
        if (start + 3 > end) {
            return null;
        }

        bool high = (data[start + 2] & 0x40) != 0;
        bool twelve = (data[start + 2] & 0x20) != 0;

        return high ? twelve ? 12 : 10 : 8;
    }


    /// <summary>
    /// The AAC flavour and channel count from an <c>AudioSpecificConfig</c>:
    /// object type 5 is HE-AAC, 29 HE-AAC v2; channel configuration 1-6 is
    /// that many channels, 7 is 7.1. Null when the record is too short.
    /// </summary>
    public static (string Codec, int Channels)? Aac(byte[] data, int start, int end) {
        if (start + 2 > end) {
            return null;
        }

        var bits = new BitReader(data, start, end);
        int objectType = bits.Read(5);
        if (objectType == 31) {
            objectType = 32 + bits.Read(6);
        }
        int frequencyIndex = bits.Read(4);
        if (frequencyIndex == 15) {
            bits.Read(24);
        }
        int channelConfig = bits.Read(4);
        if (bits.Overrun) {
            return null;
        }

        string codec = objectType switch {
            5 => "HE-AAC",
            29 => "HE-AAC v2",
            _ => "AAC",
        };
        int channels = channelConfig switch {
            >= 1 and <= 6 => channelConfig,
            7 => 8,
            _ => 0,
        };

        return (codec, channels);
    }


    /// <summary>Channels of an AC-3 stream: its coding mode (<c>acmod</c>) plus the LFE.</summary>
    public static int Ac3Channels(int acmod, bool lfe) {
        int[] full = { 2, 1, 2, 3, 3, 4, 4, 5 };

        return full[acmod & 7] + (lfe ? 1 : 0);
    }


    /// <summary>Most significant bit first, past the end reads zeros and says so.</summary>
    private struct BitReader {
        private readonly byte[] _data;
        private readonly int _end;
        private int _bit;


        public BitReader(byte[] data, int start, int end) {
            _data = data;
            _end = end;
            _bit = start * 8;
        }


        public bool Overrun { get; private set; }


        public int Read(int count) {
            int value = 0;
            for (int i = 0; i < count; i++) {
                int index = _bit >> 3;
                int bit = 0;
                if (index < _end) {
                    bit = (_data[index] >> (7 - (_bit & 7))) & 1;
                } else {
                    Overrun = true;
                }
                value = (value << 1) | bit;
                _bit++;
            }

            return value;
        }
    }
}


/// <summary>Integer reads at an offset, both byte orders, bounds checked by the array.</summary>
internal static class Bytes {
    public static int U16(byte[] d, int i) {
        return (d[i] << 8) | d[i + 1];
    }

    public static uint U32(byte[] d, int i) {
        return ((uint)d[i] << 24) | ((uint)d[i + 1] << 16) | ((uint)d[i + 2] << 8) | d[i + 3];
    }

    public static int S32(byte[] d, int i) {
        return (int)U32(d, i);
    }

    public static ulong U64(byte[] d, int i) {
        return ((ulong)U32(d, i) << 32) | U32(d, i + 4);
    }

    public static int U16Le(byte[] d, int i) {
        return d[i] | (d[i + 1] << 8);
    }

    public static uint U32Le(byte[] d, int i) {
        return d[i] | ((uint)d[i + 1] << 8) | ((uint)d[i + 2] << 16) | ((uint)d[i + 3] << 24);
    }

    public static string Ascii(byte[] d, int i, int count = 4) {
        return System.Text.Encoding.ASCII.GetString(d, i, count);
    }
}
