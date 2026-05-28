using System;
using System.Windows;
using OopsType.Native;

namespace OopsType.Services;

public sealed class TaskbarService : ITaskbarService
{
    public bool TryGetPrimaryTaskbarRect(out Rect rect)
    {
        rect = default;
        var hwnd = NativeMethods.FindWindow("Shell_TrayWnd", null);
        if (hwnd == IntPtr.Zero) return false;
        if (!NativeMethods.GetWindowRect(hwnd, out var r)) return false;
        rect = new Rect(r.Left, r.Top, Math.Max(0, r.Right - r.Left), Math.Max(0, r.Bottom - r.Top));
        return rect.Width > 0 && rect.Height > 0;
    }
}
