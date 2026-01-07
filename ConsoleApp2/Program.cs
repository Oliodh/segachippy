using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using ConsoleApp2.Emu;
using SDL;

unsafe class Program
{
    private static SDL_Window* _window;
    private static SDL_Renderer* _renderer;
    private static SDL_Texture* _texture;
    private static SmsSystem? _sms;
    private static bool _running = true;
    private static bool _paused = true;
    private static string? _lastRomDir;
    private static SDL_DialogFileFilter[]? _dialogFilters;
    private static GCHandle _dialogFiltersHandle = default;

    private const int ScreenWidth = 256;
    private const int ScreenHeight = 192;
    private const int Scale = 3;

    // Keep allocated memory for file dialog alive until callback completes
    private static nint _dialogFilterNamePtr;
    private static nint _dialogFilterPatternPtr;
    private static nint _dialogDefaultLocationPtr;

    static int Main(string[] args)
    {
        if (!SDL3.SDL_Init(SDL_InitFlags.SDL_INIT_VIDEO))
        {
            Console.WriteLine($"SDL init failed: {SDL3.SDL_GetError()}");
            return 1;
        }

        _window = SDL3.SDL_CreateWindow("SegaChippy - SMS Emulator (Press R to load ROM)"u8,
            ScreenWidth * Scale, ScreenHeight * Scale, 0);
        if (_window == null)
        {
            Console.WriteLine($"Window creation failed: {SDL3.SDL_GetError()}");
            return 1;
        }

        _renderer = SDL3.SDL_CreateRenderer(_window, (byte*)null);
        if (_renderer == null)
        {
            Console.WriteLine($"Renderer creation failed: {SDL3.SDL_GetError()}");
            return 1;
        }

        _texture = SDL3.SDL_CreateTexture(_renderer, SDL_PixelFormat.SDL_PIXELFORMAT_RGBA8888,
            SDL_TextureAccess.SDL_TEXTUREACCESS_STREAMING, ScreenWidth, ScreenHeight);
        if (_texture == null)
        {
            Console.WriteLine($"Texture creation failed: {SDL3.SDL_GetError()}");
            return 1;
        }

        _sms = new SmsSystem(SmsRegion.Pal);

        if (args.Length > 0 && File.Exists(args[0]))
        {
            LoadRom(args[0]);
        }

        var sw = Stopwatch.StartNew();
        double frameTime = 1000.0 / 50.0;
        double nextFrame = sw.Elapsed.TotalMilliseconds;

        while (_running)
        {
            ProcessEvents();

            double now = sw.Elapsed.TotalMilliseconds;
            if (now >= nextFrame)
            {
                if (!_paused && _sms != null)
                {
                    _sms.RunFrame();
                }

                Render();
                nextFrame += frameTime;

                if (now > nextFrame + frameTime * 2)
                {
                    nextFrame = now;
                }
            }
            else
            {
                SDL3.SDL_Delay(1);
            }
        }

        Cleanup();
        return 0;
    }

    static void ProcessEvents()
    {
        SDL_Event e;
        while (SDL3.SDL_PollEvent(&e))
        {
            if (e.type == (uint)SDL_EventType.SDL_EVENT_QUIT)
            {
                _running = false;
            }
            else if (e.type == (uint)SDL_EventType.SDL_EVENT_KEY_DOWN)
            {
                HandleKeyDown(e.key);
            }
            else if (e.type == (uint)SDL_EventType.SDL_EVENT_DROP_FILE)
            {
                string? droppedFile = Marshal.PtrToStringUTF8((nint)e.drop.data);
                if (!string.IsNullOrEmpty(droppedFile))
                {
                    LoadRom(droppedFile);
                }
            }
        }

        UpdateInput();
    }

    static void HandleKeyDown(SDL_KeyboardEvent e)
    {
        if (e.scancode == SDL_Scancode.SDL_SCANCODE_ESCAPE)
            _running = false;
        else if (e.scancode == SDL_Scancode.SDL_SCANCODE_R)
            OpenRomSelector();
        else if (e.scancode == SDL_Scancode.SDL_SCANCODE_P)
        {
            _paused = !_paused;
            UpdateTitle();
        }
        else if (e.scancode == SDL_Scancode.SDL_SCANCODE_F5)
            _sms?.Reset();
    }

