using System;
using System.Runtime.CompilerServices;

namespace ConsoleApp2.Emu;

/// <summary>
/// Complete Z80 CPU implementation for SMS emulation.
/// </summary>
public sealed class Z80Cpu
{
    private readonly IZ80Bus _bus;

    // Main registers
    private byte _a, _f, _b, _c, _d, _e, _h, _l;
    // Alternate registers
    private byte _a2, _f2, _b2, _c2, _d2, _e2, _h2, _l2;
    // Index registers
    private ushort _ix, _iy;
    // Other registers
    private ushort _sp, _pc;
    private byte _i, _r;
    // Interrupt flip-flops
    private bool _iff1, _iff2;
    private byte _im;
    private bool _halted;

    private const byte FlagC = 0x01;
    private const byte FlagN = 0x02;
    private const byte FlagPV = 0x04;
    private const byte FlagH = 0x10;
    private const byte FlagZ = 0x40;
    private const byte FlagS = 0x80;

    private static readonly byte[] ParityTable = new byte[256];

    static Z80Cpu()
    {
        for (int i = 0; i < 256; i++)
        {
            int bits = 0, v = i;
            while (v != 0) { bits += v & 1; v >>= 1; }
            ParityTable[i] = (byte)((bits & 1) == 0 ? FlagPV : 0);
        }
    }

    public Z80Cpu(IZ80Bus bus) { _bus = bus; Reset(); }

    public ushort PC => _pc;
    public ushort SP => _sp;
    public bool IFF1 => _iff1;
    public bool Halted => _halted;

    public void Reset()
    {
        _a = _f = _b = _c = _d = _e = _h = _l = 0;
        _a2 = _f2 = _b2 = _c2 = _d2 = _e2 = _h2 = _l2 = 0;
        _ix = _iy = 0; _sp = 0xDFF0; _pc = 0x0000;
        _i = _r = 0; _iff1 = _iff2 = false; _im = 1; _halted = false;
    }

    public void RequestInterrupt()
    {
        if (!_iff1) return;
        _halted = false; _iff1 = _iff2 = false;
        Push16(_pc); _pc = 0x0038;
    }

    public void RequestNmi()
    {
        _halted = false; _iff2 = _iff1; _iff1 = false;
        Push16(_pc); _pc = 0x0066;
    }

    public int Step()
    {
        if (_halted) return 4;
        _r = (byte)((_r & 0x80) | ((_r + 1) & 0x7F));
        return ExecuteMain(FetchByte());
    }

