using System;
using System.Diagnostics;
using System.IO;
using ConsoleApp2.Emu;

if (args.Length == 0)
{
    Console.WriteLine("Usage: ConsoleApp2 <path-to-rom.sms> [frames]");
    return;
}

var romPath = args[0];
var framesToRun = args.Length >= 2 && int.TryParse(args[1], out var f) ? f : 60;

var rom = File.ReadAllBytes(romPath);

var sms = new SmsSystem(region: SmsRegion.Pal);
sms.LoadRom(rom);

Directory.CreateDirectory("frames");

var sw = Stopwatch.StartNew();
for (var i = 0; i < framesToRun; i++)
{
    sms.RunFrame();

    var fb = sms.Video.FramebufferRgba32; // 256*192*4
    var outPath = Path.Combine("frames", $"frame_{i:D4}.ppm");
    PpmWriter.WriteRgbPpm(outPath, 256, 192, fb);

    Console.WriteLine($"Frame {i + 1}/{framesToRun} - cycles={sms.DebugLastFrameCpuCycles}");
}
sw.Stop();

Console.WriteLine($"Done in {sw.Elapsed}.");
