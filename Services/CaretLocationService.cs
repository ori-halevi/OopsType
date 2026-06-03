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
    // Our own process id. The caret label is meant for OTHER apps; showing it over our own windows
    // (e.g. the Settings window's offset NumberBoxes, which expose a text caret) is just confusing
    // noise, so we never anchor to a caret that belongs to us.
    private static readonly uint OwnProcessId = (uint)Environment.ProcessId;

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
        var tid = NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == OwnProcessId) return None();
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
    /// character. Tries the character *after* the caret first (anchor its LEFT edge); if the caret
    /// sits at end-of-text we walk one character *back* and anchor that one's RIGHT edge. Either
    /// candidate is rejected unless it lands on the caret's own line — otherwise an empty line
    /// (just pressed Enter) makes the backward walk cross the line break and the chip jumps up to
    /// the end of the previous line. When no character on the line can be measured (a truly blank
    /// line) we fall back to the line rect's leading edge so the chip still tracks the new line.
    /// The returned rect is collapsed to the caret edge so the chip sits exactly at the caret.
    /// </summary>
    private static bool TryRectFromCollapsedCaret(TextPatternRange selection, out Rect rect)
    {
        rect = default;

        // Reference rect for the caret's *own* line. Lets us reject a character rect that belongs
        // to a different line (the cross-line jump described above). Optional — if the provider
        // doesn't support the Line unit we simply proceed without the guard.
        Rect? lineRect = TryLineRect(selection);

        // Character after the caret → anchor to its LEFT edge (where the caret sits).
        var forward = selection.Clone();
        forward.ExpandToEnclosingUnit(TextUnit.Character);
        if (TryRawRect(forward, out var fr) && OnLine(fr, lineRect))
        {
            rect = new Rect(fr.X, fr.Y, 1, Math.Max(4, fr.Height));
            return true;
        }

        // Caret at end of a line's text: walk one character back, anchor its RIGHT edge — but only
        // if it's still on the caret's line (guards the empty-line cross-over).
        var backward = selection.Clone();
        if (backward.Move(TextUnit.Character, -1) != 0)
        {
            backward.ExpandToEnclosingUnit(TextUnit.Character);
            if (TryRawRect(backward, out var br) && OnLine(br, lineRect))
            {
                rect = new Rect(br.X + br.Width, br.Y, 1, Math.Max(4, br.Height));
                return true;
            }
        }

        // Blank line with nothing to measure: anchor to the line's leading edge.
        if (lineRect is { } lr)
        {
            rect = new Rect(lr.X, lr.Y, 1, Math.Max(4, lr.Height));
            return true;
        }

        return false;
    }

    /// <summary>Bounding rect of the caret's line, or null when the Line unit isn't supported.</summary>
    private static Rect? TryLineRect(TextPatternRange selection)
    {
        try
        {
            var line = selection.Clone();
            line.ExpandToEnclosingUnit(TextUnit.Line);
            return TryRawRect(line, out var lr) ? lr : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>First bounding rect of a range, allowing zero width (a degenerate caret/blank-line
    /// rect) but rejecting empty/infinite ones. Unlike <see cref="TryRectFromRange"/> it does not
    /// require a positive width, so it can measure the newline/blank-line position.</summary>
    private static bool TryRawRect(TextPatternRange range, out Rect rect)
    {
        rect = default;
        var ranges = range.GetBoundingRectangles();
        if (ranges == null || ranges.Length == 0) return false;

        var r = ranges[0];
        if (r.Height <= 0 || double.IsInfinity(r.X) || double.IsInfinity(r.Y)) return false;

        rect = new Rect(r.X, r.Y, Math.Max(0, r.Width), r.Height);
        return true;
    }

    /// <summary>True when <paramref name="r"/>'s vertical midpoint falls within the caret line's
    /// span (or when there's no line reference to test against — fail-open).</summary>
    private static bool OnLine(Rect r, Rect? lineRect)
    {
        if (lineRect is not { } lr) return true;
        var mid = r.Y + r.Height / 2;
        return mid >= lr.Y - 1 && mid <= lr.Y + lr.Height + 1;
    }

    private static CaretInfo None() => new(false, default, CaretSource.None);
}
