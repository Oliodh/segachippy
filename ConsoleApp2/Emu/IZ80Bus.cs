namespace ConsoleApp2.Emu;

public interface IZ80Bus
{
    byte ReadByte(ushort address);
    void WriteByte(ushort address, byte value);

    byte ReadPort(byte port);
    void WritePort(byte port, byte value);
}