    private int ExecuteMain(byte op)
    {
        switch (op)
        {
            case 0x00: return 4;
            case 0x01: _c = FetchByte(); _b = FetchByte(); return 10;
            case 0x02: WriteByte(BC, _a); return 7;
            case 0x03: SetBC((ushort)(BC + 1)); return 6;
            case 0x04: _b = Inc8(_b); return 4;
            case 0x05: _b = Dec8(_b); return 4;
            case 0x06: _b = FetchByte(); return 7;
            case 0x07: _a = Rlca(_a); return 4;
            case 0x08: Swap(ref _a, ref _a2); Swap(ref _f, ref _f2); return 4;
            case 0x09: SetHL(Add16(HL, BC)); return 11;
            case 0x0A: _a = ReadByte(BC); return 7;
            case 0x0B: SetBC((ushort)(BC - 1)); return 6;
            case 0x0C: _c = Inc8(_c); return 4;
            case 0x0D: _c = Dec8(_c); return 4;
            case 0x0E: _c = FetchByte(); return 7;
            case 0x0F: _a = Rrca(_a); return 4;
            case 0x10: { sbyte d = (sbyte)FetchByte(); _b--; if (_b != 0) { _pc = (ushort)(_pc + d); return 13; } return 8; }
            case 0x11: _e = FetchByte(); _d = FetchByte(); return 10;
            case 0x12: WriteByte(DE, _a); return 7;
            case 0x13: SetDE((ushort)(DE + 1)); return 6;
            case 0x14: _d = Inc8(_d); return 4;
            case 0x15: _d = Dec8(_d); return 4;
            case 0x16: _d = FetchByte(); return 7;
            case 0x17: _a = Rla(_a); return 4;
            case 0x18: { sbyte d = (sbyte)FetchByte(); _pc = (ushort)(_pc + d); return 12; }
            case 0x19: SetHL(Add16(HL, DE)); return 11;
            case 0x1A: _a = ReadByte(DE); return 7;
            case 0x1B: SetDE((ushort)(DE - 1)); return 6;
            case 0x1C: _e = Inc8(_e); return 4;
            case 0x1D: _e = Dec8(_e); return 4;
            case 0x1E: _e = FetchByte(); return 7;
            case 0x1F: _a = Rra(_a); return 4;
            case 0x20: { sbyte d = (sbyte)FetchByte(); if ((_f & FlagZ) == 0) { _pc = (ushort)(_pc + d); return 12; } return 7; }
            case 0x21: _l = FetchByte(); _h = FetchByte(); return 10;
            case 0x22: { ushort addr = FetchWord(); WriteByte(addr, _l); WriteByte((ushort)(addr + 1), _h); return 16; }
            case 0x23: SetHL((ushort)(HL + 1)); return 6;
            case 0x24: _h = Inc8(_h); return 4;
            case 0x25: _h = Dec8(_h); return 4;
            case 0x26: _h = FetchByte(); return 7;
            case 0x27: Daa(); return 4;
            case 0x28: { sbyte d = (sbyte)FetchByte(); if ((_f & FlagZ) != 0) { _pc = (ushort)(_pc + d); return 12; } return 7; }
            case 0x29: SetHL(Add16(HL, HL)); return 11;
            case 0x2A: { ushort addr = FetchWord(); _l = ReadByte(addr); _h = ReadByte((ushort)(addr + 1)); return 16; }
            case 0x2B: SetHL((ushort)(HL - 1)); return 6;
            case 0x2C: _l = Inc8(_l); return 4;
            case 0x2D: _l = Dec8(_l); return 4;
            case 0x2E: _l = FetchByte(); return 7;
            case 0x2F: _a = (byte)~_a; _f |= FlagN | FlagH; return 4;
            case 0x30: { sbyte d = (sbyte)FetchByte(); if ((_f & FlagC) == 0) { _pc = (ushort)(_pc + d); return 12; } return 7; }
            case 0x31: _sp = FetchWord(); return 10;
            case 0x32: WriteByte(FetchWord(), _a); return 13;
            case 0x33: _sp++; return 6;
            case 0x34: WriteByte(HL, Inc8(ReadByte(HL))); return 11;
            case 0x35: WriteByte(HL, Dec8(ReadByte(HL))); return 11;
            case 0x36: WriteByte(HL, FetchByte()); return 10;
            case 0x37: _f = (byte)((_f & (FlagS | FlagZ | FlagPV)) | FlagC); return 4;
            case 0x38: { sbyte d = (sbyte)FetchByte(); if ((_f & FlagC) != 0) { _pc = (ushort)(_pc + d); return 12; } return 7; }
            case 0x39: SetHL(Add16(HL, _sp)); return 11;
            case 0x3A: _a = ReadByte(FetchWord()); return 13;
            case 0x3B: _sp--; return 6;
            case 0x3C: _a = Inc8(_a); return 4;
            case 0x3D: _a = Dec8(_a); return 4;
            case 0x3E: _a = FetchByte(); return 7;
            case 0x3F: _f = (byte)((_f & (FlagS | FlagZ | FlagPV)) ^ FlagC | ((_f & FlagC) != 0 ? FlagH : 0)); return 4;
            case 0x40: return 4;
            case 0x41: _b = _c; return 4;
            case 0x42: _b = _d; return 4;
            case 0x43: _b = _e; return 4;
            case 0x44: _b = _h; return 4;
            case 0x45: _b = _l; return 4;
            case 0x46: _b = ReadByte(HL); return 7;
            case 0x47: _b = _a; return 4;
            case 0x48: _c = _b; return 4;
            case 0x49: return 4;
            case 0x4A: _c = _d; return 4;
            case 0x4B: _c = _e; return 4;
            case 0x4C: _c = _h; return 4;
            case 0x4D: _c = _l; return 4;
            case 0x4E: _c = ReadByte(HL); return 7;
            case 0x4F: _c = _a; return 4;
            case 0x50: _d = _b; return 4;
            case 0x51: _d = _c; return 4;
            case 0x52: return 4;
            case 0x53: _d = _e; return 4;
            case 0x54: _d = _h; return 4;
            case 0x55: _d = _l; return 4;
            case 0x56: _d = ReadByte(HL); return 7;
            case 0x57: _d = _a; return 4;
            case 0x58: _e = _b; return 4;
            case 0x59: _e = _c; return 4;
            case 0x5A: _e = _d; return 4;
            case 0x5B: return 4;
            case 0x5C: _e = _h; return 4;
            case 0x5D: _e = _l; return 4;
            case 0x5E: _e = ReadByte(HL); return 7;
            case 0x5F: _e = _a; return 4;
            case 0x60: _h = _b; return 4;
            case 0x61: _h = _c; return 4;
            case 0x62: _h = _d; return 4;
            case 0x63: _h = _e; return 4;
            case 0x64: return 4;
            case 0x65: _h = _l; return 4;
            case 0x66: _h = ReadByte(HL); return 7;
            case 0x67: _h = _a; return 4;
            case 0x68: _l = _b; return 4;
            case 0x69: _l = _c; return 4;
            case 0x6A: _l = _d; return 4;
            case 0x6B: _l = _e; return 4;
            case 0x6C: _l = _h; return 4;
            case 0x6D: return 4;
            case 0x6E: _l = ReadByte(HL); return 7;
            case 0x6F: _l = _a; return 4;
            case 0x70: WriteByte(HL, _b); return 7;
            case 0x71: WriteByte(HL, _c); return 7;
            case 0x72: WriteByte(HL, _d); return 7;
            case 0x73: WriteByte(HL, _e); return 7;
            case 0x74: WriteByte(HL, _h); return 7;
            case 0x75: WriteByte(HL, _l); return 7;
            case 0x76: _halted = true; return 4;
            case 0x77: WriteByte(HL, _a); return 7;
            case 0x78: _a = _b; return 4;
            case 0x79: _a = _c; return 4;
            case 0x7A: _a = _d; return 4;
            case 0x7B: _a = _e; return 4;
            case 0x7C: _a = _h; return 4;
            case 0x7D: _a = _l; return 4;
            case 0x7E: _a = ReadByte(HL); return 7;
            case 0x7F: return 4;
            case 0x80: Add8(_b); return 4;
            case 0x81: Add8(_c); return 4;
            case 0x82: Add8(_d); return 4;
            case 0x83: Add8(_e); return 4;
            case 0x84: Add8(_h); return 4;
            case 0x85: Add8(_l); return 4;
            case 0x86: Add8(ReadByte(HL)); return 7;
            case 0x87: Add8(_a); return 4;
            case 0x88: Adc8(_b); return 4;
            case 0x89: Adc8(_c); return 4;
            case 0x8A: Adc8(_d); return 4;
            case 0x8B: Adc8(_e); return 4;
            case 0x8C: Adc8(_h); return 4;
            case 0x8D: Adc8(_l); return 4;
            case 0x8E: Adc8(ReadByte(HL)); return 7;
            case 0x8F: Adc8(_a); return 4;
            case 0x90: Sub8(_b); return 4;
            case 0x91: Sub8(_c); return 4;
            case 0x92: Sub8(_d); return 4;
            case 0x93: Sub8(_e); return 4;
            case 0x94: Sub8(_h); return 4;
            case 0x95: Sub8(_l); return 4;
            case 0x96: Sub8(ReadByte(HL)); return 7;
            case 0x97: Sub8(_a); return 4;
            case 0x98: Sbc8(_b); return 4;
            case 0x99: Sbc8(_c); return 4;
            case 0x9A: Sbc8(_d); return 4;
            case 0x9B: Sbc8(_e); return 4;
            case 0x9C: Sbc8(_h); return 4;
            case 0x9D: Sbc8(_l); return 4;
            case 0x9E: Sbc8(ReadByte(HL)); return 7;
            case 0x9F: Sbc8(_a); return 4;
            case 0xA0: And8(_b); return 4;
            case 0xA1: And8(_c); return 4;
            case 0xA2: And8(_d); return 4;
            case 0xA3: And8(_e); return 4;
            case 0xA4: And8(_h); return 4;
            case 0xA5: And8(_l); return 4;
            case 0xA6: And8(ReadByte(HL)); return 7;
            case 0xA7: And8(_a); return 4;
            case 0xA8: Xor8(_b); return 4;
            case 0xA9: Xor8(_c); return 4;
            case 0xAA: Xor8(_d); return 4;
            case 0xAB: Xor8(_e); return 4;
            case 0xAC: Xor8(_h); return 4;
            case 0xAD: Xor8(_l); return 4;
            case 0xAE: Xor8(ReadByte(HL)); return 7;
            case 0xAF: Xor8(_a); return 4;
            case 0xB0: Or8(_b); return 4;
            case 0xB1: Or8(_c); return 4;
            case 0xB2: Or8(_d); return 4;
            case 0xB3: Or8(_e); return 4;
            case 0xB4: Or8(_h); return 4;
            case 0xB5: Or8(_l); return 4;
            case 0xB6: Or8(ReadByte(HL)); return 7;
            case 0xB7: Or8(_a); return 4;
            case 0xB8: Cp8(_b); return 4;
            case 0xB9: Cp8(_c); return 4;
            case 0xBA: Cp8(_d); return 4;
            case 0xBB: Cp8(_e); return 4;
            case 0xBC: Cp8(_h); return 4;
            case 0xBD: Cp8(_l); return 4;
            case 0xBE: Cp8(ReadByte(HL)); return 7;
            case 0xBF: Cp8(_a); return 4;
            case 0xC0: if ((_f & FlagZ) == 0) { _pc = Pop16(); return 11; } return 5;
            case 0xC1: SetBC(Pop16()); return 10;
            case 0xC2: { ushort addr = FetchWord(); if ((_f & FlagZ) == 0) _pc = addr; return 10; }
            case 0xC3: _pc = FetchWord(); return 10;
            case 0xC4: { ushort addr = FetchWord(); if ((_f & FlagZ) == 0) { Push16(_pc); _pc = addr; return 17; } return 10; }
            case 0xC5: Push16(BC); return 11;
            case 0xC6: Add8(FetchByte()); return 7;
            case 0xC7: Push16(_pc); _pc = 0x00; return 11;
            case 0xC8: if ((_f & FlagZ) != 0) { _pc = Pop16(); return 11; } return 5;
            case 0xC9: _pc = Pop16(); return 10;
            case 0xCA: { ushort addr = FetchWord(); if ((_f & FlagZ) != 0) _pc = addr; return 10; }
            case 0xCB: return ExecuteCB();
            case 0xCC: { ushort addr = FetchWord(); if ((_f & FlagZ) != 0) { Push16(_pc); _pc = addr; return 17; } return 10; }
            case 0xCD: { ushort addr = FetchWord(); Push16(_pc); _pc = addr; return 17; }
            case 0xCE: Adc8(FetchByte()); return 7;
            case 0xCF: Push16(_pc); _pc = 0x08; return 11;
            case 0xD0: if ((_f & FlagC) == 0) { _pc = Pop16(); return 11; } return 5;
            case 0xD1: SetDE(Pop16()); return 10;
            case 0xD2: { ushort addr = FetchWord(); if ((_f & FlagC) == 0) _pc = addr; return 10; }
            case 0xD3: _bus.WritePort(FetchByte(), _a); return 11;
            case 0xD4: { ushort addr = FetchWord(); if ((_f & FlagC) == 0) { Push16(_pc); _pc = addr; return 17; } return 10; }
            case 0xD5: Push16(DE); return 11;
            case 0xD6: Sub8(FetchByte()); return 7;
            case 0xD7: Push16(_pc); _pc = 0x10; return 11;
            case 0xD8: if ((_f & FlagC) != 0) { _pc = Pop16(); return 11; } return 5;
            case 0xD9: Swap(ref _b, ref _b2); Swap(ref _c, ref _c2); Swap(ref _d, ref _d2); Swap(ref _e, ref _e2); Swap(ref _h, ref _h2); Swap(ref _l, ref _l2); return 4;
            case 0xDA: { ushort addr = FetchWord(); if ((_f & FlagC) != 0) _pc = addr; return 10; }
            case 0xDB: _a = _bus.ReadPort(FetchByte()); return 11;
            case 0xDC: { ushort addr = FetchWord(); if ((_f & FlagC) != 0) { Push16(_pc); _pc = addr; return 17; } return 10; }
            case 0xDD: return ExecuteDD();
            case 0xDE: Sbc8(FetchByte()); return 7;
            case 0xDF: Push16(_pc); _pc = 0x18; return 11;
            case 0xE0: if ((_f & FlagPV) == 0) { _pc = Pop16(); return 11; } return 5;
            case 0xE1: SetHL(Pop16()); return 10;
            case 0xE2: { ushort addr = FetchWord(); if ((_f & FlagPV) == 0) _pc = addr; return 10; }
            case 0xE3: { ushort t = ReadWord(_sp); WriteWord(_sp, HL); SetHL(t); return 19; }
            case 0xE4: { ushort addr = FetchWord(); if ((_f & FlagPV) == 0) { Push16(_pc); _pc = addr; return 17; } return 10; }
            case 0xE5: Push16(HL); return 11;
            case 0xE6: And8(FetchByte()); return 7;
            case 0xE7: Push16(_pc); _pc = 0x20; return 11;
            case 0xE8: if ((_f & FlagPV) != 0) { _pc = Pop16(); return 11; } return 5;
            case 0xE9: _pc = HL; return 4;
            case 0xEA: { ushort addr = FetchWord(); if ((_f & FlagPV) != 0) _pc = addr; return 10; }
            case 0xEB: { var t = DE; SetDE(HL); SetHL(t); return 4; }
            case 0xEC: { ushort addr = FetchWord(); if ((_f & FlagPV) != 0) { Push16(_pc); _pc = addr; return 17; } return 10; }
            case 0xED: return ExecuteED();
            case 0xEE: Xor8(FetchByte()); return 7;
            case 0xEF: Push16(_pc); _pc = 0x28; return 11;
            case 0xF0: if ((_f & FlagS) == 0) { _pc = Pop16(); return 11; } return 5;
            case 0xF1: { var af = Pop16(); _a = (byte)(af >> 8); _f = (byte)af; return 10; }
            case 0xF2: { ushort addr = FetchWord(); if ((_f & FlagS) == 0) _pc = addr; return 10; }
            case 0xF3: _iff1 = _iff2 = false; return 4;
            case 0xF4: { ushort addr = FetchWord(); if ((_f & FlagS) == 0) { Push16(_pc); _pc = addr; return 17; } return 10; }
            case 0xF5: Push16((ushort)((_a << 8) | _f)); return 11;
            case 0xF6: Or8(FetchByte()); return 7;
            case 0xF7: Push16(_pc); _pc = 0x30; return 11;
            case 0xF8: if ((_f & FlagS) != 0) { _pc = Pop16(); return 11; } return 5;
            case 0xF9: _sp = HL; return 6;
            case 0xFA: { ushort addr = FetchWord(); if ((_f & FlagS) != 0) _pc = addr; return 10; }
            case 0xFB: _iff1 = _iff2 = true; return 4;
            case 0xFC: { ushort addr = FetchWord(); if ((_f & FlagS) != 0) { Push16(_pc); _pc = addr; return 17; } return 10; }
            case 0xFD: return ExecuteFD();
            case 0xFE: Cp8(FetchByte()); return 7;
            case 0xFF: Push16(_pc); _pc = 0x38; return 11;
            default: return 4;
        }
    }

