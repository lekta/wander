using System.Runtime.InteropServices;
using System.Text;

namespace Wander.Platform.Windows.Search;

/// <summary>
/// The <c>IFilter</c> interop, one file's worth. This is the mechanism
/// Windows Search itself uses to read documents: a format registers a
/// filter under its extension, and anybody can ask for one by path.
///
/// <para>
/// Written out here rather than taken from a package because it is four
/// methods and two structs, and because <c>Wander.Core</c>'s no-dependency
/// rule is worth more than the hundred lines it saves.
/// </para>
/// </summary>
internal static class NativeFilter {
    /// <summary>
    /// Hands back the filter registered for this file's extension, or a
    /// failure HRESULT when there is none. What is registered varies by
    /// machine: Windows itself ships filters for <c>.doc</c>, <c>.rtf</c>,
    /// <c>.htm</c> and plain text; <c>.pdf</c> arrives with a PDF reader
    /// and the Office formats with Office.
    /// </summary>
    [DllImport("query.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    internal static extern int LoadIFilter(string pwcsPath, IntPtr pUnkOuter, out IFilter ppIUnk);


    internal const int SOk = 0;
    internal const int FilterEEndOfChunks = unchecked((int)0x80041700);
    internal const int FilterENoMoreText = unchecked((int)0x80041701);
    internal const int FilterENoText = unchecked((int)0x80041705);
    internal const int FilterSLastText = 0x00041709;
}


[Flags]
internal enum FilterInit {
    None = 0,
    CanonParagraphs = 1,
    HardLineBreaks = 2,
    CanonHyphens = 4,
    CanonSpaces = 8,
    ApplyIndexAttributes = 16,
    ApplyOtherAttributes = 32,
    IndexingOnly = 64,
    SearchLinks = 128,
    FilterOwnedValueOk = 512,
}


internal enum ChunkState {
    Text = 0x1,
    Value = 0x2,
    FilterOwnedValue = 0x4,
}


[StructLayout(LayoutKind.Sequential)]
internal struct PropSpec {
    public uint Kind;
    public uint PropId;
}


[StructLayout(LayoutKind.Sequential)]
internal struct FullPropSpec {
    public Guid PropSet;
    public PropSpec Property;
}


[StructLayout(LayoutKind.Sequential)]
internal struct StatChunk {
    public uint IdChunk;
    public uint BreakType;
    public ChunkState Flags;
    public uint Locale;
    public FullPropSpec Attribute;
    public uint IdChunkSource;
    public uint StartSource;
    public uint LenSource;
}


[ComImport]
[Guid("89BCB740-6119-101A-BCB7-00DD010655AF")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IFilter {
    // Every method is PreserveSig: the interface signals ordinary states
    // ("no more chunks", "no more text") with failure HRESULTs, and letting
    // the marshaller turn those into exceptions would mean an exception per
    // chunk of every document.
    [PreserveSig]
    int Init(FilterInit flags, uint attributeCount, IntPtr attributes, ref uint outFlags);

    [PreserveSig]
    int GetChunk(out StatChunk chunk);

    [PreserveSig]
    int GetText(ref uint bufferChars, [MarshalAs(UnmanagedType.LPWStr)] StringBuilder buffer);

    [PreserveSig]
    int GetValue(out IntPtr value);

    [PreserveSig]
    int BindRegion(IntPtr region, ref Guid iid, out IntPtr obj);
}
