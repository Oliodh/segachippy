using System;

namespace ConsoleApp2.Emu;

/// <summary>
/// SMS VDP (Video Display Processor) with Mode 4 support including sprites.
/// </summary>
public sealed class SmsVdp
{
    private readonly byte[] _vram = new byte[0x4000];
    private readonly byte[] _cram = new byte[0x20];
    private readonly byte[] _regs = new byte[16];

    private ushort _address;
    private byte _controlLatch;
    private bool _controlSecond;
    private byte _readBuffer;

    private byte _status;
    private int _line;
    private int _cyclesInLine;

    // Timing constants (PAL)
    private const int CyclesPerLine = 228;
    private const int HalfCyclesPerLine = CyclesPerLine / 2;
    private const int LinesPerFrame = 313;
    private const int ActiveLines = 192;

    public SmsVdp()
    {
        FramebufferRgba32 = new byte[256 * 192 * 4];
    }

    public byte[] FramebufferRgba32 { get; }
    public bool VBlankInterrupt { get; private set; }
    public bool LineInterrupt { get; private set; }

    public byte ReadHCounter()
    {
        // Map cycles in line (0..CyclesPerLine) to 0..255 range expected by games.
        // Add half a line to round to the nearest integer; port reads are infrequent.
        int h = (int)(((long)_cyclesInLine * 256 + HalfCyclesPerLine) / CyclesPerLine);
        return (byte)h;
    }

    public byte ReadVCounter()
    {
        // Games poll V counter for timing; wrap at 8 bits
        return (byte)_line;
    }

    public void Reset()
    {
        Array.Clear(_vram);
        Array.Clear(_cram);
        Array.Clear(_regs);
        Array.Clear(FramebufferRgba32);

        _address = 0;
        _controlLatch = 0;
        _controlSecond = false;
        _readBuffer = 0;
        _status = 0;
        _line = 0;
        _cyclesInLine = 0;
        VBlankInterrupt = false;
        LineInterrupt = false;

        // Defaults
        _regs[0] = 0x00;
        _regs[1] = 0x00;
        _regs[2] = 0xFF;
    }

    public void BeginFrame()
    {
        _line = 0;
        _cyclesInLine = 0;
        Array.Clear(FramebufferRgba32);
    }

    public void EndFrame()
    {
        // Already rendered per-line
    }

    public void StepCpuCycles(int cycles)
    {
        _cyclesInLine += cycles;

        while (_cyclesInLine >= CyclesPerLine)
        {
            _cyclesInLine -= CyclesPerLine;

            // Render current line if in active display
            if (_line < ActiveLines)
            {
                RenderLine(_line);
            }

            // VBlank at line 192
            if (_line == ActiveLines)
            {
                _status |= 0x80; // VBlank flag
                if ((_regs[1] & 0x20) != 0) // IE bit
                {
                    VBlankInterrupt = true;
                }
            }

            _line++;
            if (_line >= LinesPerFrame)
            {
                _line = 0;
            }
        }
    }

    public bool GetInterruptPending()
    {
        return VBlankInterrupt || LineInterrupt;
    }

    public void ClearInterrupts()
    {
        VBlankInterrupt = false;
        LineInterrupt = false;
    }

    public byte ReadData()
    {
        _controlSecond = false;
        byte result = _readBuffer;
        _readBuffer = _vram[_address & 0x3FFF];
        _address++;
        return result;
    }

    public byte ReadStatus()
    {
        byte s = _status;
        _status = 0;
        _controlSecond = false;
        VBlankInterrupt = false;
        LineInterrupt = false;
        return s;
    }

    public void WriteData(byte value)
    {
        _controlSecond = false;
        _readBuffer = value;

        int code = (_address >> 14) & 0x03;
        if (code == 3)
        {
            // CRAM write
            _cram[_address & 0x1F] = value;
        }
        else
        {
            // VRAM write
            _vram[_address & 0x3FFF] = value;
        }
        _address++;
    }

    public void WriteControl(byte value)
    {
        if (!_controlSecond)
        {
            _controlLatch = value;
            _controlSecond = true;
            _address = (ushort)((_address & 0xFF00) | value);
            return;
        }

        _controlSecond = false;
        _address = (ushort)(_controlLatch | (value << 8));

        int code = (value >> 6) & 0x03;
        if (code == 0)
        {
            // VRAM read - pre-fetch
            _readBuffer = _vram[_address & 0x3FFF];
            _address++;
        }
        else if (code == 2)
        {
            // Register write
            int reg = value & 0x0F;
            if (reg < _regs.Length)
            {
                _regs[reg] = _controlLatch;
            }
        }
    }

