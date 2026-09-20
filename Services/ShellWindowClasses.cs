using System;
using System.Collections.Generic;
using OopsType.Native;

namespace OopsType.Services;

/// <summary>
/// Window classes belonging to shell surfaces that briefly steal the foreground but never represent
/// a place the user is typing: the taskbar, the Win11 flyouts, task view, and the input-switch popup.
///
/// <para>Each runs on a shell UI thread carrying its OWN input locale, unrelated to the app the user
/// was actually working in, so anything that reasons about "the app in front" has to look straight
/// through them. Shared by <see cref="KeyboardLayoutService"/> (which would otherwise invert the
/// indicator colors every time one opens) and by the convert-selection trigger (which would otherwise
/// treat the Win+Space popup as a window switch and refuse to convert).</para>
/// </summary>
internal static class ShellWindowClasses
{
    private static readonly HashSet<string> Transient = new(StringComparer.OrdinalIgnoreCase)
    {
        "Shell_TrayWnd",                        // primary taskbar
        "Shell_SecondaryTrayWnd",               // taskbar on secondary monitors
        "TrayNotifyWnd",                        // notification area / clock region
        "ControlCenterWindow",                  // Win11 quick settings (network / sound / battery)
        "TopLevelWindowForOverflowXamlIsland",  // taskbar corner / system-tray overflow
        "Shell_InputSwitchTopLevelWindow",      // language / input-method switch popup
        "MultitaskingViewFrame",                // task view (Win+Tab)
        "XamlExplorerHostIslandWindow",         // Win11 start / search / widgets host
    };

    internal static bool IsTransient(string? windowClass)
        => windowClass != null && Transient.Contains(windowClass);

    internal static bool IsTransient(IntPtr hwnd)
        => hwnd != IntPtr.Zero && Transient.Contains(NativeMethods.GetWindowClass(hwnd));
}
