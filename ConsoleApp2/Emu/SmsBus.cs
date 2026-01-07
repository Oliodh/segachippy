using System;

namespace ConsoleApp2.Emu;

public sealed class SmsBus : IZ80Bus
{
    private const int BankSize = 0x4000;

    private readonly SmsVdp _vdp;

    private readonly byte[] _ram = new byte[0x2000]; // 8KB system RAM
    private byte[] _rom = Array.Empty<byte>();

    // Sega mapper bank registers
    private int _bank0 = 0; // 0x0000-0x3FFF (usually fixed to slot 0)
    private int _bank1 = 1; // 0x4000-0x7FFF
    private int _bank2 = 2; // 0x8000-0xBFFF

    // Controller state (directly set by the emulator)
    public byte Joypad1 { get; set; } = 0xFF; // Active low
    public byte Joypad2 { get; set; } = 0xFF;

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
        _bank0 = 0;
        _bank1 = 1;
        if (_rom.Length == 0)
        {
            _bank2 = 2;
        }
        else
        {
            // Ceiling divide to find last 16KB bank index; keep bank2 at least 2 to match small ROM mirroring
            int lastBank = (_rom.Length + (BankSize - 1)) / BankSize - 1;
            _bank2 = Math.Max(2, lastBank);
        }
        Joypad1 = 0xFF;
        Joypad2 = 0xFF;
    }

    public byte ReadByte(ushort address)
    {
        // 0x0000-0x03FF: First 1KB always mapped to ROM slot 0
        if (address < 0x0400)
        {
            return ReadRomDirect(address);
        }
        // 0x0400-0x3FFF: Bank 0
        if (address < 0x4000)
        {
            return ReadRomBank(_bank0, address);
        }
        // 0x4000-0x7FFF: Bank 1
        if (address < 0x8000)
        {
            return ReadRomBank(_bank1, address - 0x4000);
        }
        // 0x8000-0xBFFF: Bank 2
        if (address < 0xC000)
        {
            return ReadRomBank(_bank2, address - 0x8000);
        }
        // 0xC000-0xFFFF: RAM (mirrored)
        return _ram[address & 0x1FFF];
    }

    public void WriteByte(ushort address, byte value)
    {
        // RAM area
        if (address >= 0xC000)
        {
            _ram[address & 0x1FFF] = value;

            // Sega mapper control registers at end of RAM
            switch (address)
            {
                case 0xFFFC: // RAM/ROM control (ignore for now)
                    break;
                case 0xFFFD: // Bank 0 select
                    _bank0 = value;
                    break;
                case 0xFFFE: // Bank 1 select
                    _bank1 = value;
                    break;
                case 0xFFFF: // Bank 2 select
                    _bank2 = value;
                    break;
            }
        }
    }

    public byte ReadPort(byte port)
    {
        // VDP ports (active when bits 6 set, bit 0 determines data/control)
        if ((port & 0xC0) == 0x80)
        {
            return (port & 1) == 0 ? _vdp.ReadData() : _vdp.ReadStatus();
        }

        // Controller ports
        if ((port & 0xC0) == 0xC0)
        {
            // DC/DD - joystick ports
            if ((port & 1) == 0)
            {
                // Port DC: Joy1 + partial Joy2
                return Joypad1;
            }
            else
            {
                // Port DD: Rest of Joy2 + region
                return (byte)((Joypad2 & 0x0F) | 0xF0);
            }
        }

        return 0xFF;
    }

    public void WritePort(byte port, byte value)
    {
        // VDP ports
        if ((port & 0xC0) == 0x80)
        {
            if ((port & 1) == 0)
                _vdp.WriteData(value);
            else
                _vdp.WriteControl(value);
        }
        // PSG port (0x7F) - sound, ignored for now
    }

    private byte ReadRomDirect(int offset)
    {
        if (_rom.Length == 0) return 0xFF;
        return _rom[offset % _rom.Length];
    }

    private byte ReadRomBank(int bank, int offset)
    {
        if (_rom.Length == 0) return 0xFF;
        int addr = (bank * BankSize + offset) % _rom.Length;
        return _rom[addr];
    }
}
