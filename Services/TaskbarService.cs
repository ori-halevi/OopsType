using System;
using System.Collections.Generic;
using System.Windows;
using OopsType.Native;

namespace OopsType.Services;

public sealed class TaskbarService : ITaskbarService
{
    public IReadOnlyList<TaskbarInfo> GetAllTaskbars()
    {
        var taskbars = new List<TaskbarInfo>(2);

        if (TryGetInfo(NativeMethods.FindWindow("Shell_TrayWnd", null), out var primary))
            taskbars.Add(primary);

        // Secondary taskbars all share the class "Shell_SecondaryTrayWnd" (one per extra monitor).
        // FindWindowEx with a null parent walks top-level windows; feeding the previous match back
        // as hwndChildAfter advances the search, so the loop visits each one exactly once.
        var hwnd = IntPtr.Zero;
        while ((hwnd = NativeMethods.FindWindowEx(IntPtr.Zero, hwnd, "Shell_SecondaryTrayWnd", null)) != IntPtr.Zero)
        {
            if (TryGetInfo(hwnd, out var secondary))
                taskbars.Add(secondary);
        }

        return taskbars;
    }

    private static bool TryGetInfo(IntPtr hwnd, out TaskbarInfo info)
    {
        info = default;
        if (hwnd == IntPtr.Zero) return false;
        if (!NativeMethods.GetWindowRect(hwnd, out var r)) return false;
        var rect = new Rect(r.Left, r.Top, Math.Max(0, r.Right - r.Left), Math.Max(0, r.Bottom - r.Top));
        if (rect.Width <= 0 || rect.Height <= 0) return false;
        info = new TaskbarInfo(hwnd, rect);
        return true;
    }
}
