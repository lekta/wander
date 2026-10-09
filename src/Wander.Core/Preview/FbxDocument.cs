using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace Wander.Core.Preview;

/// <summary>
/// One node of an FBX file: a name, its properties - numbers, strings,
/// arrays (<c>double[]</c>, <c>int[]</c>, <c>long[]</c>, <c>float[]</c>) -
/// and its children. The same tree whether the file was binary or text.
/// </summary>
internal sealed class FbxNode {
    public FbxNode(string name) {
        Name = name;
    }


    public string Name { get; }

    public List<object> Properties { get; } = new();

    public List<FbxNode> Children { get; } = new();


    public FbxNode? Child(string name) {
        return Children.FirstOrDefault(c => c.Name == name);
    }

    public IEnumerable<FbxNode> All(string name) {
        return Children.Where(c => c.Name == name);
    }

    public long Long(int index) {
        return index < Properties.Count ? Convert.ToInt64(Properties[index], CultureInfo.InvariantCulture) : 0;
    }

    public double Double(int index) {
        return index < Properties.Count ? Convert.ToDouble(Properties[index], CultureInfo.InvariantCulture) : 0;
    }

    public string String(int index) {
        return index < Properties.Count && Properties[index] is string s ? s : "";
    }
}


/// <summary>
/// FBX read into its node tree, binary or text. Binary (7.x): a 27-byte
/// header, then records - end offset, property count, property bytes
/// (32-bit before version 7500, 64-bit from it), a name, typed properties,
/// nested records closed by an empty one; arrays may be zlib-deflated. Text:
/// <c>Name: value, value { ... }</c>, arrays as <c>*N { a: ... }</c>,
/// comments after <c>;</c>.
/// </summary>
internal static class FbxDocument {
    private const string BinaryMagic = "Kaydara FBX Binary  ";

    /// <summary>An array past this many elements is not a preview's; the reader stops.</summary>
    private const int MaxArray = 64 * 1024 * 1024;


    /// <summary>The root (unnamed) node, or null when the bytes are not FBX this reads.</summary>
    public static FbxNode? Parse(byte[] bytes) {
        if (bytes.Length > 27 && Encoding.ASCII.GetString(bytes, 0, BinaryMagic.Length) == BinaryMagic) {
            int version = (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(23));
            var root = new FbxNode("");
            int at = 27;
            bool wide = version >= 7500;
            while (ReadNode(bytes, ref at, wide) is { } node) {
                root.Children.Add(node);
            }

            return root;
        }

        return LooksLikeText(bytes) ? new TextParser(Encoding.UTF8.GetString(bytes)).Document() : null;
    }


    // --- Binary ---


