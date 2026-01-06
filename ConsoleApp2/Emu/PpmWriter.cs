using System.IO;
using System.Text;

namespace ConsoleApp2.Emu;

public static class PpmWriter
{
    // Writes P6 PPM (binary RGB). Input is RGBA32.
    public static void WriteRgbPpm(string path, int width, int height, byte[] rgba32)
    {
        using var fs = File.Create(path);

        var header = Encoding.ASCII.GetBytes($"P6\n{width} {height}\n255\n");
        fs.Write(header);

        var rgb = new byte[width * height * 3];
        var si = 0;
        var di = 0;

        while (di < rgb.Length && si + 3 < rgba32.Length)
        {
            rgb[di + 0] = rgba32[si + 0];
            rgb[di + 1] = rgba32[si + 1];
            rgb[di + 2] = rgba32[si + 2];
            si += 4;
            di += 3;
        }

        fs.Write(rgb);
    }
}