    private int ExecuteCB()
    {
        byte op = FetchByte();
        int reg = op & 0x07;
        int action = op >> 3;
        byte val = ReadReg8(reg);
        int cycles = reg == 6 ? 15 : 8;

        switch (action)
        {
            case 0: val = Rlc(val); break;
            case 1: val = Rrc(val); break;
            case 2: val = Rl(val); break;
            case 3: val = Rr(val); break;
            case 4: val = Sla(val); break;
            case 5: val = Sra(val); break;
            case 6: val = Sll(val); break;
            case 7: val = Srl(val); break;
            default:
                int bit = (action - 8) & 7;
                if (action < 16) { Bit(val, bit); return cycles; }
                else if (action < 24) val = (byte)(val & ~(1 << bit));
                else val = (byte)(val | (1 << bit));
                break;
        }
        WriteReg8(reg, val);
        return cycles;
    }

    private int ExecuteDD() => ExecuteIndexed(FetchByte(), ref _ix);
    private int ExecuteFD() => ExecuteIndexed(FetchByte(), ref _iy);

    private int ExecuteIndexed(byte op, ref ushort idx)
    {
        switch (op)
        {
            case 0x09: idx = Add16(idx, BC); return 15;
            case 0x19: idx = Add16(idx, DE); return 15;
            case 0x21: idx = FetchWord(); return 14;
            case 0x22: { ushort addr = FetchWord(); WriteWord(addr, idx); return 20; }
            case 0x23: idx++; return 10;
            case 0x29: idx = Add16(idx, idx); return 15;
            case 0x2A: { ushort addr = FetchWord(); idx = ReadWord(addr); return 20; }
            case 0x2B: idx--; return 10;
            case 0x34: { ushort addr = (ushort)(idx + (sbyte)FetchByte()); WriteByte(addr, Inc8(ReadByte(addr))); return 23; }
            case 0x35: { ushort addr = (ushort)(idx + (sbyte)FetchByte()); WriteByte(addr, Dec8(ReadByte(addr))); return 23; }
            case 0x36: { ushort addr = (ushort)(idx + (sbyte)FetchByte()); WriteByte(addr, FetchByte()); return 19; }
            case 0x39: idx = Add16(idx, _sp); return 15;
            case 0x46: { sbyte d = (sbyte)FetchByte(); _b = ReadByte((ushort)(idx + d)); return 19; }
            case 0x4E: { sbyte d = (sbyte)FetchByte(); _c = ReadByte((ushort)(idx + d)); return 19; }
            case 0x56: { sbyte d = (sbyte)FetchByte(); _d = ReadByte((ushort)(idx + d)); return 19; }
            case 0x5E: { sbyte d = (sbyte)FetchByte(); _e = ReadByte((ushort)(idx + d)); return 19; }
            case 0x66: { sbyte d = (sbyte)FetchByte(); _h = ReadByte((ushort)(idx + d)); return 19; }
            case 0x6E: { sbyte d = (sbyte)FetchByte(); _l = ReadByte((ushort)(idx + d)); return 19; }
            case 0x7E: { sbyte d = (sbyte)FetchByte(); _a = ReadByte((ushort)(idx + d)); return 19; }
            case 0x70: { sbyte d = (sbyte)FetchByte(); WriteByte((ushort)(idx + d), _b); return 19; }
            case 0x71: { sbyte d = (sbyte)FetchByte(); WriteByte((ushort)(idx + d), _c); return 19; }
            case 0x72: { sbyte d = (sbyte)FetchByte(); WriteByte((ushort)(idx + d), _d); return 19; }
            case 0x73: { sbyte d = (sbyte)FetchByte(); WriteByte((ushort)(idx + d), _e); return 19; }
            case 0x74: { sbyte d = (sbyte)FetchByte(); WriteByte((ushort)(idx + d), _h); return 19; }
            case 0x75: { sbyte d = (sbyte)FetchByte(); WriteByte((ushort)(idx + d), _l); return 19; }
            case 0x77: { sbyte d = (sbyte)FetchByte(); WriteByte((ushort)(idx + d), _a); return 19; }
            case 0x86: { sbyte d = (sbyte)FetchByte(); Add8(ReadByte((ushort)(idx + d))); return 19; }
            case 0x8E: { sbyte d = (sbyte)FetchByte(); Adc8(ReadByte((ushort)(idx + d))); return 19; }
            case 0x96: { sbyte d = (sbyte)FetchByte(); Sub8(ReadByte((ushort)(idx + d))); return 19; }
            case 0x9E: { sbyte d = (sbyte)FetchByte(); Sbc8(ReadByte((ushort)(idx + d))); return 19; }
            case 0xA6: { sbyte d = (sbyte)FetchByte(); And8(ReadByte((ushort)(idx + d))); return 19; }
            case 0xAE: { sbyte d = (sbyte)FetchByte(); Xor8(ReadByte((ushort)(idx + d))); return 19; }
            case 0xB6: { sbyte d = (sbyte)FetchByte(); Or8(ReadByte((ushort)(idx + d))); return 19; }
            case 0xBE: { sbyte d = (sbyte)FetchByte(); Cp8(ReadByte((ushort)(idx + d))); return 19; }
            case 0xCB: return ExecuteIndexedCB(idx);
            case 0xE1: idx = Pop16(); return 14;
            case 0xE3: { ushort t = ReadWord(_sp); WriteWord(_sp, idx); idx = t; return 23; }
            case 0xE5: Push16(idx); return 15;
            case 0xE9: _pc = idx; return 8;
            case 0xF9: _sp = idx; return 10;
            default: return ExecuteMain(op);
        }
    }

