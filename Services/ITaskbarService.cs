using System;
using System.Collections.Generic;
using System.Windows;

namespace OopsType.Services;

/// <summary>
/// A live taskbar window and its screen-pixel bounds. <see cref="Hwnd"/> is the shell tray window
/// — the primary <c>Shell_TrayWnd</c> or a per-monitor <c>Shell_SecondaryTrayWnd</c> — used to
/// anchor the strip directly behind that specific taskbar in Z-order.
/// </summary>
public readonly record struct TaskbarInfo(IntPtr Hwnd, Rect Rect);

public interface ITaskbarService
{
    /// <summary>
    /// Enumerates every taskbar currently present: the primary one plus one per secondary monitor.
    /// Secondary taskbars only exist while Windows' "Show my taskbar on all displays" setting is on
    /// — when it's off the shell destroys their windows, so they simply don't appear here and the
    /// strip is naturally absent on those monitors without any explicit setting check.
    /// </summary>
    IReadOnlyList<TaskbarInfo> GetAllTaskbars();
}
