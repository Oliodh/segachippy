using System;

namespace ConsoleApp2.Emu;

// Minimal Z80 core: enough structure to expand; implements a small opcode subset.
// Many ROMs will require more opcodes + interrupts + mappers to fully run.
public sealed class Z80
{
    private readonly IZ80Bus _bus;

    public Z80(IZ80Bus bus)
    {
        _bus = bus;
    }

    // Registers
    public ushort PC { get; private set; }
    public ushort SP { get; private set; }

    public byte A { get; private set; }
    public byte F { get; private set; }
    public byte B { get; private set; }
    public byte C { get; private set; }
    public byte D { get; private set; }
    public byte E { get; private set; }
    public byte H { get; private set; }
    public byte L { get; private set; }

    public void Reset()
    {
        PC = 0x0000;
        SP = 0xDFF0;
        A = F = B = C = D = E = H = L = 0;
    }

    public int Step()
    {
        var op = FetchByte();

        switch (op)
        {
            case 0x00: // NOP
                return 4;

            case 0x3E: // LD A,n
                A = FetchByte();
                return 7;

            case 0x06: // LD B,n
                B = FetchByte();
                return 7;

            case 0x0E: // LD C,n
                C = FetchByte();
                return 7;

            case 0x16: // LD D,n
                D = FetchByte();
                return 7;

            case 0x1E: // LD E,n
                E = FetchByte();
                return 7;

            case 0x26: // LD H,n
                H = FetchByte();
                return 7;

            case 0x2E: // LD L,n
                L = FetchByte();
                return 7;

            case 0x32: // LD (nn),A
            {
                var lo = FetchByte();
                var hi = FetchByte();
                var addr = (ushort)(lo | (hi << 8));
                _bus.WriteByte(addr, A);
                return 13;
            }

            case 0x3A: // LD A,(nn)
            {
                var lo = FetchByte();
                var hi = FetchByte();
                var addr = (ushort)(lo | (hi << 8));
                A = _bus.ReadByte(addr);
                return 13;
            }

            case 0xC3: // JP nn
            {
                var lo = FetchByte();
                var hi = FetchByte();
                PC = (ushort)(lo | (hi << 8));
                return 10;
            }

            case 0xCD: // CALL nn
            {
                var lo = FetchByte();
                var hi = FetchByte();
                var addr = (ushort)(lo | (hi << 8));
                Push16(PC);
                PC = addr;
                return 17;
            }

            case 0xC9: // RET
                PC = Pop16();
                return 10;

            case 0xD3: // OUT (n),A
            {
                var port = FetchByte();
                _bus.WritePort(port, A);
                return 11;
            }

            case 0xDB: // IN A,(n)
            {
                var port = FetchByte();
                A = _bus.ReadPort(port);
                return 11;
            }

            case 0xAF: // XOR A
                A = 0;
                F = 0x80; // Z set
                return 4;

            default:
                // For now: treat unknown opcodes as NOP to keep advancing.
                // You’ll extend this table to run real games.
                return 4;
        }
    }

    private byte FetchByte()
    {
        var b = _bus.ReadByte(PC);
        PC++;
        return b;
    }

    private void Push16(ushort value)
    {
        SP--;
        _bus.WriteByte(SP, (byte)(value >> 8));
        SP--;
        _bus.WriteByte(SP, (byte)value);
    }

    private ushort Pop16()
    {
        var lo = _bus.ReadByte(SP);
        SP++;
        var hi = _bus.ReadByte(SP);
        SP++;
        return (ushort)(lo | (hi << 8));
    }
}