    private int ExecuteIndexedCB(ushort idx)
    {
        sbyte d = (sbyte)FetchByte();
        byte op = FetchByte();
        ushort addr = (ushort)(idx + d);
        byte val = ReadByte(addr);
        int action = op >> 3;
        int reg = op & 0x07;
        byte result;

        switch (action)
        {
            case 0: result = Rlc(val); break;
            case 1: result = Rrc(val); break;
            case 2: result = Rl(val); break;
            case 3: result = Rr(val); break;
            case 4: result = Sla(val); break;
            case 5: result = Sra(val); break;
            case 6: result = Sll(val); break;
            case 7: result = Srl(val); break;
            default:
                int bit = (action - 8) & 7;
                if (action < 16) { Bit(val, bit); return 20; }
                else if (action < 24) result = (byte)(val & ~(1 << bit));
                else result = (byte)(val | (1 << bit));
                WriteByte(addr, result);
                if (reg != 6) WriteReg8(reg, result);
                return 23;
        }
        WriteByte(addr, result);
        if (reg != 6) WriteReg8(reg, result);
        return 23;
    }

    private int ExecuteED()
    {
        byte op = FetchByte();
        switch (op)
        {
            case 0x40: _b = In(_c); return 12;
            case 0x41: _bus.WritePort(_c, _b); return 12;
            case 0x42: Sbc16(BC); return 15;
            case 0x43: WriteWord(FetchWord(), BC); return 20;
            case 0x44: { int r = 0 - _a; _f = (byte)(FlagN | (r < 0 ? FlagC : 0) | SZ(r) | ((_a ^ r) & 0x10) | ((_a == 0x80) ? FlagPV : 0)); _a = (byte)r; return 8; }
            case 0x45: _iff1 = _iff2; _pc = Pop16(); return 14;
            case 0x46: _im = 0; return 8;
            case 0x47: _i = _a; return 9;
            case 0x48: _c = In(_c); return 12;
            case 0x49: _bus.WritePort(_c, _c); return 12;
            case 0x4A: Adc16(BC); return 15;
            case 0x4B: SetBC(ReadWord(FetchWord())); return 20;
            case 0x4D: _pc = Pop16(); return 14;
            case 0x4F: _r = _a; return 9;
            case 0x50: _d = In(_c); return 12;
            case 0x51: _bus.WritePort(_c, _d); return 12;
            case 0x52: Sbc16(DE); return 15;
            case 0x53: WriteWord(FetchWord(), DE); return 20;
            case 0x56: _im = 1; return 8;
            case 0x57: _a = _i; _f = (byte)((_f & FlagC) | SZ(_a) | (_iff2 ? FlagPV : 0)); return 9;
            case 0x58: _e = In(_c); return 12;
            case 0x59: _bus.WritePort(_c, _e); return 12;
            case 0x5A: Adc16(DE); return 15;
            case 0x5B: SetDE(ReadWord(FetchWord())); return 20;
            case 0x5E: _im = 2; return 8;
            case 0x5F: _a = _r; _f = (byte)((_f & FlagC) | SZ(_a) | (_iff2 ? FlagPV : 0)); return 9;
            case 0x60: _h = In(_c); return 12;
            case 0x61: _bus.WritePort(_c, _h); return 12;
            case 0x62: Sbc16(HL); return 15;
            case 0x63: WriteWord(FetchWord(), HL); return 20;
            case 0x67: Rrd(); return 18;
            case 0x68: _l = In(_c); return 12;
            case 0x69: _bus.WritePort(_c, _l); return 12;
            case 0x6A: Adc16(HL); return 15;
            case 0x6B: SetHL(ReadWord(FetchWord())); return 20;
            case 0x6F: Rld(); return 18;
            case 0x72: Sbc16(_sp); return 15;
            case 0x73: WriteWord(FetchWord(), _sp); return 20;
            case 0x78: _a = In(_c); return 12;
            case 0x79: _bus.WritePort(_c, _a); return 12;
            case 0x7A: Adc16(_sp); return 15;
            case 0x7B: _sp = ReadWord(FetchWord()); return 20;
            case 0xA0: Ldi(); return 16;
            case 0xA1: Cpi(); return 16;
            case 0xA2: Ini(); return 16;
            case 0xA3: Outi(); return 16;
            case 0xA8: Ldd(); return 16;
            case 0xA9: Cpd(); return 16;
            case 0xAA: Ind(); return 16;
            case 0xAB: Outd(); return 16;
            case 0xB0: Ldi(); if (BC != 0) { _pc -= 2; return 21; } return 16;
            case 0xB1: Cpi(); if (BC != 0 && (_f & FlagZ) == 0) { _pc -= 2; return 21; } return 16;
            case 0xB2: Ini(); if (_b != 0) { _pc -= 2; return 21; } return 16;
            case 0xB3: Outi(); if (_b != 0) { _pc -= 2; return 21; } return 16;
            case 0xB8: Ldd(); if (BC != 0) { _pc -= 2; return 21; } return 16;
            case 0xB9: Cpd(); if (BC != 0 && (_f & FlagZ) == 0) { _pc -= 2; return 21; } return 16;
            case 0xBA: Ind(); if (_b != 0) { _pc -= 2; return 21; } return 16;
            case 0xBB: Outd(); if (_b != 0) { _pc -= 2; return 21; } return 16;
            default: return 8;
        }
    }