    static void UpdateInput()
    {
        if (_sms == null) return;

        int numKeys;
        SDLBool* keys = SDL3.SDL_GetKeyboardState(&numKeys);
        byte joy = 0xFF;

        if (IsKeyPressed(keys, SDL_Scancode.SDL_SCANCODE_UP)) joy &= 0xFE;
        if (IsKeyPressed(keys, SDL_Scancode.SDL_SCANCODE_DOWN)) joy &= 0xFD;
        if (IsKeyPressed(keys, SDL_Scancode.SDL_SCANCODE_LEFT)) joy &= 0xFB;
        if (IsKeyPressed(keys, SDL_Scancode.SDL_SCANCODE_RIGHT)) joy &= 0xF7;
        if (IsKeyPressed(keys, SDL_Scancode.SDL_SCANCODE_Z)) joy &= 0xEF;
        if (IsKeyPressed(keys, SDL_Scancode.SDL_SCANCODE_X)) joy &= 0xDF;
        if (IsKeyPressed(keys, SDL_Scancode.SDL_SCANCODE_A)) joy &= 0xEF;
        if (IsKeyPressed(keys, SDL_Scancode.SDL_SCANCODE_S)) joy &= 0xDF;
        if (IsKeyPressed(keys, SDL_Scancode.SDL_SCANCODE_RETURN)) joy &= 0xEF;

        _sms.SetInput(joy);
    }

    static bool IsKeyPressed(SDLBool* keys, SDL_Scancode code)
    {
        return (bool)keys[(int)code];
    }

    static void OpenRomSelector()
    {
        Console.WriteLine("\n=== Opening ROM File Dialog ===");

        // Free any previously allocated memory
        FreeDialogMemory();

        // Define file filter for SMS ROM files
        SDL_DialogFileFilter filter = new SDL_DialogFileFilter();
        _dialogFilterNamePtr = Marshal.StringToHGlobalAnsi("Sega Master System ROMs");
        _dialogFilterPatternPtr = Marshal.StringToHGlobalAnsi("*.sms");
        filter.name = (byte*)_dialogFilterNamePtr;
        filter.pattern = (byte*)_dialogFilterPatternPtr;

        if (_dialogFiltersHandle.IsAllocated) _dialogFiltersHandle.Free();
        _dialogFilters = new SDL_DialogFileFilter[] { filter };
        _dialogFiltersHandle = GCHandle.Alloc(_dialogFilters, GCHandleType.Pinned);

        // Determine the default location
        string defaultLocation = _lastRomDir ?? Environment.CurrentDirectory;
        _dialogDefaultLocationPtr = Marshal.StringToHGlobalAnsi(defaultLocation);

        // Show the file dialog with function pointer
        SDL_DialogFileFilter* filtersPtr = (SDL_DialogFileFilter*)_dialogFiltersHandle.AddrOfPinnedObject();
        SDL3.SDL_ShowOpenFileDialog(&FileDialogCallback, 0, _window, filtersPtr, 1, (byte*)_dialogDefaultLocationPtr, false);

        // Note: Memory will be freed in the callback or on next dialog open
        // Note: The callback will be invoked asynchronously when the user selects a file
        Console.WriteLine("File dialog opened. Select a ROM file...");
    }

    static void FreeDialogMemory()
    {
        if (_dialogFilterNamePtr != nint.Zero) Marshal.FreeHGlobal(_dialogFilterNamePtr);
        if (_dialogFilterPatternPtr != nint.Zero) Marshal.FreeHGlobal(_dialogFilterPatternPtr);
        if (_dialogDefaultLocationPtr != nint.Zero) Marshal.FreeHGlobal(_dialogDefaultLocationPtr);
        _dialogFilters = null;
        if (_dialogFiltersHandle.IsAllocated) _dialogFiltersHandle.Free();
        _dialogFiltersHandle = default;
        _dialogFilterNamePtr = nint.Zero;
        _dialogFilterPatternPtr = nint.Zero;
        _dialogDefaultLocationPtr = nint.Zero;
    }

