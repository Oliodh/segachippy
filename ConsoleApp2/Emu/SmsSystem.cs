using System;

namespace ConsoleApp2.Emu;

public sealed class SmsSystem
{
    private readonly SmsBus _bus;
    private readonly Z80 _cpu;

    // PAL Master System timing (approx):
    // Z80 clock: 3,546,893 Hz (PAL)
    // Frame rate: 50 Hz
    // Cycles per frame: 3,546,893 / 50 = 70,937.86 -> use 70,938
    private readonly int _cpuCyclesPerFrame;

    public SmsSystem(SmsRegion region)
    {
        Video = new SmsVdp();
        _bus = new SmsBus(Video);
        _cpu = new Z80(_bus);

        _cpuCyclesPerFrame = region == SmsRegion.Pal ? 70_938 : 59_659; // rough NTSC fallback
    }

    public SmsVdp Video { get; }

    public int DebugLastFrameCpuCycles { get; private set; }

    public void LoadRom(ReadOnlySpan<byte> rom)
    {
        _bus.LoadRom(rom);
        Reset();
    }

    public void Reset()
    {
        _bus.Reset();
        Video.Reset();
        _cpu.Reset();
    }

    public void RunFrame()
    {
        var remaining = _cpuCyclesPerFrame;
        var executed = 0;

        Video.BeginFrame();

        while (remaining > 0)
        {
            var cycles = _cpu.Step();
            if (cycles <= 0)
            {
                cycles = 1;
            }

            remaining -= cycles;
            executed += cycles;

            Video.StepCpuCycles(cycles);
        }

        Video.EndFrame();
        DebugLastFrameCpuCycles = executed;
    }
}