    private ushort BC => (ushort)((_b << 8) | _c);
    private ushort DE => (ushort)((_d << 8) | _e);
    private ushort HL => (ushort)((_h << 8) | _l);
    private void SetBC(ushort v) { _b = (byte)(v >> 8); _c = (byte)v; }
    private void SetDE(ushort v) { _d = (byte)(v >> 8); _e = (byte)v; }
    private void SetHL(ushort v) { _h = (byte)(v >> 8); _l = (byte)v; }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private byte FetchByte() => _bus.ReadByte(_pc++);
    private ushort FetchWord() { byte l = FetchByte(); return (ushort)(l | (FetchByte() << 8)); }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private byte ReadByte(ushort addr) => _bus.ReadByte(addr);
    private void WriteByte(ushort addr, byte val) => _bus.WriteByte(addr, val);
    private ushort ReadWord(ushort addr) => (ushort)(ReadByte(addr) | (ReadByte((ushort)(addr + 1)) << 8));
    private void WriteWord(ushort addr, ushort val) { WriteByte(addr, (byte)val); WriteByte((ushort)(addr + 1), (byte)(val >> 8)); }

    private void Push16(ushort v) { _sp--; WriteByte(_sp, (byte)(v >> 8)); _sp--; WriteByte(_sp, (byte)v); }
    private ushort Pop16() { byte l = ReadByte(_sp++); byte h = ReadByte(_sp++); return (ushort)(l | (h << 8)); }

