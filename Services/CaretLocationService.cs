using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Automation;
using OopsType.Models;
using OopsType.Native;

namespace OopsType.Services;

public sealed class CaretLocationService : ICaretLocationService
{
    // Window classes that indicate a popup/menu/tooltip — never show the label over these.
    private static readonly HashSet<string> SuppressedClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "#32768",            // standard Win32 menu
        "tooltips_class32",
        "BaseBar",
        "MsoCommandBarPopup",
        "MsoCommandBar",
        "DV2ControlHost",    // start menu host
        "Windows.UI.Core.CoreWindow",
        "ApplicationFrameWindow",
        "Shell_TrayWnd",
        "WorkerW",
        "Progman",
    };

    public CaretInfo GetCaretRect()
    {
        var hwnd = NativeMethods.GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return None();

        var cls = NativeMethods.GetWindowClass(hwnd);
        if (SuppressedClasses.Contains(cls)) return None();

        // Some popups (context menus) take focus without becoming foreground — check GUI thread info menu owner too.
        var tid = NativeMethods.GetWindowThreadProcessId(hwnd, IntPtr.Zero);
        var info = new NativeMethods.GUITHREADINFO
        {
            cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.GUITHREADINFO>()
        };
        if (NativeMethods.GetGUIThreadInfo(tid, ref info) && info.hwndMenuOwner != IntPtr.Zero)
            return None();

        if (TryGuiThreadInfoCaret(info, out var r1))
            return new CaretInfo(true, r1, CaretSource.GuiThreadInfo);

        if (TryUiAutomationTextSelection(out var r2))
            return new CaretInfo(true, r2, CaretSource.UiAutomation);

        return None();
    }

    private static bool TryGuiThreadInfoCaret(NativeMethods.GUITHREADINFO info, out Rect rect)
    {
        rect = default;
        if (info.hwndCaret == IntPtr.Zero) return false;
        var c = info.rcCaret;
        var w = c.Right - c.Left;
        var h = c.Bottom - c.Top;
        // A real text caret has zero/tiny width but real height. Reject completely empty rects.
        if (h <= 0) return false;

        var tl = new NativeMethods.POINT { X = c.Left, Y = c.Top };
        var br = new NativeMethods.POINT { X = c.Right, Y = c.Bottom };
        if (!NativeMethods.ClientToScreen(info.hwndCaret, ref tl)) return false;
        if (!NativeMethods.ClientToScreen(info.hwndCaret, ref br)) return false;

        rect = new Rect(tl.X, tl.Y, Math.Max(1, br.X - tl.X), Math.Max(4, br.Y - tl.Y));
        return true;
    }

    /// <summary>
    /// Only succeeds when the focused element exposes TextPattern AND has a real
    /// (non-empty) text selection/caret rect. Never falls back to BoundingRectangle —
    /// that's what makes the label show up on menu items, buttons, etc.
    /// </summary>
    private static bool TryUiAutomationTextSelection(out Rect rect)
    {
        rect = default;
        try
        {
            var focused = AutomationElement.FocusedElement;
            if (focused == null) return false;

            if (!focused.TryGetCurrentPattern(TextPattern.Pattern, out var patternObj)
                || patternObj is not TextPattern tp)
                return false;

            var sel = tp.GetSelection();
            if (sel == null || sel.Length == 0) return false;

            var ranges = sel[0].GetBoundingRectangles();
            if (ranges == null || ranges.Length == 0) return false;

            var r = ranges[0];
            if (r.Width < 0 || r.Height <= 0) return false;
            if (double.IsInfinity(r.X) || double.IsInfinity(r.Y)) return false;

            rect = new Rect(r.X, r.Y, Math.Max(1, r.Width), Math.Max(4, r.Height));
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static CaretInfo None() => new(false, default, CaretSource.None);
}
