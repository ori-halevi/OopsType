using System;
using System.Windows.Automation;
using System.Windows.Automation.Text;
using OopsType.Infrastructure;

namespace OopsType.Services.TextFix;

/// <summary>
/// Re-selects the text a conversion just inserted, so one more language switch converts it straight
/// back — the gesture that stands in for an undo stack.
///
/// <para><b>Why not just Shift+Left.</b> Arrow keys are the obvious way to walk a selection back over
/// what was typed, and they work in classic Win32 edit controls. But their meaning in bidirectional
/// text is up to the control: some move the caret logically (previous character), others visually
/// (leftwards on screen), and in right-to-left text those are opposite directions. Converting INTO
/// Hebrew therefore left nothing selected in exactly the apps that move visually, while the same code
/// worked converting out of it. UI Automation's text ranges are defined in logical characters, so
/// moving an endpoint back N characters means the same thing regardless of script or control.</para>
///
/// <para><b>Threading.</b> Cross-process COM, same as <see cref="SelectionReader"/> — MTA worker only.</para>
/// </summary>
internal static class SelectionWriter
{
    /// <summary>
    /// Selects the last <paramref name="length"/> characters before the caret. Returns false when the
    /// provider cannot do it, leaving the caller to fall back to synthetic keystrokes.
    /// </summary>
    internal static bool TrySelectPreceding(int length, IntPtr expectedForeground, IErrorReporter reporter)
    {
        if (length <= 0) return false;

        try
        {
            var focused = AutomationElement.FocusedElement;
            if (focused == null) return false;

            // The text landed in the window we typed into; if focus has moved on, selecting anything
            // would be meddling with an app we were never invited into.
            if (!BelongsTo(focused, expectedForeground)) return false;

            if (!focused.TryGetCurrentPattern(TextPattern.Pattern, out var patternObj)
                || patternObj is not TextPattern text)
                return false;

            var selection = text.GetSelection();
            if (selection == null || selection.Length != 1) return false;

            // After typing, the selection is a collapsed caret sitting at the end of the inserted
            // run. Walk its START endpoint back over exactly what we inserted and select that.
            var range = selection[0].Clone();
            var moved = range.MoveEndpointByUnit(TextPatternRangeEndpoint.Start, TextUnit.Character, -length);
            if (moved != -length) return false;

            range.Select();
            return true;
        }
        catch (Exception ex)
        {
            reporter.Report("SelectionWriter.TrySelectPreceding", ex);
            return false;
        }
    }

    private static bool BelongsTo(AutomationElement element, IntPtr foreground)
    {
        if (foreground == IntPtr.Zero) return false;

        try
        {
            Native.NativeMethods.GetWindowThreadProcessId(foreground, out var foregroundPid);
            if (foregroundPid == 0) return false;
            return element.Current.ProcessId == (int)foregroundPid;
        }
        catch
        {
            return false;
        }
    }
}
