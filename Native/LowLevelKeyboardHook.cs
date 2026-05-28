using System;
using System.Diagnostics;

namespace OopsType.Native;

internal sealed class LowLevelKeyboardHook : IDisposable
{
    private readonly NativeMethods.LowLevelKeyboardProc _proc;
    private IntPtr _hook;

    public event Action? KeyPressed;

    public LowLevelKeyboardHook()
    {
        _proc = HookCallback;
        using var process = Process.GetCurrentProcess();
        using var module = process.MainModule!;
        _hook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, _proc,
            NativeMethods.GetModuleHandle(module.ModuleName), 0);
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int msg = wParam.ToInt32();
            if (msg == NativeMethods.WM_KEYDOWN || msg == NativeMethods.WM_SYSKEYDOWN)
                KeyPressed?.Invoke();
        }
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
