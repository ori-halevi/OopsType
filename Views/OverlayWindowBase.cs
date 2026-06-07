using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using OopsType.Native;

namespace OopsType.Views;

public abstract class OverlayWindowBase : Window
{
    private IntPtr _hwnd;

    // When set, the overlay resists Windows' "Show Desktop" (Win+D), which otherwise hides every
    // top-level window — including click-through tool windows like ours. Opt-in because the
    // caret/mouse overlays manage their own visibility per-frame and should NOT fight Show Desktop.
    public bool KeepVisibleThroughShowDesktop { get; set; }

    // Our *intended* visibility. The Show-Desktop guard only suppresses external hides while we
    // want to be visible; our own hides (SetVisible(false), e.g. taskbar gone) clear this first
    // so they're still honored. Tracking intent rather than a transient flag makes the guard
    // robust regardless of whether WPF dispatches the hide synchronously.
    private bool _wantVisible;
    // DPI is read on every move; resolving it via PresentationSource.FromVisual walks the visual
    // tree and is wasteful for a per-frame call. Cache it and invalidate on DpiChanged (which
    // fires when the window crosses monitors with different DPIs — required for correctness on
    // mixed-DPI setups, not just an optimization).
    private (double X, double Y)? _dpiCache;

    protected OverlayWindowBase()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = System.Windows.Media.Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Focusable = false;
        IsHitTestVisible = false;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.Manual;
        // Start with a tiny non-zero size offscreen — pure NaN size breaks initial composition on some setups.
        Width = 1; Height = 1;
        Left = -32000; Top = -32000;
        SourceInitialized += OnSourceInitialized;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _hwnd = new WindowInteropHelper(this).Handle;
        var ex = NativeMethods.GetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE);
        ex |= NativeMethods.WS_EX_LAYERED
            | NativeMethods.WS_EX_TRANSPARENT
            | NativeMethods.WS_EX_TOOLWINDOW
            | NativeMethods.WS_EX_NOACTIVATE;
        NativeMethods.SetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE, ex);

        HwndSource.FromHwnd(_hwnd)?.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_WINDOWPOSCHANGING && KeepVisibleThroughShowDesktop && _wantVisible)
        {
            // "Show Desktop" (Win+D) hides every top-level window by issuing an SWP_HIDEWINDOW.
            // Strip that flag so the overlay stays put. Our own intentional hides go through
            // SetVisible(false), which clears _wantVisible first, so those are still honored.
            var pos = Marshal.PtrToStructure<NativeMethods.WINDOWPOS>(lParam);
            if ((pos.flags & NativeMethods.SWP_HIDEWINDOW) != 0)
            {
                pos.flags &= ~NativeMethods.SWP_HIDEWINDOW;
                Marshal.StructureToPtr(pos, lParam, false);
            }
        }
        return IntPtr.Zero;
    }

    /// <summary>Force Z-order to topmost (above the taskbar, which is itself topmost).</summary>
    public void EnsureTopmost()
    {
        if (_hwnd == IntPtr.Zero) return;
        // Re-arm WS_EX_TOPMOST in case a prior EnsureBehindTaskbar call had cleared it (legacy
        // fallback path) or the placement was toggled from "behind" back to "top".
        var ex = NativeMethods.GetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE);
        if ((ex & NativeMethods.WS_EX_TOPMOST) == 0)
            NativeMethods.SetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE, ex | NativeMethods.WS_EX_TOPMOST);
        NativeMethods.SetWindowPos(_hwnd, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
    }

    /// <summary>
    /// Place the window directly BEHIND the given taskbar in Z-order, so the taskbar's Win11
    /// translucency shows the strip's color through it. The window stays topmost (the taskbar is
    /// itself topmost): inserting right after the taskbar keeps the strip in the topmost band but
    /// below the taskbar, which means no ordinary window can wedge itself between the two. Without
    /// this — i.e. if the strip were merely non-topmost — any window the user drags low enough
    /// would slide on top of it. The caller passes the specific taskbar HWND to anchor against, so
    /// strips on secondary monitors sit behind their own taskbar rather than the primary one.
    /// </summary>
    public void EnsureBehindTaskbar(IntPtr taskbar)
    {
        if (_hwnd == IntPtr.Zero) return;

        if (taskbar == IntPtr.Zero)
        {
            // No taskbar to anchor against — fall back to plain topmost so the strip stays visible.
            EnsureTopmost();
            return;
        }

        var ex = NativeMethods.GetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE);
        if ((ex & NativeMethods.WS_EX_TOPMOST) == 0)
            NativeMethods.SetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE, ex | NativeMethods.WS_EX_TOPMOST);

        NativeMethods.SetWindowPos(_hwnd, taskbar, 0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
    }

    public void PositionInScreenPixels(double xPx, double yPx)
    {
        var dpi = GetDpi();
        Left = xPx / dpi.X;
        Top = yPx / dpi.Y;
    }

    public void PositionInScreenPixels(double xPx, double yPx, double widthPx, double heightPx)
    {
        var dpi = GetDpi();
        Left = xPx / dpi.X;
        Top = yPx / dpi.Y;
        Width = Math.Max(1, widthPx / dpi.X);
        Height = Math.Max(1, heightPx / dpi.Y);
    }

    public void ShowOverlay()
    {
        _wantVisible = true;
        if (Visibility != Visibility.Visible) Show();
    }

    /// <summary>
    /// Set visibility while recording the intent, so the Show-Desktop guard knows whether an
    /// incoming hide is ours (allow) or Windows' (suppress). Use this instead of assigning
    /// <see cref="UIElement.Visibility"/> directly when <see cref="KeepVisibleThroughShowDesktop"/>
    /// is in play.
    /// </summary>
    public void SetVisible(bool visible)
    {
        _wantVisible = visible;
        Visibility = visible ? Visibility.Visible : Visibility.Hidden;
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        _dpiCache = null;
    }

    private (double X, double Y) GetDpi()
    {
        if (_dpiCache is { } cached) return cached;

        var src = PresentationSource.FromVisual(this);
        if (src?.CompositionTarget != null)
        {
            var m = src.CompositionTarget.TransformToDevice;
            var v = (m.M11, m.M22);
            _dpiCache = v;
            return v;
        }
        // Pre-Show: PresentationSource is null. Don't cache the fallback or we'd be stuck at 1.0
        // forever; let the next call re-resolve.
        return (1.0, 1.0);
    }
}