    [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvCdecl) })]
    static void FileDialogCallback(nint userdata, byte** filelist, int filter)
    {
        if (filelist == null)
        {
            Console.WriteLine("File dialog error occurred.");
            return;
        }

        if (*filelist == null)
        {
            Console.WriteLine("File dialog cancelled by user.");
            return;
        }

        // Get the first selected file
        string? filePath = Marshal.PtrToStringUTF8((nint)(*filelist));
        if (!string.IsNullOrEmpty(filePath))
        {
            Console.WriteLine($"User selected: {filePath}");
            LoadRom(filePath);
        }

        FreeDialogMemory();
    }

    static void CreateTestRom()
    {
        string testPath = Path.Combine(Path.GetTempPath(), "test.sms");
        byte[] testRom = new byte[0x8000];
        int i = 0;

        void Emit(params byte[] bytes) { foreach (var b in bytes) testRom[i++] = b; }
        void VdpReg(byte reg, byte val) { Emit(0x3E, val, 0xD3, 0xBF, 0x3E, (byte)(0x80 | reg), 0xD3, 0xBF); }
        void VramAddr(ushort addr) { Emit(0x3E, (byte)(addr & 0xFF), 0xD3, 0xBF, 0x3E, (byte)(0x40 | ((addr >> 8) & 0x3F)), 0xD3, 0xBF); }
        void VramWrite(byte val) { Emit(0x3E, val, 0xD3, 0xBE); }
        void CramAddr(byte addr) { Emit(0x3E, addr, 0xD3, 0xBF, 0x3E, 0xC0, 0xD3, 0xBF); }

        Emit(0xF3);
        VdpReg(0, 0x04); VdpReg(1, 0x20); VdpReg(2, 0xFF);
        VdpReg(5, 0xFF); VdpReg(6, 0xFF); VdpReg(7, 0x00);
        VdpReg(8, 0x00); VdpReg(9, 0x00); VdpReg(10, 0xFF);

        CramAddr(0);
        for (int c = 0; c < 16; c++) VramWrite((byte)((c * 4) | ((c * 4) << 2)));
        CramAddr(16);
        for (int c = 0; c < 16; c++) VramWrite((byte)(0x30 | c));

        VramAddr(32);
        for (int r = 0; r < 8; r++) { VramWrite(0xFF); VramWrite(0xFF); VramWrite(0x00); VramWrite(0x00); }
        for (int r = 0; r < 8; r++) { byte p = (r % 2 == 0) ? (byte)0xAA : (byte)0x55; VramWrite(p); VramWrite(p); VramWrite((byte)~p); VramWrite(0x00); }

        VramAddr(0x3800);
        for (int t = 0; t < 32 * 24; t++) { VramWrite((byte)((t % 3 == 0) ? 1 : ((t % 3 == 1) ? 2 : 0))); VramWrite(0x00); }

        VdpReg(1, 0xE0);
        Emit(0xFB);
        int loop = i;
        Emit(0x76, 0xC3, (byte)(loop & 0xFF), (byte)(loop >> 8));

        int ih = 0x0038;
        testRom[ih++] = 0xF5; testRom[ih++] = 0xDB; testRom[ih++] = 0xBF;
        testRom[ih++] = 0xF1; testRom[ih++] = 0xFB; testRom[ih++] = 0xC9;

        File.WriteAllBytes(testPath, testRom);
        Console.WriteLine($"Created: {testPath}");
        LoadRom(testPath);
    }

    static void LoadRom(string path)
    {
        try
        {
            byte[] rom = File.ReadAllBytes(path);
            if (rom.Length % 16384 == 512)
            {
                byte[] stripped = new byte[rom.Length - 512];
                Array.Copy(rom, 512, stripped, 0, stripped.Length);
                rom = stripped;
            }

            _sms?.LoadRom(rom);
            _lastRomDir = Path.GetDirectoryName(path);
            _paused = false;

            Console.WriteLine($"Loaded: {Path.GetFileName(path)} ({rom.Length / 1024}KB)");
            UpdateTitle();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to load ROM: {ex.Message}");
        }
    }

    static void UpdateTitle()
    {
        string status = _paused ? " [PAUSED]" : "";
        byte[] title = System.Text.Encoding.UTF8.GetBytes($"SegaChippy{status}\0");
        fixed (byte* p = title)
        {
            SDL3.SDL_SetWindowTitle(_window, p);
        }
    }

    static void Render()
    {
        if (_sms == null) return;

        nint pixels;
        int pitch;

        if (SDL3.SDL_LockTexture(_texture, null, &pixels, &pitch))
        {
            byte[] fb = _sms.Video.FramebufferRgba32;
            for (int y = 0; y < ScreenHeight; y++)
            {
                Marshal.Copy(fb, y * ScreenWidth * 4, pixels + y * pitch, ScreenWidth * 4);
            }
            SDL3.SDL_UnlockTexture(_texture);
        }

        SDL3.SDL_RenderClear(_renderer);
        SDL3.SDL_RenderTexture(_renderer, _texture, null, null);
        SDL3.SDL_RenderPresent(_renderer);
    }

    static void Cleanup()
    {
        // Free dialog memory if allocated
        FreeDialogMemory();

        if (_texture != null) SDL3.SDL_DestroyTexture(_texture);
        if (_renderer != null) SDL3.SDL_DestroyRenderer(_renderer);
        if (_window != null) SDL3.SDL_DestroyWindow(_window);
        SDL3.SDL_Quit();
    }
}