    private byte ReadReg8(int r) => r switch { 0 => _b, 1 => _c, 2 => _d, 3 => _e, 4 => _h, 5 => _l, 6 => ReadByte(HL), _ => _a };
    private void WriteReg8(int r, byte v) { switch (r) { case 0: _b = v; break; case 1: _c = v; break; case 2: _d = v; break; case 3: _e = v; break; case 4: _h = v; break; case 5: _l = v; break; case 6: WriteByte(HL, v); break; default: _a = v; break; } }

    private static void Swap(ref byte a, ref byte b) { (a, b) = (b, a); }
    private byte SZ(int v) => (byte)((v & 0x80) | ((v & 0xFF) == 0 ? FlagZ : 0));

    private byte Inc8(byte v) { byte r = (byte)(v + 1); _f = (byte)((_f & FlagC) | SZ(r) | ((r & 0x0F) == 0 ? FlagH : 0) | (r == 0x80 ? FlagPV : 0)); return r; }
    private byte Dec8(byte v) { byte r = (byte)(v - 1); _f = (byte)((_f & FlagC) | SZ(r) | FlagN | ((v & 0x0F) == 0 ? FlagH : 0) | (v == 0x80 ? FlagPV : 0)); return r; }

    private void Add8(byte v) { int r = _a + v; _f = (byte)(SZ(r) | ((r > 0xFF) ? FlagC : 0) | (((_a ^ r ^ v) & 0x10) != 0 ? FlagH : 0) | (((_a ^ v ^ 0x80) & (_a ^ r) & 0x80) != 0 ? FlagPV : 0)); _a = (byte)r; }
    private void Adc8(byte v) { int c = _f & FlagC; int r = _a + v + c; _f = (byte)(SZ(r) | ((r > 0xFF) ? FlagC : 0) | (((_a ^ r ^ v) & 0x10) != 0 ? FlagH : 0) | (((_a ^ v ^ 0x80) & (_a ^ r) & 0x80) != 0 ? FlagPV : 0)); _a = (byte)r; }
    private void Sub8(byte v) { int r = _a - v; _f = (byte)(SZ(r) | FlagN | ((r < 0) ? FlagC : 0) | (((_a ^ r ^ v) & 0x10) != 0 ? FlagH : 0) | (((_a ^ v) & (_a ^ r) & 0x80) != 0 ? FlagPV : 0)); _a = (byte)r; }
    private void Sbc8(byte v) { int c = _f & FlagC; int r = _a - v - c; _f = (byte)(SZ(r) | FlagN | ((r < 0) ? FlagC : 0) | (((_a ^ r ^ v) & 0x10) != 0 ? FlagH : 0) | (((_a ^ v) & (_a ^ r) & 0x80) != 0 ? FlagPV : 0)); _a = (byte)r; }
    private void And8(byte v) { _a &= v; _f = (byte)(SZ(_a) | FlagH | ParityTable[_a]); }
    private void Xor8(byte v) { _a ^= v; _f = (byte)(SZ(_a) | ParityTable[_a]); }
    private void Or8(byte v) { _a |= v; _f = (byte)(SZ(_a) | ParityTable[_a]); }
    private void Cp8(byte v) { int r = _a - v; _f = (byte)(SZ(r) | FlagN | ((r < 0) ? FlagC : 0) | (((_a ^ r ^ v) & 0x10) != 0 ? FlagH : 0) | (((_a ^ v) & (_a ^ r) & 0x80) != 0 ? FlagPV : 0)); }