    private void RenderLine(int line)
    {
        // Get background scroll
        int scrollX = _regs[8];
        int scrollY = _regs[9];

        // Name table base
        int nameTableBase = ((_regs[2] & 0x0E) << 10) & 0x3800;

        // Background priority array for sprite handling
        Span<bool> bgPriority = stackalloc bool[256];
        Span<byte> lineBuffer = stackalloc byte[256];

        // Render background
        int tileRow = ((line + scrollY) >> 3) & 0x1F;
        int fineY = (line + scrollY) & 0x07;

        for (int col = 0; col < 256; col++)
        {
            int scrolledCol = (col + scrollX) & 0xFF;
            int tileCol = scrolledCol >> 3;
            int fineX = scrolledCol & 0x07;

            int entryAddr = nameTableBase + ((tileRow * 32 + tileCol) * 2);
            byte lo = _vram[entryAddr & 0x3FFF];
            byte hi = _vram[(entryAddr + 1) & 0x3FFF];

            int tileIndex = lo | ((hi & 0x01) << 8);
            int palette = (hi >> 3) & 0x01;
            bool hFlip = (hi & 0x02) != 0;
            bool vFlip = (hi & 0x04) != 0;
            bool priority = (hi & 0x10) != 0;

            int tileY = vFlip ? (7 - fineY) : fineY;
            int tileX = hFlip ? fineX : (7 - fineX);

            int tileBase = (tileIndex * 32) & 0x3FFF;
            int rowAddr = tileBase + tileY * 4;

            byte b0 = _vram[(rowAddr + 0) & 0x3FFF];
            byte b1 = _vram[(rowAddr + 1) & 0x3FFF];
            byte b2 = _vram[(rowAddr + 2) & 0x3FFF];
            byte b3 = _vram[(rowAddr + 3) & 0x3FFF];

            int colorIndex =
                (((b0 >> tileX) & 1) << 0) |
                (((b1 >> tileX) & 1) << 1) |
                (((b2 >> tileX) & 1) << 2) |
                (((b3 >> tileX) & 1) << 3);

            lineBuffer[col] = (byte)(palette * 16 + colorIndex);
            bgPriority[col] = priority && colorIndex != 0;
        }

        // Render sprites
        int spriteBase = ((_regs[5] & 0x7E) << 7) & 0x3F00;
        int patternBase = (_regs[6] & 0x04) != 0 ? 0x2000 : 0;
        bool tallSprites = (_regs[1] & 0x02) != 0;
        int spriteHeight = tallSprites ? 16 : 8;

        int spritesOnLine = 0;
        for (int i = 0; i < 64 && spritesOnLine < 8; i++)
        {
            int y = _vram[(spriteBase + i) & 0x3FFF];
            if (y == 0xD0) break; // End of sprite list

            y++;
            if (y > 240) y -= 256;

            if (line < y || line >= y + spriteHeight) continue;

            spritesOnLine++;

            int x = _vram[(spriteBase + 128 + i * 2) & 0x3FFF];
            int tileIndex = _vram[(spriteBase + 128 + i * 2 + 1) & 0x3FFF];

            if (tallSprites) tileIndex &= 0xFE;

            int spriteY = line - y;
            int tileAddr = patternBase + (tileIndex * 32) + spriteY * 4;

            byte b0 = _vram[(tileAddr + 0) & 0x3FFF];
            byte b1 = _vram[(tileAddr + 1) & 0x3FFF];
            byte b2 = _vram[(tileAddr + 2) & 0x3FFF];
            byte b3 = _vram[(tileAddr + 3) & 0x3FFF];

            for (int sx = 0; sx < 8; sx++)
            {
                int px = x + sx;
                if (px < 0 || px >= 256) continue;

                int bit = 7 - sx;
                int colorIndex =
                    (((b0 >> bit) & 1) << 0) |
                    (((b1 >> bit) & 1) << 1) |
                    (((b2 >> bit) & 1) << 2) |
                    (((b3 >> bit) & 1) << 3);

                if (colorIndex == 0) continue; // Transparent

                // Sprite uses second palette
                if (!bgPriority[px])
                {
                    lineBuffer[px] = (byte)(16 + colorIndex);
                }
            }
        }

        // Write to framebuffer
        int fbOffset = line * 256 * 4;
        for (int x = 0; x < 256; x++)
        {
            uint rgba = ColorFromCram(lineBuffer[x]);
            FramebufferRgba32[fbOffset + 0] = (byte)(rgba >> 24);
            FramebufferRgba32[fbOffset + 1] = (byte)(rgba >> 16);
            FramebufferRgba32[fbOffset + 2] = (byte)(rgba >> 8);
            FramebufferRgba32[fbOffset + 3] = (byte)rgba;
            fbOffset += 4;
        }
    }

    private uint ColorFromCram(int index)
    {
        index &= 0x1F;
        byte v = _cram[index];

        // SMS CRAM: 2 bits per channel (--BBGGRR)
        int r = (v & 0x03);
        int g = (v >> 2) & 0x03;
        int b = (v >> 4) & 0x03;

        byte R = (byte)(r * 85);
        byte G = (byte)(g * 85);
        byte B = (byte)(b * 85);

        return ((uint)R << 24) | ((uint)G << 16) | ((uint)B << 8) | 0xFFu;
    }
}
