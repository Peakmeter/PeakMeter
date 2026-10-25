using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;

namespace PeakMeter;

internal static partial class Native
{
    public const int GWL_EXSTYLE = -20;
    public const int WS_EX_TRANSPARENT = 0x20;
    public const int WS_EX_TOOLWINDOW = 0x80;
    public const int WS_EX_NOACTIVATE = 0x08000000;
    public const int WM_HOTKEY = 0x0312;
    public const uint MOD_ALT = 1, MOD_CONTROL = 2, MOD_SHIFT = 4, MOD_WIN = 8, MOD_NOREPEAT = 0x4000;

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    public static partial nint GetWindowLongPtr(nint hwnd, int index);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    public static partial nint SetWindowLongPtr(nint hwnd, int index, nint value);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool RegisterHotKey(nint hwnd, int id, uint modifiers, uint vk);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool UnregisterHotKey(nint hwnd, int id);

    [LibraryImport("psapi.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool EmptyWorkingSet(nint process);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPos(nint hwnd, nint after, int x, int y, int cx, int cy, uint flags);

    /// <summary>Back on top without moving, resizing, activating or redrawing.</summary>
    public static void KeepOnTop(nint hwnd)
    {
        const uint NOSIZE = 0x1, NOMOVE = 0x2, NOREDRAW = 0x8, NOACTIVATE = 0x10, NOOWNERZORDER = 0x200, NOSENDCHANGING = 0x400;
        if (hwnd != 0) SetWindowPos(hwnd, -1, 0, 0, 0, 0, NOSIZE | NOMOVE | NOREDRAW | NOACTIVATE | NOOWNERZORDER | NOSENDCHANGING);
    }

    [LibraryImport("user32.dll")]
    private static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(nint hwnd, out uint pid);

    public static bool GameRunning() =>
        System.Diagnostics.Process.GetProcessesByName("AION2").Length > 0 || System.Diagnostics.Process.GetProcessesByName("Aion2").Length > 0;

    /// <summary>AION 2 or PeakMeter itself is the window in front.</summary>
    public static bool GameOrMeterInFront()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == 0) return false;
        GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == Environment.ProcessId) return true;
        try
        {
            using var p = System.Diagnostics.Process.GetProcessById((int)pid);
            return p.ProcessName.StartsWith("AION2", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    public static void SetExStyle(nint hwnd, int flag, bool on)
    {
        var style = (int)GetWindowLongPtr(hwnd, GWL_EXSTYLE);
        style = on ? style | flag : style & ~flag;
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, style);
    }
}

/// <summary>System-wide hotkeys through RegisterHotKey — no keyboard hook, nothing that watches game input.</summary>
public sealed class Hotkeys : IDisposable
{
    private readonly nint _hwnd;
    private readonly HwndSource _source;
    private readonly Dictionary<int, Action> _actions = new();
    private int _nextId = 0xB100;

    public Hotkeys(nint hwnd)
    {
        _hwnd = hwnd;
        _source = HwndSource.FromHwnd(hwnd)!;
        _source.AddHook(WndProc);
    }

    public bool Register(ModifierKeys mods, Key key, Action action)
    {
        uint m = Native.MOD_NOREPEAT;
        if (mods.HasFlag(ModifierKeys.Control)) m |= Native.MOD_CONTROL;
        if (mods.HasFlag(ModifierKeys.Shift)) m |= Native.MOD_SHIFT;
        if (mods.HasFlag(ModifierKeys.Alt)) m |= Native.MOD_ALT;
        var id = _nextId++;
        if (!Native.RegisterHotKey(_hwnd, id, m, (uint)KeyInterop.VirtualKeyFromKey(key))) return false;
        _actions[id] = action;
        return true;
    }

    /// <summary>"Ctrl+Shift+D" → registered. False when the text is not a key combination or another app owns it.</summary>
    public bool Register(string gesture, Action action) =>
        TryParse(gesture, out var mods, out var key) && Register(mods, key, action);

    public static bool TryParse(string gesture, out ModifierKeys mods, out Key key)
    {
        mods = ModifierKeys.None;
        key = Key.None;
        if (string.IsNullOrWhiteSpace(gesture)) return false;
        var parts = gesture.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var p in parts[..^1])
        {
            switch (p.ToLowerInvariant())
            {
                case "ctrl" or "strg" or "control": mods |= ModifierKeys.Control; break;
                case "shift" or "umschalt": mods |= ModifierKeys.Shift; break;
                case "alt": mods |= ModifierKeys.Alt; break;
                default: return false;
            }
        }
        var k = parts[^1];
        if (k.Length == 1 && char.IsDigit(k[0])) k = "D" + k;
        return Enum.TryParse(k, true, out key) && key != Key.None;
    }

    public void UnregisterAll()
    {
        foreach (var id in _actions.Keys) Native.UnregisterHotKey(_hwnd, id);
        _actions.Clear();
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == Native.WM_HOTKEY && _actions.TryGetValue((int)wParam, out var action))
        {
            action();
            handled = true;
        }
        return 0;
    }

    public void Dispose()
    {
        foreach (var id in _actions.Keys) Native.UnregisterHotKey(_hwnd, id);
        _actions.Clear();
        _source.RemoveHook(WndProc);
    }
}
