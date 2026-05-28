using System;
using System.Diagnostics;

namespace OopsType.Native;

/// <summary>
/// Global low-level mouse hook. Fires <see cref="MouseMoved"/> on every WM_MOUSEMOVE the system
/// dispatches. The hook must be installed from a thread with a message pump (the WPF UI thread is
/// fine); the callback runs on that same thread. Keep the callback trivial — Windows enforces
/// LowLevelHooksTimeout (~300ms by default) and will silently unhook a slow callback, also
/// degrading mouse responsiveness system-wide while it waits.
/// </summary>
internal sealed class LowLevelMouseHook : IDisposable
{
    private readonly NativeMethods.LowLevelMouseProc _proc;
    private IntPtr _hook;

    public event Action? MouseMoved;

    public LowLevelMouseHook()
    {
        _proc = HookCallback;
        using var process = Process.GetCurrentProcess();
        using var module = process.MainModule!;
        _hook = NativeMethods.SetWindowsHookExMouse(NativeMethods.WH_MOUSE_LL, _proc,
            NativeMethods.GetModuleHandle(module.ModuleName), 0);
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && wParam.ToInt32() == NativeMethods.WM_MOUSEMOVE)
            MouseMoved?.Invoke();
        return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
    }
}
