using System.Runtime.InteropServices;

namespace Translater_App.Helpers;

/// <summary>
/// Global hotkey manager using Win32 RegisterHotKey.
/// </summary>
public sealed class HotKeyManager : IDisposable
{
    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    public const uint MOD_ALT = 0x0001;
    public const uint MOD_CTRL = 0x0002;
    public const uint MOD_SHIFT = 0x0004;
    public const uint MOD_NOREPEAT = 0x4000;

    public const int WM_HOTKEY = 0x0312;

    private readonly IntPtr _hwnd;
    private int _nextId = 1;
    private readonly Dictionary<int, Action> _callbacks = new();

    public HotKeyManager(IntPtr hwnd)
    {
        _hwnd = hwnd;
    }

    /// <summary>
    /// Register a hotkey. Returns the hotkey ID.
    /// </summary>
    public int Register(uint modifiers, uint vk, Action callback)
    {
        var id = _nextId++;
        if (RegisterHotKey(_hwnd, id, modifiers | MOD_NOREPEAT, vk))
        {
            _callbacks[id] = callback;
            return id;
        }
        return -1;
    }

    /// <summary>
    /// Call this from the window message loop when WM_HOTKEY is received.
    /// </summary>
    public void HandleHotKey(int id)
    {
        if (_callbacks.TryGetValue(id, out var callback))
        {
            callback();
        }
    }

    public void Dispose()
    {
        foreach (var id in _callbacks.Keys)
        {
            UnregisterHotKey(_hwnd, id);
        }
        _callbacks.Clear();
    }
}
