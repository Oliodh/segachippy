using System;

namespace ConsoleApp2.Emu;

// Minimal SMS VDP subset:
// - VRAM (16KB), CRAM (32 bytes)
// - Port 0xBF control writes (very simplified): set VRAM write address
// - Port 0xBE data writes: write into VRAM/CRAM (VRAM only in this minimal build)
// - Renders background using Mode 4 tilemap at name table base from register #2
public sealed class SmsVdp
{
    private readonly byte[] _vram = new byte[0x4000];
    private readonly byte[] _cram = new byte[0x20];

    private readonly byte[] _regs = new byte[16];

    private ushort _address;
    private byte _controlLatch;
    private bool _controlSecond;

    private byte _status;

    private int _cpuCycleAccumulator;

    public SmsVdp()
    {
        FramebufferRgba32 = new byte[256 * 192 * 4];
    }

    public byte[] FramebufferRgba32 { get; }

    public void Reset()
    {
        Array.Clear(_vram);
        Array.Clear(_cram);
        Array.Clear(_regs);

        _address = 0;
        _controlLatch = 0;
        _controlSecond = false;
        _status = 0;
        _cpuCycleAccumulator = 0;

        // Reasonable defaults
        _regs[0] = 0x00;
        _regs[1] = 0x00;
        _regs[2] = 0xFF; // name table base (we'll mask)
    }

    public void BeginFrame()
    {
        // Clear frame (opaque black)
        Array.Clear(FramebufferRgba32);
    }

    public void EndFrame()
    {
        // For now we render once per frame (cheap + simple).
        RenderBackground();
    }

    public void StepCpuCycles(int cycles)
    {
        _cpuCycleAccumulator += cycles;

        // In a more complete emulator, this would advance scanlines and set VBlank, request IRQ, etc.
        // Here we just set VBlank at end-of-frame by EndFrame().
    }

    public byte ReadData()
    {
        // VRAM read buffer behavior omitted; return 0xFF for now.
        return 0xFF;
    }

    public byte ReadStatus()
    {
        var s = _status;
        _status = 0;
        _controlSecond = false;
        return s;
    }

    public void WriteData(byte value)
    {
        // Minimal: treat current address as VRAM write.
        _vram[_address & 0x3FFF] = value;
        _address++;
    }

    public void WriteControl(byte value)
    {
        if (!_controlSecond)
        {
            _controlLatch = value;
            _controlSecond = true;
            return;
        }

        _controlSecond = false;

        // For this minimal build, interpret as:
        // address = (value & 0x3F) << 8 | latch
        // code = value >> 6 (ignored mostly)
        _address = (ushort)(((_uint(value) & 0x3Fu) << 8) | _controlLatch);

        var code = (value >> 6) & 0x03;
        if (code == 2)
        {
            // Register write: latch = data, low nibble of value selects register
            var reg = value & 0x0F;
            if (reg < _regs.Length)
            {
                _regs[reg] = _controlLatch;
            }
        }
        else if (code == 3)
        {
            // CRAM write address in real hardware; omitted for now
        }
    }

    private static uint _uint(byte b) => b;

    private void RenderBackground()
    {
        // Mode 4 name table base: reg2 bits, base = (reg2 & 0x0E) << 10 (commonly)
        // We'll approximate: base = (reg2 & 0x0E) * 0x400
        var nameTableBase = (_regs[2] & 0x0E) * 0x400;

        // Tile pattern base is typically 0x0000 in Mode 4; sprite/scroll regs ignored.
        // Tilemap is 32x28 (or 32x24 visible). We'll render 32x24 tiles for 192 lines.
        for (var ty = 0; ty < 24; ty++)
        {
            for (var tx = 0; tx < 32; tx++)
            {
                var entryAddr = (nameTableBase + ((ty * 32 + tx) * 2)) & 0x3FFF;
                var lo = _vram[entryAddr];
                var hi = _vram[(entryAddr + 1) & 0x3FFF];

                // Mode 4 tile entry: 9-bit tile index + attributes (we ignore many)
                var tileIndex = lo | ((hi & 0x01) << 8);
                var paletteSelect = (hi >> 3) & 0x01; // 0/1
                var hFlip = (hi & 0x02) != 0;
                var vFlip = (hi & 0x04) != 0;

                DrawTile8x8(tx * 8, ty * 8, tileIndex, paletteSelect, hFlip, vFlip);
            }
        }
    }

    private void DrawTile8x8(int dstX, int dstY, int tileIndex, int palette, bool hFlip, bool vFlip)
    {
        // Mode 4 patterns are 32 bytes per tile (4bpp planar):
        // For each row: 4 bytes (bitplanes 0..3)
        var tileBase = (tileIndex * 32) & 0x3FFF;

        for (var row = 0; row < 8; row++)
        {
            var srcRow = vFlip ? (7 - row) : row;

            var b0 = _vram[(tileBase + srcRow * 4 + 0) & 0x3FFF];
            var b1 = _vram[(tileBase + srcRow * 4 + 1) & 0x3FFF];
            var b2 = _vram[(tileBase + srcRow * 4 + 2) & 0x3FFF];
            var b3 = _vram[(tileBase + srcRow * 4 + 3) & 0x3FFF];

            var y = dstY + row;
            if ((uint)y >= 192u)
            {
                continue;
            }

            for (var col = 0; col < 8; col++)
            {
                var bit = hFlip ? col : (7 - col);

                var c =
                    (((b0 >> bit) & 1) << 0) |
                    (((b1 >> bit) & 1) << 1) |
                    (((b2 >> bit) & 1) << 2) |
                    (((b3 >> bit) & 1) << 3);

                var x = dstX + col;
                if ((uint)x >= 256u)
                {
                    continue;
                }

                // Palette index: palette*16 + c. Use CRAM if present, otherwise grayscale ramp.
                var rgba = ColorFromCram(palette * 16 + c);
                var idx = (y * 256 + x) * 4;
                FramebufferRgba32[idx + 0] = (byte)(rgba >> 24);
                FramebufferRgba32[idx + 1] = (byte)(rgba >> 16);
                FramebufferRgba32[idx + 2] = (byte)(rgba >> 8);
                FramebufferRgba32[idx + 3] = (byte)rgba;
            }
        }
    }

    private uint ColorFromCram(int index)
    {
        index &= 0x1F; // 32 entries total in CRAM
        var v = _cram[index];

        // SMS CRAM: 2 bits per channel (BBGGRR?? depending); approximate to RGB:
        var r = (v & 0x03);
        var g = (v >> 2) & 0x03;
        var b = (v >> 4) & 0x03;

        byte R = (byte)(r * 85);
        byte G = (byte)(g * 85);
        byte B = (byte)(b * 85);

        return ((uint)R << 24) | ((uint)G << 16) | ((uint)B << 8) | 0xFFu;
    }
}