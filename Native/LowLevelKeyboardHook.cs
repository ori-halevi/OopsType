using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using OopsType.Infrastructure;

namespace OopsType.Native;

/// <summary>
/// Global low-level keyboard hook. Must be installed from a thread with a message pump (the WPF
/// UI thread). Windows enforces <c>LowLevelHooksTimeout</c> (~300ms) and will silently unhook a
/// slow callback — keep the path through <see cref="HookCallback"/> trivial.
///
/// <para><see cref="EnsureInstalled"/> can be called periodically from a watchdog to recover from
/// (a) initial install failure (transient AV interference, hMod resolution race), and (b) Windows
/// having silently unhooked us because a downstream subscriber blew the timeout budget. Without
/// this, a single bad day's slow UI Automation call would permanently kill keyboard tracking
/// until the user restarts the app.</para>
/// </summary>
internal sealed class LowLevelKeyboardHook : IDisposable
{
    private readonly NativeMethods.LowLevelKeyboardProc _proc;
    private readonly IErrorReporter _reporter;
    private IntPtr _hook;

    public event Action? KeyPressed;

    public LowLevelKeyboardHook(IErrorReporter reporter)
    {
        _reporter = reporter;
        _proc = HookCallback;
        _hook = InstallHook();
    }

    /// <summary>True when SetWindowsHookEx succeeded. False means we silently degrade (no key events).</summary>
    public bool IsInstalled => _hook != IntPtr.Zero;

    /// <summary>
    /// No-op if the hook is currently installed. Otherwise attempts a fresh
    /// <c>SetWindowsHookEx</c>. Errors are reported but never thrown — safe inside a watchdog tick.
    /// </summary>
    public void EnsureInstalled()
    {
        if (_hook != IntPtr.Zero) return;
        _hook = InstallHook();
    }

    private IntPtr InstallHook()
    {
        try
        {
            using var process = Process.GetCurrentProcess();
            var module = process.MainModule;
            // MainModule can legitimately be null on restricted profiles — fall back to a zero
            // hMod, which SetWindowsHookEx still accepts for a global LL hook.
            var hMod = module != null
                ? NativeMethods.GetModuleHandle(module.ModuleName)
                : IntPtr.Zero;

            var hook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, _proc, hMod, 0);
            if (hook == IntPtr.Zero)
            {
                var err = Marshal.GetLastWin32Error();
                _reporter.Report("LowLevelKeyboardHook.Install",
                    new Win32Exception(err, $"SetWindowsHookEx WH_KEYBOARD_LL failed (Win32 error {err})"));
            }
            return hook;
        }
        catch (Exception ex)
        {
            _reporter.Report("LowLevelKeyboardHook.Install", ex);
            return IntPtr.Zero;
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        // CRITICAL: any throw that escapes here propagates into native code via the hook chain.
        // Always return CallNextHookEx so other hooks keep working even if our subscriber blows up.
        try
        {
            if (nCode >= 0)
            {
                int msg = wParam.ToInt32();
                if ((msg == NativeMethods.WM_KEYDOWN || msg == NativeMethods.WM_SYSKEYDOWN)
                    && !IsOwnInjectedKey(lParam))
                    KeyPressed?.Invoke();
            }
        }
        catch (Exception ex)
        {
            _reporter.Report("LowLevelKeyboardHook.Callback", ex);
        }
        return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    /// <summary>
    /// True for keystrokes OopsType itself synthesized (the "convert selection" feature types the
    /// corrected text with SendInput). Those are not user activity: counting them would reset the
    /// idle timer and make the app look busy on its own behalf.
    ///
    /// <para>Reads the single <c>dwExtraInfo</c> field straight out of the KBDLLHOOKSTRUCT rather
    /// than marshalling the whole struct — this runs on every keystroke inside the LL hook, where
    /// the <c>LowLevelHooksTimeout</c> budget makes allocation-free the only acceptable shape.
    /// Only OUR signature is filtered; injected input from other tools (AutoHotkey, on-screen
    /// keyboards) still counts as real typing, because for those it is.</para>
    /// </summary>
    private static bool IsOwnInjectedKey(IntPtr lParam)
    {
        if (lParam == IntPtr.Zero) return false;
        return Marshal.ReadIntPtr(lParam, NativeMethods.KbdLLHookExtraInfoOffset) == NativeMethods.InjectedSignature;
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero)
        {
            try { NativeMethods.UnhookWindowsHookEx(_hook); }
            catch (Exception ex) { _reporter.Report("LowLevelKeyboardHook.Dispose", ex); }
            _hook = IntPtr.Zero;
        }
    }
}