    private static FbxNode? ReadNode(byte[] d, ref int at, bool wide) {
        int head = wide ? 25 : 13;
        if (at + head > d.Length) {
            return null;
        }

        long end = wide ? (long)BinaryPrimitives.ReadUInt64LittleEndian(d.AsSpan(at)) : BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(at));
        long count = wide ? (long)BinaryPrimitives.ReadUInt64LittleEndian(d.AsSpan(at + 8)) : BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(at + 4));
        int nameLength = d[at + (wide ? 24 : 12)];
        if (end == 0) {
            // The empty record that closes a list.
            at += head;

            return null;
        }
        if (end > d.Length || end <= at) {
            throw new FormatException("FBX record out of bounds");
        }

        var node = new FbxNode(Encoding.ASCII.GetString(d, at + head, nameLength));
        int p = at + head + nameLength;
        for (long i = 0; i < count; i++) {
            node.Properties.Add(ReadProperty(d, ref p));
        }
        while (p < end) {
            if (ReadNode(d, ref p, wide) is not { } child) {
                break;
            }
            node.Children.Add(child);
        }
        at = (int)end;

        return node;
    }

    private static object ReadProperty(byte[] d, ref int p) {
        char type = (char)d[p++];
        var span = d.AsSpan();
        object value;
        switch (type) {
            case 'Y':
                value = BinaryPrimitives.ReadInt16LittleEndian(span[p..]);
                p += 2;
                break;

            case 'C':
                value = d[p] != 0;
                p += 1;
                break;

            case 'I':
                value = BinaryPrimitives.ReadInt32LittleEndian(span[p..]);
                p += 4;
                break;

            case 'F':
                value = BinaryPrimitives.ReadSingleLittleEndian(span[p..]);
                p += 4;
                break;

            case 'D':
                value = BinaryPrimitives.ReadDoubleLittleEndian(span[p..]);
                p += 8;
                break;

            case 'L':
                value = BinaryPrimitives.ReadInt64LittleEndian(span[p..]);
                p += 8;
                break;

            case 'S' or 'R': {
                    int length = BinaryPrimitives.ReadInt32LittleEndian(span[p..]);
                    if (length < 0 || p + 4 + length > d.Length) {
                        throw new FormatException("FBX string out of bounds");
                    }
                    value = type == 'S' ? Encoding.UTF8.GetString(d, p + 4, length) : Array.Empty<byte>();
                    p += 4 + length;
                    break;
                }

            case 'f' or 'd' or 'l' or 'i' or 'b':
                value = ReadArray(d, ref p, type);
                break;

            default:
                throw new FormatException($"FBX property type '{type}'");
        }

        return value;
    }

    private static object ReadArray(byte[] d, ref int p, char type) {
        int length = BinaryPrimitives.ReadInt32LittleEndian(d.AsSpan(p));
        int encoding = BinaryPrimitives.ReadInt32LittleEndian(d.AsSpan(p + 4));
        int stored = BinaryPrimitives.ReadInt32LittleEndian(d.AsSpan(p + 8));
        p += 12;
        int element = type switch {
            'd' or 'l' => 8,
            'f' or 'i' => 4,
            _ => 1,
        };
        if (length < 0 || length > MaxArray || stored < 0 || p + stored > d.Length) {
            throw new FormatException("FBX array out of bounds");
        }

        byte[] raw;
        if (encoding == 1) {
            raw = new byte[length * element];
            using var inflate = new ZLibStream(new MemoryStream(d, p, stored), CompressionMode.Decompress);
            inflate.ReadExactly(raw);
        } else {
            raw = d.AsSpan(p, Math.Min(stored, length * element)).ToArray();
        }
        p += stored;

        switch (type) {
            case 'd': {
                    var values = new double[length];
                    for (int i = 0; i < length; i++) {
                        values[i] = BinaryPrimitives.ReadDoubleLittleEndian(raw.AsSpan(i * 8));
                    }

                    return values;
                }

            case 'f': {
                    var values = new double[length];
                    for (int i = 0; i < length; i++) {
                        values[i] = BinaryPrimitives.ReadSingleLittleEndian(raw.AsSpan(i * 4));
                    }

                    return values;
                }

            case 'i': {
                    var values = new int[length];
                    for (int i = 0; i < length; i++) {
                        values[i] = BinaryPrimitives.ReadInt32LittleEndian(raw.AsSpan(i * 4));
                    }

                    return values;
                }

            case 'l': {
                    var values = new long[length];
                    for (int i = 0; i < length; i++) {
                        values[i] = BinaryPrimitives.ReadInt64LittleEndian(raw.AsSpan(i * 8));
                    }

                    return values;
                }

            default:
                return raw;
        }
    }


    // --- Text ---


    private static bool LooksLikeText(byte[] bytes) {
        string head = Encoding.ASCII.GetString(bytes, 0, Math.Min(bytes.Length, 512));

        return head.Contains("FBXHeaderExtension", StringComparison.Ordinal) || head.StartsWith("; FBX", StringComparison.Ordinal);
    }


    /// <summary>The text form: names with a colon, comma-separated values, braces for children.</summary>
    private sealed class TextParser {
        private readonly string _text;
        private int _at;


        public TextParser(string text) {
            _text = text;
        }


        public FbxNode Document() {
            var root = new FbxNode("");
            while (Node() is { } node) {
                root.Children.Add(node);
            }

            return root;
        }


        /// <summary>One node: <c>Name: values { children }</c>; null at the end of a list or the file.</summary>
        private FbxNode? Node() {
            SkipBlank();
            if (_at >= _text.Length || _text[_at] == '}') {
                return null;
            }

            int start = _at;
            while (_at < _text.Length && _text[_at] != ':' && _text[_at] != '\n') {
                _at++;
            }
            if (_at >= _text.Length || _text[_at] != ':') {
                throw new FormatException("FBX text: a name without a colon");
            }

            var node = new FbxNode(_text[start.._at].Trim());
            _at++;
            Values(node);
            SkipSpaces();
            if (_at < _text.Length && _text[_at] == '{') {
                _at++;
                while (Node() is { } child) {
                    node.Children.Add(child);
                }
                SkipBlank();
                if (_at < _text.Length && _text[_at] == '}') {
                    _at++;
                }
            }

            return node;
        }

        /// <summary>The values on a node's line - or, for <c>*N { a: ... }</c>, the array.</summary>
        private void Values(FbxNode node) {
            SkipSpaces();
            if (_at < _text.Length && _text[_at] == '*') {
                while (_at < _text.Length && _text[_at] != '{') {
                    _at++;
                }
                _at++;
                SkipBlank();
                // "a:"
                while (_at < _text.Length && _text[_at] != ':') {
                    _at++;
                }
                _at++;
                var numbers = new List<double>();
                while (true) {
                    SkipBlank();
                    if (_at >= _text.Length || _text[_at] == '}') {
                        _at++;

                        break;
                    }
                    if (_text[_at] == ',') {
                        _at++;

                        continue;
                    }
                    numbers.Add(Convert.ToDouble(Number(), CultureInfo.InvariantCulture));
                }
                node.Properties.Add(numbers.All(n => n == Math.Floor(n) && Math.Abs(n) < int.MaxValue)
                    ? numbers.Select(n => (int)n).ToArray()
                    : numbers.ToArray());

                return;
            }

            while (_at < _text.Length) {
                SkipSpaces();
                if (_at >= _text.Length || _text[_at] is '\n' or '\r' or '{' or '}') {
                    return;
                }

                char c = _text[_at];
                if (c == '"') {
                    int end = _text.IndexOf('"', _at + 1);
                    if (end < 0) {
                        throw new FormatException("FBX text: an open string");
                    }
                    node.Properties.Add(_text[(_at + 1)..end]);
                    _at = end + 1;
                } else if (c == ',') {
                    _at++;
                    // A value list can go on to the next line after a comma.
                    SkipBlank();
                } else if (c is '-' or '+' or '.' || char.IsDigit(c)) {
                    node.Properties.Add(Number());
                } else {
                    // A bare word: T, Y, a flag - kept as text.
                    int start = _at;
                    while (_at < _text.Length && !char.IsWhiteSpace(_text[_at]) && _text[_at] is not (',' or '{' or '}')) {
                        _at++;
                    }
                    node.Properties.Add(_text[start.._at]);
                }
            }
        }

        /// <summary>A whole number as a long - object ids are 64-bit, past what a double holds exactly - else a double.</summary>
        private object Number() {
            int start = _at;
            while (_at < _text.Length && (char.IsDigit(_text[_at]) || _text[_at] is '-' or '+' or '.' or 'e' or 'E')) {
                _at++;
            }

            var token = _text.AsSpan(start, _at - start);
            if (token.IndexOfAny('.', 'e', 'E') < 0 && long.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out long whole)) {
                return whole;
            }

            return double.Parse(token, NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        private void SkipSpaces() {
            while (_at < _text.Length && _text[_at] is ' ' or '\t') {
                _at++;
            }
        }

        /// <summary>Spaces, line breaks and comments.</summary>
        private void SkipBlank() {
            while (_at < _text.Length) {
                char c = _text[_at];
                if (char.IsWhiteSpace(c)) {
                    _at++;
                } else if (c == ';') {
                    while (_at < _text.Length && _text[_at] != '\n') {
                        _at++;
                    }
                } else {
                    return;
                }
            }
        }
    }
}
