using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Text;
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

            // First try the selection range as-is. When the user has actually selected text this
            // yields a real rect. When the caret is collapsed (no selection — the common case while
            // typing), many providers (Chromium, WPF, WinUI, Win32 edit) return ZERO bounding
            // rectangles for the degenerate range, which is why the label used to silently vanish
            // in those text areas. In that case we widen a *clone* to one character so the provider
            // has a glyph to measure, then anchor to the caret edge of that character.
            if (TryRectFromRange(sel[0], out rect)) return true;
            if (TryRectFromCollapsedCaret(sel[0], out rect)) return true;

            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Reads the first bounding rectangle of a text range, rejecting empty/degenerate ones.</summary>
    private static bool TryRectFromRange(TextPatternRange range, out Rect rect)
    {
        rect = default;
        var ranges = range.GetBoundingRectangles();
        if (ranges == null || ranges.Length == 0) return false;

        var r = ranges[0];
        if (r.Width <= 0 || r.Height <= 0) return false;
        if (double.IsInfinity(r.X) || double.IsInfinity(r.Y)) return false;

        rect = new Rect(r.X, r.Y, Math.Max(1, r.Width), Math.Max(4, r.Height));
        return true;
    }

    /// <summary>
    /// Recovers a caret rect from a collapsed (zero-length) selection by expanding a clone to one
    /// character. Tries the character *after* the caret first; if the caret sits at end-of-text
    /// (nothing after it) we expand to the character *before* and anchor to that one's right edge.
    /// The returned rect is collapsed to the caret edge so the chip sits exactly at the caret, not
    /// spread across the measured glyph.
    /// </summary>
    private static bool TryRectFromCollapsedCaret(TextPatternRange selection, out Rect rect)
    {
        rect = default;

        // Character after the caret → anchor to its LEFT edge (where the caret is).
        var forward = selection.Clone();
        forward.ExpandToEnclosingUnit(TextUnit.Character);
        if (TryRectFromRange(forward, out var fr))
        {
            rect = new Rect(fr.X, fr.Y, 1, fr.Height);
            return true;
        }

        // End of text: walk one character back, anchor to its RIGHT edge.
        var backward = selection.Clone();
        if (backward.Move(TextUnit.Character, -1) != 0)
        {
            backward.ExpandToEnclosingUnit(TextUnit.Character);
            if (TryRectFromRange(backward, out var br))
            {
                rect = new Rect(br.X + br.Width, br.Y, 1, br.Height);
                return true;
            }
        }

        return false;
    }

    private static CaretInfo None() => new(false, default, CaretSource.None);
}
