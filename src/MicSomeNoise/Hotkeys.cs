using System;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;

namespace MicSomeNoise;

public sealed class Hotkeys : IDisposable
{
    private readonly HwndSource source;
    private (uint Modifiers, uint Key)[] registered = [];
    public event Action? Open;
    public event Action? Play;
    public event Action? Next;
    public event Action? Previous;
    public event Action? VolumeDown;
    public event Action? VolumeUp;
    public Hotkeys(IntPtr window) { source = HwndSource.FromHwnd(window)!; source.AddHook(Hook); }
    public void Apply(string open, string play, string next, string volumeDown, string volumeUp, string previous)
    {
        var shortcuts = new[] { Parse(open), Parse(play), Parse(next), Parse(volumeDown), Parse(volumeUp), Parse(previous) };
        if (shortcuts.Distinct().Count() != shortcuts.Length)
            throw new InvalidOperationException("All hotkeys must be different.");
        string[] names = ["Open", "Play", "Next track", "Volume down", "Volume up", "Previous track"];
        for (int i = 1; i <= 6; i++) UnregisterHotKey(source.Handle, i);
        try
        {
            for (int i = 0; i < shortcuts.Length; i++)
                if (!RegisterHotKey(source.Handle, i + 1, shortcuts[i].Modifiers | 0x4000, shortcuts[i].Key))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), names[i] + " hotkey is unavailable. Choose another shortcut.");
            registered = shortcuts;
        }
        catch (Exception error)
        {
            for (int i = 1; i <= 6; i++) UnregisterHotKey(source.Handle, i);
            bool restored = true;
            for (int i = 0; i < registered.Length; i++)
                restored &= RegisterHotKey(source.Handle, i + 1, registered[i].Modifiers | 0x4000, registered[i].Key);
            if (!restored) throw new InvalidOperationException(error.Message + " Previous shortcuts could not all be restored. Apply available shortcuts in Settings.", error);
            throw;
        }
    }
    private static (uint Modifiers, uint Key) Parse(string value)
    {
        string[] parts = value.Split('+', StringSplitOptions.TrimEntries);
        uint modifiers = 0;
        foreach (string part in parts[..^1]) modifiers |= part.ToLowerInvariant() switch { "ctrl" => 2u, "alt" => 1u, "shift" => 4u, "win" => 8u, _ => throw new InvalidOperationException("Use Ctrl, Alt, Shift, or Win in shortcuts.") };
        if (!Enum.TryParse<Key>(parts[^1], true, out var key) || !Enum.IsDefined(key) || key is Key.None or Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin || int.TryParse(parts[^1], out _))
            throw new InvalidOperationException("Use a key such as F8, F9, or a shortcut such as Ctrl+Alt+Space.");
        return (modifiers, (uint)KeyInterop.VirtualKeyFromKey(key));
    }
    public static ushort ValidateHoldKey(string value, string action, params string[] shortcuts)
    {
        var parsed = Parse(value);
        if (parsed.Modifiers != 0 || !(parsed.Key is >= 0x30 and <= 0x5a or >= 0x70 and <= 0x87 or 0x20))
            throw new InvalidOperationException($"Choose a single letter, digit (D0–D9), function key, or Space for {action}. Modifier keys and chords are not supported.");
        if (shortcuts.Select(shortcut => Parse(shortcut).Key).Contains(parsed.Key))
            throw new InvalidOperationException($"{action} conflicts with another shortcut. Choose a different key.");
        return (ushort)parsed.Key;
    }
    private IntPtr Hook(
IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == 0x312)
        {
            switch (wParam.ToInt32())
            {
                case 1: handled = true; Open?.Invoke(); break;
                case 2: handled = true; Play?.Invoke(); break;
                case 3: handled = true; Next?.Invoke(); break;
                case 4: handled = true; VolumeDown?.Invoke(); break;
                case 5: handled = true; VolumeUp?.Invoke(); break;
                case 6: handled = true; Previous?.Invoke(); break;
            }
        }
        return IntPtr.Zero;
    }
    public void Dispose() { for (int i = 1; i <= 6; i++) UnregisterHotKey(source.Handle, i); source.RemoveHook(Hook); }
    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
}
