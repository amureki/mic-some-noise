using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace MicSomeNoise;

internal static class KeyboardInput
{
    public static bool IsDown(ushort key) => (GetAsyncKeyState(key) & 0x8000) != 0;

    public static void Send(ushort key, bool down)
    {
        if (down)
        {
            GetWindowThreadProcessId(GetForegroundWindow(), out uint process);
            if (process == Environment.ProcessId) throw new InvalidOperationException("Return to the game and use the global play shortcut when automatic push-to-talk is enabled.");
            if (IsDown(key)) throw new InvalidOperationException("Release your push-to-talk key before starting automatic playback.");
        }
        uint scan = MapVirtualKey(key, 4);
        if (scan == 0) throw new InvalidOperationException("This push-to-talk key cannot be mapped on your keyboard. Choose another key.");
        var input = new Input { Type = 1, Data = new InputUnion { Keyboard = new Keyboard { Scan = (ushort)(scan & 0xff), Flags = 8u | (down ? 0u : 2u) | ((scan & 0xff00) == 0xe000 ? 1u : 0u) } } };
        if (SendInput(1, [input], Marshal.SizeOf<Input>()) != 1)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows blocked the push-to-talk key. Check that the game and app run with the same permissions.");
    }
    [StructLayout(LayoutKind.Sequential)] private struct Input { public uint Type; public InputUnion Data; }
    [StructLayout(LayoutKind.Explicit)] private struct InputUnion
    {
        [FieldOffset(0)] public Keyboard Keyboard;
        [FieldOffset(0)] public Mouse Mouse;
    }
    [StructLayout(LayoutKind.Sequential)] private struct Keyboard { public ushort Key, Scan; public uint Flags, Time; public UIntPtr Extra; }
    [StructLayout(LayoutKind.Sequential)] private struct Mouse { public int X, Y; public uint Data, Flags, Time; public UIntPtr Extra; }
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, Input[] inputs, int size);
    [DllImport("user32.dll")] private static extern uint MapVirtualKey(uint code, uint type);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
}
