using System;

namespace OopsType.Native;

internal sealed class WinEventHook : IDisposable
{
    private readonly NativeMethods.WinEventDelegate _delegate;
    private IntPtr _hook;

    public event Action<uint, IntPtr>? EventRaised;

    public WinEventHook(uint eventMin, uint eventMax)
    {
        _delegate = OnWinEvent;
        _hook = NativeMethods.SetWinEventHook(eventMin, eventMax, IntPtr.Zero, _delegate,
            0, 0, NativeMethods.WINEVENT_OUTOFCONTEXT | NativeMethods.WINEVENT_SKIPOWNPROCESS);
    }

    private void OnWinEvent(IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        => EventRaised?.Invoke(eventType, hwnd);

    public void Dispose()
    {
        if (_hook != IntPtr.Zero)
        {
            NativeMethods.UnhookWinEvent(_hook);
            _hook = IntPtr.Zero;
        }
    }
}
