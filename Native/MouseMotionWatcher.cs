using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Interop;
using System.Windows.Threading;
using OopsType.Infrastructure;

namespace OopsType.Native;

/// <summary>
/// Detects mouse motion and raises <see cref="MouseMoved"/> — the wake signal the mouse-label
/// overlay uses to start following the cursor. Event-driven, so zero work while the mouse is still.
///
/// <para><b>Why Raw Input and not a WH_MOUSE_LL hook:</b> a low-level mouse hook sits on the
/// <i>critical path</i> of every mouse event — the OS calls the hook and waits for it to return
/// before moving the visible cursor. Even a trivial callback on a dedicated thread costs a
/// context-switch round-trip per mouse report (up to 1000/s), so under load the pointer stutters
/// system-wide. Raw input (<c>WM_INPUT</c> + <c>RIDEV_INPUTSINK</c>) is the opposite: the system
/// moves the cursor immediately and merely <i>notifies</i> us asynchronously. We are a passive
/// observer and can never delay the pointer, no matter how busy we are.</para>
///
/// <para>The notification is received on a private STA thread with its own message pump (a hidden
/// <see cref="HwndSource"/> window driven by <see cref="Dispatcher.Run"/>), so motion detection is
/// also independent of UI-thread load. <see cref="MouseMoved"/> therefore fires on that thread, not
/// the UI thread — subscribers must marshal any UI work themselves.</para>
///
/// <para>We don't read the WM_INPUT payload: its arrival alone means "the mouse moved", and the
/// overlay reads the live position via <c>GetCursorPos</c>. <see cref="EnsureInstalled"/> re-registers
/// if registration was lost, mirroring the old hook's watchdog contract.</para>
/// </summary>
internal sealed class MouseMotionWatcher : IDisposable
{
    private readonly IErrorReporter _reporter;
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _ready = new(false);

    // The pump thread's dispatcher and the hidden window that receives WM_INPUT. Assigned on the
    // pump thread before _ready is set; the window is touched only on that thread.
    private Dispatcher? _dispatcher;
    private HwndSource? _source;

    private volatile bool _registered;
    private volatile bool _disposed;

    public event Action? MouseMoved;

    public MouseMotionWatcher(IErrorReporter reporter)
    {
        _reporter = reporter;
        _thread = new Thread(ThreadMain)
        {
            IsBackground = true,
            Name = "OopsType.MouseMotion",
        };
        // STA: the HwndSource window and its dispatcher are WPF UI machinery.
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        // Block until the pump thread has created its window and attempted registration, so
        // IsInstalled is meaningful and the dispatcher is ready for EnsureInstalled/Dispose.
        _ready.Wait();
    }

    /// <summary>True when raw-input registration succeeded. False means we silently degrade (no motion events).</summary>
    public bool IsInstalled => _registered;

    /// <summary>
    /// Re-registers for raw input if registration isn't currently active, on the pump thread.
    /// No-op when already registered or disposed. Safe from a watchdog tick; never throws.
    /// </summary>
    public void EnsureInstalled()
    {
        if (_disposed) return;
        var d = _dispatcher;
        if (d == null || _registered) return;
        d.BeginInvoke(new Action(() =>
        {
            if (!_disposed && !_registered) Register();
        }));
    }

    private void ThreadMain()
    {
        _dispatcher = Dispatcher.CurrentDispatcher;
        try
        {
            // A hidden window (never shown, never given a RootVisual) purely to receive WM_INPUT.
            _source = new HwndSource(new HwndSourceParameters("OopsTypeMouseMotion")
            {
                Width = 1,
                Height = 1,
                WindowStyle = 0, // no WS_VISIBLE — the window is never displayed
            });
            _source.AddHook(WndProc);
            Register();
        }
        catch (Exception ex)
        {
            _reporter.Report("MouseMotionWatcher.Init", ex);
        }
        _ready.Set();

        Dispatcher.Run();

        // Dispatcher shut down (Dispose) — tear down on the SAME thread that set things up.
        Unregister();
        _source?.Dispose();
        _source = null;
    }

    private void Register()
    {
        var source = _source;
        if (source == null) return;
        try
        {
            var devices = new[]
            {
                new NativeMethods.RAWINPUTDEVICE
                {
                    usUsagePage = NativeMethods.HID_USAGE_PAGE_GENERIC,
                    usUsage = NativeMethods.HID_USAGE_GENERIC_MOUSE,
                    dwFlags = NativeMethods.RIDEV_INPUTSINK,
                    hwndTarget = source.Handle,
                },
            };
            if (NativeMethods.RegisterRawInputDevices(devices, 1,
                    (uint)Marshal.SizeOf<NativeMethods.RAWINPUTDEVICE>()))
            {
                _registered = true;
            }
            else
            {
                var err = Marshal.GetLastWin32Error();
                _reporter.Report("MouseMotionWatcher.Register",
                    new Win32Exception(err, $"RegisterRawInputDevices failed (Win32 error {err})"));
            }
        }
        catch (Exception ex)
        {
            _reporter.Report("MouseMotionWatcher.Register", ex);
        }
    }

    private void Unregister()
    {
        if (!_registered) return;
        try
        {
            // RIDEV_REMOVE requires hwndTarget == IntPtr.Zero.
            var devices = new[]
            {
                new NativeMethods.RAWINPUTDEVICE
                {
                    usUsagePage = NativeMethods.HID_USAGE_PAGE_GENERIC,
                    usUsage = NativeMethods.HID_USAGE_GENERIC_MOUSE,
                    dwFlags = NativeMethods.RIDEV_REMOVE,
                    hwndTarget = IntPtr.Zero,
                },
            };
            NativeMethods.RegisterRawInputDevices(devices, 1,
                (uint)Marshal.SizeOf<NativeMethods.RAWINPUTDEVICE>());
        }
        catch (Exception ex)
        {
            _reporter.Report("MouseMotionWatcher.Unregister", ex);
        }
        _registered = false;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_INPUT)
        {
            // Passive observer — raw input never gates the system cursor. Leave handled=false so
            // WPF forwards to DefWindowProc, which performs the WM_INPUT buffer cleanup we skip by
            // not reading the payload (arrival alone means "the mouse moved").
            try { MouseMoved?.Invoke(); }
            catch (Exception ex) { _reporter.Report("MouseMotionWatcher.WndProc", ex); }
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // Ending the dispatcher unblocks Dispatcher.Run, which then unregisters and disposes the
        // window on the pump thread. BeginInvokeShutdown is safe to call from another thread.
        _dispatcher?.BeginInvokeShutdown(DispatcherPriority.Send);
        _thread.Join(TimeSpan.FromSeconds(2));
        _ready.Dispose();
    }
}
