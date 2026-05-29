using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using OopsType.Infrastructure;

namespace OopsType.Native;

internal sealed class WinEventHook : IDisposable
{
    private readonly NativeMethods.WinEventDelegate _delegate;
    private readonly IErrorReporter _reporter;
    private readonly uint _eventMin;
    private readonly uint _eventMax;
    private IntPtr _hook;

    public event Action<uint, IntPtr>? EventRaised;

    public WinEventHook(uint eventMin, uint eventMax, IErrorReporter reporter)
    {
        _reporter = reporter;
        _eventMin = eventMin;
        _eventMax = eventMax;
        _delegate = OnWinEvent;
        _hook = InstallHook();
    }

    /// <summary>True when SetWinEventHook succeeded. False means focus-change tracking is disabled.</summary>
    public bool IsInstalled => _hook != IntPtr.Zero;

    /// <summary>
    /// Re-installs the WinEvent hook if not currently active. Recovers from initial install
    /// failure (e.g., transient session-setup race) or — rarer than LL hooks but still possible —
    /// the system having torn the hook down across a session lock/RDP transition.
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
            var hook = NativeMethods.SetWinEventHook(_eventMin, _eventMax, IntPtr.Zero, _delegate,
                0, 0, NativeMethods.WINEVENT_OUTOFCONTEXT | NativeMethods.WINEVENT_SKIPOWNPROCESS);
            if (hook == IntPtr.Zero)
            {
                var err = Marshal.GetLastWin32Error();
                _reporter.Report("WinEventHook.Install",
                    new Win32Exception(err, $"SetWinEventHook failed (Win32 error {err})"));
            }
            return hook;
        }
        catch (Exception ex)
        {
            _reporter.Report("WinEventHook.Install", ex);
            return IntPtr.Zero;
        }
    }

    private void OnWinEvent(IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        // OUTOFCONTEXT means Windows calls us on our own thread, but if a subscriber throws
        // here the exception still bubbles through SendMessage and can poison the WinEvent
        // dispatch — catch it.
        try { EventRaised?.Invoke(eventType, hwnd); }
        catch (Exception ex) { _reporter.Report("WinEventHook.Callback", ex); }
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero)
        {
            try { NativeMethods.UnhookWinEvent(_hook); }
            catch (Exception ex) { _reporter.Report("WinEventHook.Dispose", ex); }
            _hook = IntPtr.Zero;
        }
    }
}
