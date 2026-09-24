using System.Buffers.Binary;
using Wander.Core.Preview;

namespace Wander.Core.Tests;

public class PeHeaderTests {
    [Fact]
    public void X64ConsoleProgram() {
        var facts = PeHeader.Parse(Pe(machine: 0x8664, wide: true, subsystem: 3, dll: false, clr: false));

        Assert.Equal(new PeFacts(PeMachine.X64, PeSubsystem.Console, false, false), facts);
    }

    [Fact]
    public void X86WindowsLibraryWithClrHeader() {
        var facts = PeHeader.Parse(Pe(machine: 0x014C, wide: false, subsystem: 2, dll: true, clr: true));

        Assert.Equal(new PeFacts(PeMachine.X86, PeSubsystem.Windows, true, true), facts);
    }

    [Fact]
    public void Arm64Driver() {
        var facts = PeHeader.Parse(Pe(machine: 0xAA64, wide: true, subsystem: 1, dll: false, clr: false));

        Assert.Equal(PeMachine.Arm64, facts!.Machine);
        Assert.Equal(PeSubsystem.Native, facts.Subsystem);
    }

    [Fact]
    public void NotAProgram_Null() {
        Assert.Null(PeHeader.Parse("just some text, not a program at all, long enough to pass the length check"u8));
        Assert.Null(PeHeader.Parse(new byte[] { (byte)'M', (byte)'Z' }));
    }

    [Fact]
    public void PointerPastTheEnd_Null() {
        var bytes = Pe(machine: 0x8664, wide: true, subsystem: 3, dll: false, clr: false);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(0x3C), 100_000);

        Assert.Null(PeHeader.Parse(bytes));
    }

    /// <summary>A pointer this close to int.MaxValue wrapped round in "pointer + 26" and read past the buffer.</summary>
    [Fact]
    public void PointerNearIntMax_Null() {
        var bytes = Pe(machine: 0x8664, wide: true, subsystem: 3, dll: false, clr: false);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(0x3C), int.MaxValue - 10);

        Assert.Null(PeHeader.Parse(bytes));
    }

    [Fact]
    public void CoreAssembly_DotNetLibrary() {
        using var file = File.OpenRead(typeof(PeHeader).Assembly.Location);
        var facts = PeHeader.Read(file);

        Assert.NotNull(facts);
        Assert.True(facts!.IsDotNet);
        Assert.True(facts.IsLibrary);
    }


    /// <summary>The smallest header the reader accepts: DOS stub pointer, COFF header, optional header, directories.</summary>
    private static byte[] Pe(ushort machine, bool wide, ushort subsystem, bool dll, bool clr) {
        const int pe = 0x80;
        var bytes = new byte[1024];
        bytes[0] = (byte)'M';
        bytes[1] = (byte)'Z';
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(0x3C), pe);
        bytes[pe] = (byte)'P';
        bytes[pe + 1] = (byte)'E';
        int coff = pe + 4;
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(coff), machine);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(coff + 18), (ushort)(dll ? 0x2002 : 0x0002));
        int optional = coff + 20;
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(optional), (ushort)(wide ? 0x20B : 0x10B));
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(optional + 68), subsystem);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(optional + (wide ? 108 : 92)), 16);
        if (clr) {
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(optional + (wide ? 112 : 96) + (14 * 8)), 0x2008);
        }

        return bytes;
    }
}
