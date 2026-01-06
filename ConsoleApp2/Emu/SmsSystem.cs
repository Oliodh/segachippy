using System;

namespace ConsoleApp2.Emu;

public sealed class SmsSystem
{
    private readonly SmsBus _bus;
    private readonly Z80Cpu _cpu;
    private readonly int _cpuCyclesPerFrame;

    public SmsSystem(SmsRegion region)
    {
        Video = new SmsVdp();
        _bus = new SmsBus(Video);
        _cpu = new Z80Cpu(_bus);

        // PAL: 3,546,893 Hz / 50 = 70,938 cycles/frame
        // NTSC: 3,579,545 Hz / 60 = 59,659 cycles/frame
        _cpuCyclesPerFrame = region == SmsRegion.Pal ? 70_938 : 59_659;
    }

    public SmsVdp Video { get; }
    public SmsBus Bus => _bus;
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

    /// <summary>
    /// Set controller input state. Active low (0 = pressed).
    /// Bits: 0=Up, 1=Down, 2=Left, 3=Right, 4=Button1, 5=Button2
    /// </summary>
    public void SetInput(byte joypad1, byte joypad2 = 0xFF)
    {
        _bus.Joypad1 = joypad1;
        _bus.Joypad2 = joypad2;
    }

    public void RunFrame()
    {
        int remaining = _cpuCyclesPerFrame;
        int executed = 0;

        Video.BeginFrame();

        while (remaining > 0)
        {
            int cycles = _cpu.Step();
            if (cycles <= 0) cycles = 1;

            remaining -= cycles;
            executed += cycles;

            Video.StepCpuCycles(cycles);

            // Check for VBlank interrupt
            if (Video.GetInterruptPending() && _cpu.IFF1)
            {
                _cpu.RequestInterrupt();
                Video.ClearInterrupts();
            }
        }

        Video.EndFrame();
        DebugLastFrameCpuCycles = executed;
    }
}