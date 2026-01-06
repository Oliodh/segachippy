# SegaChippy - Sega Master System Emulator

A Sega Master System (SMS) emulator written in C# with SDL3 graphics.

## Features

- **Complete Z80 CPU** - All main opcodes plus CB/DD/ED/FD prefix instructions
- **VDP (Video Display Processor)** - Mode 4 graphics with background tiles and sprites
- **Sega Mapper** - ROM banking support for games up to 512KB (like Sonic Chaos)
- **Controller Input** - Full gamepad support with keyboard mapping
- **ROM Selector** - Press 'R' to scan for and load ROM files

## Controls

| Key | Action |
|-----|--------|
| Arrow Keys | D-Pad (Up/Down/Left/Right) |
| Z or A | Button 1 |
| X or S | Button 2 |
| Enter | Start/Button 1 |
| R | Open ROM Selector |
| P | Pause/Resume |
| F5 | Reset |
| Escape | Quit |

## Building

Requires .NET 10.0 SDK.

```bash
cd ConsoleApp2
dotnet build
```

## Running

```bash
# Run with a ROM file
dotnet run -- path/to/game.sms

# Run and press 'R' to select ROM
dotnet run
```

You can also drag and drop ROM files onto the window.

## Supported Games

The emulator targets compatibility with Sega Master System games including:
- Sonic the Hedgehog
- Sonic Chaos
- Alex Kidd series
- And many more!

## Technical Details

- **CPU**: Zilog Z80 @ 3.58 MHz (NTSC) / 3.55 MHz (PAL)
- **Video**: TMS9918-derived VDP, 256x192 resolution, 32 colors
- **Audio**: SN76489 PSG (not yet implemented)
- **Region**: PAL (50Hz) by default

## ROM Format

Supports standard `.sms` ROM files. ROMs with 512-byte headers are automatically detected and handled.