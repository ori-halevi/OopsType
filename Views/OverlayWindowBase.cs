using System;
using System.Windows;
using System.Windows.Interop;
using OopsType.Native;

namespace OopsType.Views;

public abstract class OverlayWindowBase : Window
{
    private IntPtr _hwnd;

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
    }

    /// <summary>Force Z-order to topmost (above the taskbar, which is itself topmost).</summary>
    public void EnsureTopmost()
    {
        if (_hwnd == IntPtr.Zero) return;
        NativeMethods.SetWindowPos(_hwnd, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
    }

    /// <summary>
    /// Drop WS_EX_TOPMOST so the window sits BELOW the taskbar (taskbar is topmost).
    /// Visible only through the taskbar's translucency on Win11.
    /// </summary>
    public void EnsureBehindTaskbar()
    {
        if (_hwnd == IntPtr.Zero) return;
        var ex = NativeMethods.GetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE);
        ex &= ~NativeMethods.WS_EX_TOPMOST;
        NativeMethods.SetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE, ex);
        NativeMethods.SetWindowPos(_hwnd, NativeMethods.HWND_NOTOPMOST, 0, 0, 0, 0,
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
        if (Visibility != Visibility.Visible) Show();
    }

    private (double X, double Y) GetDpi()
    {
        var src = PresentationSource.FromVisual(this);
        if (src?.CompositionTarget != null)
        {
            var m = src.CompositionTarget.TransformToDevice;
            return (m.M11, m.M22);
        }
        return (1.0, 1.0);
    }
}
