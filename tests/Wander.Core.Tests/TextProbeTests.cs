using System.Text;
using Wander.Core.Preview;

namespace Wander.Core.Tests;

public class TextProbeTests {
    [Fact]
    public void EmptySample_IsText() {
        Assert.True(TextProbe.LooksLikeText(ReadOnlySpan<byte>.Empty));
    }


    [Fact]
    public void PlainAscii_IsText() {
        Assert.True(TextProbe.LooksLikeText(Encoding.UTF8.GetBytes("m_Name: Settings\nm_Enabled: 1\n")));
    }


    [Fact]
    public void Utf8WithCyrillic_IsText() {
        Assert.True(TextProbe.LooksLikeText(Encoding.UTF8.GetBytes("Имя: Настройки\r\n")));
    }


    [Fact]
    public void NulByte_IsBinary() {
        var bytes = Encoding.ASCII.GetBytes("UnityFS\0\0\0\0\0\x08\x05");

        Assert.False(TextProbe.LooksLikeText(bytes));
    }


    /// <summary>
    /// UTF-16 text is mostly NUL bytes for Latin content, so the NUL rule
    /// would call it binary. The BOM is what saves it.
    /// </summary>
    [Fact]
    public void Utf16WithBom_IsText() {
        var bytes = new UnicodeEncoding(bigEndian: false, byteOrderMark: true)
            .GetPreamble()
            .Concat(Encoding.Unicode.GetBytes("hello"))
            .ToArray();

        Assert.True(TextProbe.LooksLikeText(bytes));
    }


    [Fact]
    public void Utf8Bom_IsText() {
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("x")).ToArray();

        Assert.True(TextProbe.LooksLikeText(bytes));
    }


    /// <summary>
    /// A stray control byte in a log is not proof of binary — the test is a
    /// proportion, and one escape in a hundred characters stays text.
    /// </summary>
    [Fact]
    public void OccasionalControlByte_StaysText() {
        var bytes = Encoding.ASCII.GetBytes(new string('a', 200)).ToList();
        bytes[50] = 0x07;

        Assert.True(TextProbe.LooksLikeText(bytes.ToArray()));
    }


    [Fact]
    public void MostlyControlBytes_IsBinary() {
        var bytes = Enumerable.Range(0, 200).Select(i => (byte)(i % 20 == 0 ? 'a' : 0x01)).ToArray();

        Assert.False(TextProbe.LooksLikeText(bytes));
    }


    [Fact]
    public void TabsAndNewlines_AreNotControlBytes() {
        var bytes = Encoding.ASCII.GetBytes("a\tb\r\nc\r\nd\r\n");

        Assert.True(TextProbe.LooksLikeText(bytes));
    }
}