    private ushort Add16(ushort a, ushort b) { int r = a + b; _f = (byte)((_f & (FlagS | FlagZ | FlagPV)) | ((r > 0xFFFF) ? FlagC : 0) | (((a ^ r ^ b) & 0x1000) != 0 ? FlagH : 0)); return (ushort)r; }
    private void Adc16(ushort v) { int r = HL + v + (_f & FlagC); _f = (byte)(((r >> 8) & FlagS) | ((r & 0xFFFF) == 0 ? FlagZ : 0) | ((r > 0xFFFF) ? FlagC : 0) | (((HL ^ r ^ v) & 0x1000) != 0 ? FlagH : 0) | (((HL ^ v ^ 0x8000) & (HL ^ r) & 0x8000) != 0 ? FlagPV : 0)); SetHL((ushort)r); }
    private void Sbc16(ushort v) { int r = HL - v - (_f & FlagC); _f = (byte)(((r >> 8) & FlagS) | ((r & 0xFFFF) == 0 ? FlagZ : 0) | FlagN | ((r < 0) ? FlagC : 0) | (((HL ^ r ^ v) & 0x1000) != 0 ? FlagH : 0) | (((HL ^ v) & (HL ^ r) & 0x8000) != 0 ? FlagPV : 0)); SetHL((ushort)r); }

    private byte Rlca(byte v) { byte c = (byte)(v >> 7); _a = (byte)((v << 1) | c); _f = (byte)((_f & (FlagS | FlagZ | FlagPV)) | c); return _a; }
    private byte Rrca(byte v) { byte c = (byte)(v & 1); _a = (byte)((v >> 1) | (c << 7)); _f = (byte)((_f & (FlagS | FlagZ | FlagPV)) | c); return _a; }
    private byte Rla(byte v) { byte c = (byte)(_f & FlagC); _a = (byte)((v << 1) | c); _f = (byte)((_f & (FlagS | FlagZ | FlagPV)) | (v >> 7)); return _a; }
    private byte Rra(byte v) { byte c = (byte)((_f & FlagC) << 7); _a = (byte)((v >> 1) | c); _f = (byte)((_f & (FlagS | FlagZ | FlagPV)) | (v & 1)); return _a; }

