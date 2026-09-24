using System.Buffers.Binary;

namespace Wander.Core.Preview;

/// <summary>The processor a program is built for, from the PE header's <c>Machine</c> field.</summary>
public enum PeMachine {
    Other,
    X86,
    X64,
    Arm,
    Arm64,
}


/// <summary>What kind of program Windows starts it as, from the optional header's <c>Subsystem</c>.</summary>
public enum PeSubsystem {
    Other,
    Windows,
    Console,
    Native,
    Efi,
}


/// <summary>What the header of a Windows program says about it (PLAN B7).</summary>
/// <param name="Machine">The processor.</param>
/// <param name="Subsystem">Window, console, driver.</param>
/// <param name="IsLibrary">A DLL rather than a program to start.</param>
/// <param name="IsDotNet">Carries a CLR header - a .NET assembly.</param>
public sealed record PeFacts(PeMachine Machine, PeSubsystem Subsystem, bool IsLibrary, bool IsDotNet);


/// <summary>
/// Reads the few facts the preview card shows out of a PE file's headers:
/// the DOS stub's pointer, the COFF header, the start of the optional
/// header. A few hundred bytes, no loader, nothing executed.
/// </summary>
public static class PeHeader {
    /// <summary>Enough for the DOS header, the COFF header and the optional header of either width.</summary>
    private const int HeaderBudget = 4096;

    private const ushort DllFlag = 0x2000;
    private const ushort Pe32Magic = 0x10B;
    private const ushort Pe32PlusMagic = 0x20B;
    private const int ClrDirectory = 14;


    /// <summary>The facts, or null when the stream is not a PE file.</summary>
    public static PeFacts? Read(Stream stream) {
        var buffer = new byte[HeaderBudget];
        int read = 0;
        while (read < buffer.Length) {
            int n = stream.Read(buffer, read, buffer.Length - read);
            if (n == 0) {
                break;
            }
            read += n;
        }

        return Parse(buffer.AsSpan(0, read));
    }


    /// <inheritdoc cref="Read"/>
    public static PeFacts? Parse(ReadOnlySpan<byte> bytes) {
        if (bytes.Length < 0x40 || bytes[0] != 'M' || bytes[1] != 'Z') {
            return null;
        }

        int pe = BinaryPrimitives.ReadInt32LittleEndian(bytes[0x3C..]);
        // "PE\0\0", the 20-byte COFF header, and the optional header's magic.
        // Against the length minus 26, not the pointer plus 26: a pointer
        // near int.MaxValue would wrap round and pass.
        if (pe < 0x40 || pe > bytes.Length - 26
            || bytes[pe] != 'P' || bytes[pe + 1] != 'E' || bytes[pe + 2] != 0 || bytes[pe + 3] != 0) {
            return null;
        }

        int coff = pe + 4;
        ushort machine = BinaryPrimitives.ReadUInt16LittleEndian(bytes[coff..]);
        ushort characteristics = BinaryPrimitives.ReadUInt16LittleEndian(bytes[(coff + 18)..]);
        int optional = coff + 20;
        ushort magic = BinaryPrimitives.ReadUInt16LittleEndian(bytes[optional..]);
        bool wide = magic == Pe32PlusMagic;
        if (!wide && magic != Pe32Magic) {
            return null;
        }

        var subsystem = PeSubsystem.Other;
        if (optional + 70 <= bytes.Length) {
            subsystem = SubsystemOf(BinaryPrimitives.ReadUInt16LittleEndian(bytes[(optional + 68)..]));
        }

        // The data directories follow the fixed part of the optional
        // header; the fourteenth is the CLR header, set only in .NET files.
        int count = optional + (wide ? 108 : 92);
        int directories = optional + (wide ? 112 : 96);
        int clr = directories + (ClrDirectory * 8);
        bool dotNet = count + 4 <= bytes.Length
            && BinaryPrimitives.ReadInt32LittleEndian(bytes[count..]) > ClrDirectory
            && clr + 8 <= bytes.Length
            && BinaryPrimitives.ReadInt32LittleEndian(bytes[clr..]) != 0;

        return new PeFacts(MachineOf(machine), subsystem, (characteristics & DllFlag) != 0, dotNet);
    }


    private static PeMachine MachineOf(ushort value) {
        return value switch {
            0x014C => PeMachine.X86,
            0x8664 => PeMachine.X64,
            0x01C0 or 0x01C4 => PeMachine.Arm,
            0xAA64 => PeMachine.Arm64,
            _ => PeMachine.Other,
        };
    }

    private static PeSubsystem SubsystemOf(ushort value) {
        return value switch {
            1 => PeSubsystem.Native,
            2 => PeSubsystem.Windows,
            3 => PeSubsystem.Console,
            10 or 11 or 12 or 13 => PeSubsystem.Efi,
            _ => PeSubsystem.Other,
        };
    }
}
