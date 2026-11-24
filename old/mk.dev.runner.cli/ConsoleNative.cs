using System.Runtime.InteropServices;

namespace mk.dev.runner.cli;

/// <summary>Puts the Windows console into VT mode: colours/cursor codes out, raw keys and mouse codes in.</summary>
internal static class ConsoleNative
{
    const int STD_INPUT_HANDLE = -10, STD_OUTPUT_HANDLE = -11;
    const uint ENABLE_PROCESSED_INPUT = 0x1, ENABLE_LINE_INPUT = 0x2, ENABLE_ECHO_INPUT = 0x4,
               ENABLE_QUICK_EDIT_MODE = 0x40, ENABLE_EXTENDED_FLAGS = 0x80, ENABLE_VIRTUAL_TERMINAL_INPUT = 0x200,
               ENABLE_MOUSE_INPUT = 0x10, ENABLE_WINDOW_INPUT = 0x8;
    const uint ENABLE_PROCESSED_OUTPUT = 0x1, ENABLE_VIRTUAL_TERMINAL_PROCESSING = 0x4, DISABLE_NEWLINE_AUTO_RETURN = 0x8;

    [DllImport("kernel32.dll")] static extern IntPtr GetStdHandle(int n);
    [DllImport("kernel32.dll")] static extern bool GetConsoleMode(IntPtr h, out uint mode);
    [DllImport("kernel32.dll")] static extern bool SetConsoleMode(IntPtr h, uint mode);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern bool ReadConsoleW(IntPtr h, [Out] char[] buf, uint toRead, out uint read, IntPtr control);

    static uint _inMode, _outMode;
    static bool _saved;

    public static bool EnableVtOutput()
    {
        var o = GetStdHandle(STD_OUTPUT_HANDLE);
        if (!GetConsoleMode(o, out var mode)) return false;
        return SetConsoleMode(o, mode | ENABLE_PROCESSED_OUTPUT | ENABLE_VIRTUAL_TERMINAL_PROCESSING);
    }

    public static bool EnterRaw()
    {
        var i = GetStdHandle(STD_INPUT_HANDLE);
        var o = GetStdHandle(STD_OUTPUT_HANDLE);
        if (!GetConsoleMode(i, out _inMode) || !GetConsoleMode(o, out _outMode)) return false;
        _saved = true;
        var inMode = (_inMode & ~(ENABLE_PROCESSED_INPUT | ENABLE_LINE_INPUT | ENABLE_ECHO_INPUT | ENABLE_QUICK_EDIT_MODE | ENABLE_MOUSE_INPUT | ENABLE_WINDOW_INPUT))
                     | ENABLE_EXTENDED_FLAGS | ENABLE_VIRTUAL_TERMINAL_INPUT;
        SetConsoleMode(i, inMode);
        SetConsoleMode(o, _outMode | ENABLE_PROCESSED_OUTPUT | ENABLE_VIRTUAL_TERMINAL_PROCESSING | DISABLE_NEWLINE_AUTO_RETURN);
        return true;
    }

    public static void Restore()
    {
        if (!_saved) return;
        SetConsoleMode(GetStdHandle(STD_INPUT_HANDLE), _inMode | ENABLE_EXTENDED_FLAGS);
        SetConsoleMode(GetStdHandle(STD_OUTPUT_HANDLE), _outMode);
    }

    /// <summary>Blocks until input arrives; returns the raw characters (VT sequences included).</summary>
    public static string Read()
    {
        var buf = new char[256];
        if (!ReadConsoleW(GetStdHandle(STD_INPUT_HANDLE), buf, (uint)buf.Length, out var n, IntPtr.Zero)) return "";
        return new string(buf, 0, (int)n);
    }
}