    private byte Rlc(byte v) { byte c = (byte)(v >> 7); byte r = (byte)((v << 1) | c); _f = (byte)(SZ(r) | c | ParityTable[r]); return r; }
    private byte Rrc(byte v) { byte c = (byte)(v & 1); byte r = (byte)((v >> 1) | (c << 7)); _f = (byte)(SZ(r) | c | ParityTable[r]); return r; }
    private byte Rl(byte v) { byte c = (byte)(_f & FlagC); byte r = (byte)((v << 1) | c); _f = (byte)(SZ(r) | (v >> 7) | ParityTable[r]); return r; }
    private byte Rr(byte v) { byte c = (byte)((_f & FlagC) << 7); byte r = (byte)((v >> 1) | c); _f = (byte)(SZ(r) | (v & 1) | ParityTable[r]); return r; }
    private byte Sla(byte v) { byte r = (byte)(v << 1); _f = (byte)(SZ(r) | (v >> 7) | ParityTable[r]); return r; }
    private byte Sra(byte v) { byte r = (byte)((v >> 1) | (v & 0x80)); _f = (byte)(SZ(r) | (v & 1) | ParityTable[r]); return r; }
    private byte Sll(byte v) { byte r = (byte)((v << 1) | 1); _f = (byte)(SZ(r) | (v >> 7) | ParityTable[r]); return r; }
    private byte Srl(byte v) { byte r = (byte)(v >> 1); _f = (byte)(SZ(r) | (v & 1) | ParityTable[r]); return r; }
    private void Bit(byte v, int b) { byte r = (byte)(v & (1 << b)); _f = (byte)((_f & FlagC) | FlagH | (r == 0 ? FlagZ | FlagPV : 0) | (r & FlagS)); }

    private void Daa()
    {
        int a = _a; int c = _f & FlagC; int h = _f & FlagH; int n = _f & FlagN;
        int lo = a & 0x0F; int hi = a >> 4;
        if (n == 0) { if (h != 0 || lo > 9) a += 0x06; if (c != 0 || hi > 9 || (hi >= 9 && lo > 9)) { a += 0x60; c = FlagC; } }
        else { if (h != 0) a -= 0x06; if (c != 0) a -= 0x60; }
        _a = (byte)a; _f = (byte)(SZ(_a) | ParityTable[_a] | c | n | ((_a ^ (byte)a) & FlagH));
    }

    private byte In(byte port) { byte v = _bus.ReadPort(port); _f = (byte)((_f & FlagC) | SZ(v) | ParityTable[v]); return v; }

    private void Ldi() { byte v = ReadByte(HL); WriteByte(DE, v); SetHL((ushort)(HL + 1)); SetDE((ushort)(DE + 1)); SetBC((ushort)(BC - 1)); _f = (byte)((_f & (FlagS | FlagZ | FlagC)) | (BC != 0 ? FlagPV : 0)); }
    private void Ldd() { byte v = ReadByte(HL); WriteByte(DE, v); SetHL((ushort)(HL - 1)); SetDE((ushort)(DE - 1)); SetBC((ushort)(BC - 1)); _f = (byte)((_f & (FlagS | FlagZ | FlagC)) | (BC != 0 ? FlagPV : 0)); }
    private void Cpi() { byte v = ReadByte(HL); int r = _a - v; SetHL((ushort)(HL + 1)); SetBC((ushort)(BC - 1)); _f = (byte)(SZ(r) | FlagN | (BC != 0 ? FlagPV : 0) | (_f & FlagC) | (((_a ^ r ^ v) & 0x10) != 0 ? FlagH : 0)); }
    private void Cpd() { byte v = ReadByte(HL); int r = _a - v; SetHL((ushort)(HL - 1)); SetBC((ushort)(BC - 1)); _f = (byte)(SZ(r) | FlagN | (BC != 0 ? FlagPV : 0) | (_f & FlagC) | (((_a ^ r ^ v) & 0x10) != 0 ? FlagH : 0)); }
    private void Ini() { byte v = _bus.ReadPort(_c); WriteByte(HL, v); _b--; SetHL((ushort)(HL + 1)); _f = (byte)(SZ(_b) | FlagN); }
    private void Ind() { byte v = _bus.ReadPort(_c); WriteByte(HL, v); _b--; SetHL((ushort)(HL - 1)); _f = (byte)(SZ(_b) | FlagN); }
    private void Outi() { byte v = ReadByte(HL); _b--; _bus.WritePort(_c, v); SetHL((ushort)(HL + 1)); _f = (byte)(SZ(_b) | FlagN); }
    private void Outd() { byte v = ReadByte(HL); _b--; _bus.WritePort(_c, v); SetHL((ushort)(HL - 1)); _f = (byte)(SZ(_b) | FlagN); }

    private void Rld() { byte m = ReadByte(HL); WriteByte(HL, (byte)((m << 4) | (_a & 0x0F))); _a = (byte)((_a & 0xF0) | (m >> 4)); _f = (byte)((_f & FlagC) | SZ(_a) | ParityTable[_a]); }
    private void Rrd() { byte m = ReadByte(HL); WriteByte(HL, (byte)((_a << 4) | (m >> 4))); _a = (byte)((_a & 0xF0) | (m & 0x0F)); _f = (byte)((_f & FlagC) | SZ(_a) | ParityTable[_a]); }
}
