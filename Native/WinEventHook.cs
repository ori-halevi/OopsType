using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using OopsType.Infrastructure;

namespace OopsType.Native;

internal sealed class WinEventHook : IDisposable
{
    private readonly NativeMethods.WinEventDelegate _delegate;
    private readonly IErrorReporter _reporter;
    private IntPtr _hook;

    public event Action<uint, IntPtr>? EventRaised;

    public WinEventHook(uint eventMin, uint eventMax, IErrorReporter reporter)
    {
        _reporter = reporter;
        _delegate = OnWinEvent;

        try
        {
            _hook = NativeMethods.SetWinEventHook(eventMin, eventMax, IntPtr.Zero, _delegate,
                0, 0, NativeMethods.WINEVENT_OUTOFCONTEXT | NativeMethods.WINEVENT_SKIPOWNPROCESS);
            if (_hook == IntPtr.Zero)
            {
                var err = Marshal.GetLastWin32Error();
                _reporter.Report("WinEventHook.Install",
                    new Win32Exception(err, $"SetWinEventHook failed (Win32 error {err})"));
            }
        }
        catch (Exception ex)
        {
            _reporter.Report("WinEventHook.Install", ex);
            _hook = IntPtr.Zero;
        }
    }

    /// <summary>True when SetWinEventHook succeeded. False means focus-change tracking is disabled.</summary>
    public bool IsInstalled => _hook != IntPtr.Zero;

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
