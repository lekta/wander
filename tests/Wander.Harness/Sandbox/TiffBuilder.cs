using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace Wander.Harness.Sandbox;

/// <summary>
/// Writes a little-endian TIFF from named sections - IFDs and blobs - so an
/// entry in one IFD can point at another section by name and the builder
/// fills in the offset. Enough for a DNG (IFD0 with a preview, an Exif IFD,
/// a raw sub-IFD with a strip) and for the TIFF blocks a CR3 carries in its
/// CMT boxes. Large blobs are streamed, not buffered.
/// </summary>
public sealed class TiffBuilder {
    private const ushort TypeByte = 1;
    private const ushort TypeAscii = 2;
    private const ushort TypeShort = 3;
    private const ushort TypeLong = 4;
    private const ushort TypeRational = 5;
    private const ushort TypeUndefined = 7;

    private readonly List<Section> _sections = new();


    public TiffBuilder Ifd(string name, TiffIfd ifd) {
        _sections.Add(new Section(name, ifd, null, 0, null));

        return this;
    }

    public TiffBuilder Blob(string name, byte[] bytes) {
        _sections.Add(new Section(name, null, bytes, bytes.Length, null));

        return this;
    }

    /// <summary>A blob written by <paramref name="writer"/> straight into the output stream - for gigabyte-class padding.</summary>
    public TiffBuilder Blob(string name, long length, Action<Stream, long> writer) {
        _sections.Add(new Section(name, null, null, length, writer));

        return this;
    }

    public byte[] Build() {
        using var stream = new MemoryStream();
        Build(stream);

        return stream.ToArray();
    }

    public void Build(Stream output) {
        var offsets = new Dictionary<string, uint>(StringComparer.Ordinal);
        long position = 8;
        foreach (var section in _sections) {
            offsets[section.Name] = checked((uint)position);
            position += section.Ifd is not null ? section.Ifd.Size : Pad(section.Length);
        }

        Span<byte> header = stackalloc byte[8];
        header[0] = (byte)'I';
        header[1] = (byte)'I';
        BinaryPrimitives.WriteUInt16LittleEndian(header[2..], 42);
        BinaryPrimitives.WriteUInt32LittleEndian(header[4..], 8);
        output.Write(header);

        foreach (var section in _sections) {
            if (section.Ifd is not null) {
                section.Ifd.Write(output, offsets[section.Name], offsets);
            } else if (section.Bytes is not null) {
                output.Write(section.Bytes);
                if ((section.Bytes.Length & 1) == 1) {
                    output.WriteByte(0);
                }
            } else {
                section.Writer!(output, section.Length);
                if ((section.Length & 1) == 1) {
                    output.WriteByte(0);
                }
            }
        }
    }


    private static long Pad(long length) {
        return length + (length & 1);
    }


    private sealed record Section(string Name, TiffIfd? Ifd, byte[]? Bytes, long Length, Action<Stream, long>? Writer);


    /// <summary>One image file directory; entries are sorted by tag on write, as the format requires.</summary>
    public sealed class TiffIfd {
        private readonly List<Entry> _entries = new();


        /// <summary>Bytes the directory occupies: the table plus every value that does not fit in four bytes.</summary>
        public long Size {
            get {
                long size = 2 + 12L * _entries.Count + 4;
                foreach (var entry in _entries) {
                    if (entry.Value is { Length: > 4 } value) {
                        size += Pad(value.Length);
                    }
                }

                return size;
            }
        }


        public TiffIfd Ascii(ushort tag, string text) {
            var bytes = Encoding.ASCII.GetBytes(text + "\0");

            return Add(tag, TypeAscii, (uint)bytes.Length, bytes);
        }

        public TiffIfd Short(ushort tag, params ushort[] values) {
            var bytes = new byte[values.Length * 2];
            for (int i = 0; i < values.Length; i++) {
                BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(i * 2), values[i]);
            }

            return Add(tag, TypeShort, (uint)values.Length, bytes);
        }

        public TiffIfd Long(ushort tag, params uint[] values) {
            var bytes = new byte[values.Length * 4];
            for (int i = 0; i < values.Length; i++) {
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(i * 4), values[i]);
            }

            return Add(tag, TypeLong, (uint)values.Length, bytes);
        }

        public TiffIfd Rational(ushort tag, uint numerator, uint denominator) {
            var bytes = new byte[8];
            BinaryPrimitives.WriteUInt32LittleEndian(bytes, numerator);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), denominator);

            return Add(tag, TypeRational, 1, bytes);
        }

        public TiffIfd Bytes(ushort tag, params byte[] values) {
            return Add(tag, TypeByte, (uint)values.Length, values);
        }

        public TiffIfd Undefined(ushort tag, byte[] values) {
            return Add(tag, TypeUndefined, (uint)values.Length, values);
        }

        /// <summary>A LONG whose value is the offset of another section.</summary>
        public TiffIfd Ref(ushort tag, string sectionName) {
            _entries.Add(new Entry(tag, TypeLong, 1, null, sectionName));

            return this;
        }


        internal void Write(Stream output, uint ownOffset, IReadOnlyDictionary<string, uint> offsets) {
            var sorted = _entries.OrderBy(e => e.Tag).ToList();
            uint external = ownOffset + 2 + 12u * (uint)sorted.Count + 4;

            var table = new byte[2 + 12 * sorted.Count + 4];
            BinaryPrimitives.WriteUInt16LittleEndian(table, (ushort)sorted.Count);
            var tail = new MemoryStream();

            for (int i = 0; i < sorted.Count; i++) {
                var entry = sorted[i];
                var slot = table.AsSpan(2 + i * 12, 12);
                BinaryPrimitives.WriteUInt16LittleEndian(slot, entry.Tag);
                BinaryPrimitives.WriteUInt16LittleEndian(slot[2..], entry.Type);
                BinaryPrimitives.WriteUInt32LittleEndian(slot[4..], entry.Count);

                if (entry.Section is not null) {
                    BinaryPrimitives.WriteUInt32LittleEndian(slot[8..], offsets[entry.Section]);
                } else if (entry.Value!.Length <= 4) {
                    entry.Value.CopyTo(slot[8..]);
                } else {
                    BinaryPrimitives.WriteUInt32LittleEndian(slot[8..], external + (uint)tail.Length);
                    tail.Write(entry.Value);
                    if ((entry.Value.Length & 1) == 1) {
                        tail.WriteByte(0);
                    }
                }
            }
            // Next-IFD pointer: chains are expressed through Ref entries here.
            BinaryPrimitives.WriteUInt32LittleEndian(table.AsSpan(2 + 12 * sorted.Count), 0);

            output.Write(table);
            tail.Position = 0;
            tail.CopyTo(output);
        }


        private TiffIfd Add(ushort tag, ushort type, uint count, byte[] value) {
            _entries.Add(new Entry(tag, type, count, value, null));

            return this;
        }


        private sealed record Entry(ushort Tag, ushort Type, uint Count, byte[]? Value, string? Section);
    }
}
