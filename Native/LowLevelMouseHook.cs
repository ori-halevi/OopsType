using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using OopsType.Infrastructure;

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
    private readonly IErrorReporter _reporter;
    private IntPtr _hook;

    public event Action? MouseMoved;

    public LowLevelMouseHook(IErrorReporter reporter)
    {
        _reporter = reporter;
        _proc = HookCallback;
        _hook = InstallHook();
    }

    /// <summary>True when SetWindowsHookEx succeeded. False means we silently degrade (no mouse events).</summary>
    public bool IsInstalled => _hook != IntPtr.Zero;

    private IntPtr InstallHook()
    {
        try
        {
            using var process = Process.GetCurrentProcess();
            var module = process.MainModule;
            var hMod = module != null
                ? NativeMethods.GetModuleHandle(module.ModuleName)
                : IntPtr.Zero;

            var hook = NativeMethods.SetWindowsHookExMouse(NativeMethods.WH_MOUSE_LL, _proc, hMod, 0);
            if (hook == IntPtr.Zero)
            {
                var err = Marshal.GetLastWin32Error();
                _reporter.Report("LowLevelMouseHook.Install",
                    new Win32Exception(err, $"SetWindowsHookEx WH_MOUSE_LL failed (Win32 error {err})"));
            }
            return hook;
        }
        catch (Exception ex)
        {
            _reporter.Report("LowLevelMouseHook.Install", ex);
            return IntPtr.Zero;
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        // CRITICAL: any throw escaping here corrupts the system hook chain. Always swallow
        // subscriber exceptions and always return CallNextHookEx so other apps' hooks survive.
        try
        {
            if (nCode >= 0 && wParam.ToInt32() == NativeMethods.WM_MOUSEMOVE)
                MouseMoved?.Invoke();
        }
        catch (Exception ex)
        {
            _reporter.Report("LowLevelMouseHook.Callback", ex);
        }
        return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero)
        {
            try { NativeMethods.UnhookWindowsHookEx(_hook); }
            catch (Exception ex) { _reporter.Report("LowLevelMouseHook.Dispose", ex); }
            _hook = IntPtr.Zero;
        }
    }
}
