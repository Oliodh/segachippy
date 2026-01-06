using System;

namespace ConsoleApp2.Emu;

public sealed class SmsBus : IZ80Bus
{
    private readonly SmsVdp _vdp;

    private readonly byte[] _ram = new byte[0x2000]; // 8KB system RAM
    private byte[] _rom = Array.Empty<byte>();

    // Simple fixed mapping (no mapper):
    // 0000-3FFF: ROM bank 0
    // 4000-7FFF: ROM bank 1 (or continuation)
    // 8000-BFFF: ROM bank 2 (or continuation)
    // C000-DFFF: RAM
    // E000-FFFF: RAM mirror
    public SmsBus(SmsVdp vdp)
    {
        _vdp = vdp;
    }

    public void LoadRom(ReadOnlySpan<byte> rom)
    {
        _rom = rom.ToArray();
    }

    public void Reset()
    {
        Array.Clear(_ram);
    }

    public byte ReadByte(ushort address)
    {
        if (address <= 0xBFFF)
        {
            return ReadRom(address);
        }

        if (address >= 0xC000)
        {
            return _ram[address & 0x1FFF];
        }

        // Unmapped in this minimal core
        return 0xFF;
    }

    public void WriteByte(ushort address, byte value)
    {
        if (address >= 0xC000)
        {
            _ram[address & 0x1FFF] = value;
            return;
        }

        // ROM area write ignored in this minimal core (no mapper)
    }

    public byte ReadPort(byte port)
    {
        // SMS ports are mirrored; only low 8-bit is decoded in practice.
        // VDP:
        // 0xBE: data
        // 0xBF: control/status
        switch (port)
        {
            case 0xBE:
                return _vdp.ReadData();
            case 0xBF:
                return _vdp.ReadStatus();
            default:
                return 0xFF;
        }
    }

    public void WritePort(byte port, byte value)
    {
        switch (port)
        {
            case 0xBE:
                _vdp.WriteData(value);
                break;
            case 0xBF:
                _vdp.WriteControl(value);
                break;
            default:
                break;
        }
    }

    private byte ReadRom(ushort address)
    {
        if (_rom.Length == 0)
        {
            return 0xFF;
        }

        // Wrap for small ROMs
        var index = address % _rom.Length;
        return _rom[index];
    }
}