using System.Runtime.InteropServices;
using Avalonia.Threading;
using NexStrap.Models;

namespace NexStrap.Services;

public sealed class GlobalHotKeyService : IDisposable
{
    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN     = 0x0100;
    private const int WM_SYSKEYDOWN  = 0x0104;
    private const int VK_CONTROL     = 0x11;
    private const int VK_SHIFT       = 0x10;
    private const int VK_MENU        = 0x12;

    // modifier VK codes — never fire as the "main" key
    private static readonly HashSet<int> ModifierVks = [
        0x10, 0x11, 0x12,               // Shift, Ctrl, Alt
        0xA0, 0xA1, 0xA2, 0xA3, 0xA4, 0xA5  // L/R variants
    ];

    private IntPtr _hook = IntPtr.Zero;
    private LowLevelKeyboardProc? _proc; // keep reference to prevent GC

    private readonly List<(string name, HotKeyBinding binding, Action callback)> _registrations = [];

    public void Install()
    {
        if (_hook != IntPtr.Zero) return;
        _proc = HookCallback;
        _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(null), 0);
    }

    public void Register(string actionName, HotKeyBinding binding, Action callback)
    {
        _registrations.RemoveAll(r => r.name == actionName);
        if (!binding.IsEmpty)
            _registrations.Add((actionName, binding, callback));
    }

    public void Unregister(string actionName)
        => _registrations.RemoveAll(r => r.name == actionName);

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (wParam == WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN))
        {
            int vk = Marshal.ReadInt32(lParam);
            if (!ModifierVks.Contains(vk))
            {
                int mods = 0;
                if ((GetAsyncKeyState(VK_CONTROL) & unchecked((short)0x8000)) != 0) mods |= 2;
                if ((GetAsyncKeyState(VK_SHIFT)   & unchecked((short)0x8000)) != 0) mods |= 4;
                if ((GetAsyncKeyState(VK_MENU)    & unchecked((short)0x8000)) != 0) mods |= 1;

                foreach (var (_, binding, callback) in _registrations)
                {
                    if (binding.VirtualKey == vk && binding.Modifiers == mods)
                        Dispatcher.UIThread.InvokeAsync(callback);
                }
            }
        }
        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
    }
}
