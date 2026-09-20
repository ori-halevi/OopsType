using System;
using System.Collections.Generic;
using System.Threading;
using OopsType.Infrastructure;
using OopsType.Native;

namespace OopsType.Services.TextFix;

/// <summary>
/// Types a string into whatever app has focus, replacing its current selection, using synthetic
/// Unicode keystrokes.
///
/// <para><b>Why keystrokes and not a paste.</b> Besides leaving the clipboard alone, real key input
/// is what web apps actually listen for: a framework-controlled input (React, Vue) updates its state
/// from <c>input</c> events, and pasted content is sometimes intercepted or sanitised instead. Typing
/// lands correctly in both native and web targets. Cost is negligible — the whole string goes out in
/// a single <c>SendInput</c> call, not one syscall per character.</para>
/// </summary>
internal static class TextInjector
{
    // How long to wait for the user to let go of the modifier keys before typing, and how often to
    // re-check. A language switch is usually Alt+Shift, so the modifiers are still physically down
    // at the moment we are triggered; two seconds is far longer than any human takes to release.
    private static readonly TimeSpan ModifierReleaseTimeout = TimeSpan.FromSeconds(2);
    private const int ModifierPollMs = 15;

    private static readonly int[] Modifiers =
    {
        NativeMethods.VK_SHIFT,
        NativeMethods.VK_CONTROL,
        NativeMethods.VK_MENU,
        NativeMethods.VK_LWIN,
        NativeMethods.VK_RWIN,
    };

    /// <summary>
    /// Replaces the active selection with <paramref name="text"/>. Returns false without typing
    /// anything if the user is still holding modifiers when the timeout expires.
    /// </summary>
    /// <param name="expectedForeground">
    /// Window that must still be in the foreground when we finally type. Re-checked after the
    /// modifier wait, because that wait can last long enough for the user to switch apps — and
    /// typing a converted sentence into whatever they switched to would be the single worst thing
    /// this feature could do.
    /// </param>
    internal static bool Replace(string text, IntPtr expectedForeground, IErrorReporter reporter)
    {
        if (string.IsNullOrEmpty(text)) return false;

        // CRITICAL: while Alt is physically held, a synthetic character arrives as WM_SYSCHAR rather
        // than WM_CHAR — i.e. as a menu accelerator. Typing "file" into a held-Alt window would open
        // menus instead of inserting text. Waiting is safer than synthesising key-ups, which would
        // desynchronise the real physical key state.
        if (!WaitForModifiersReleased()) return false;

        if (expectedForeground != IntPtr.Zero && NativeMethods.GetForegroundWindow() != expectedForeground)
            return false;

        try
        {
            var inputs = new List<NativeMethods.INPUT>(text.Length * 2);

            foreach (var c in text)
            {
                inputs.Add(UnicodeKey(c, up: false));
                inputs.Add(UnicodeKey(c, up: true));
            }

            var array = inputs.ToArray();
            var sent = NativeMethods.SendInput(
                (uint)array.Length, array, System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.INPUT>());

            if (sent != array.Length)
            {
                // A partial send means UIPI blocked us — the target runs at a higher integrity level
                // (an elevated app, the secure desktop). Whatever did land is a half-applied
                // replacement, so this is a FAILURE: reporting it as success would log a conversion
                // that never happened and flash a chip over mangled text.
                reporter.Report("TextInjector.Replace",
                    $"SendInput delivered {sent} of {array.Length} events (target likely elevated).");

                // If the truncation fell between the Shift press and its release, Shift is now stuck
                // down for the user. Releasing it is harmless when it was never pressed.
                ReleaseShift();
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            reporter.Report("TextInjector.Replace", ex);
            return false;
        }
    }

    /// <summary>
    /// Walks the selection back over the last <paramref name="length"/> characters with Shift+Left.
    ///
    /// <para>Fallback only: <see cref="SelectionWriter"/> does this through UI Automation, which is
    /// script-agnostic. Arrow keys are not — a control that moves the caret VISUALLY rather than
    /// logically sends these the wrong way through right-to-left text — so this runs only where the
    /// UIA route is unavailable, which in practice means classic Win32 controls, where it is correct.
    /// Callers must not use it for text containing line breaks: those are not inserted one-for-one
    /// (WPF drops a lone CR, Win32 EDIT collapses CRLF), so the count would overshoot into text that
    /// was already there.</para>
    /// </summary>
    internal static void SelectPrecedingWithKeys(int length, IErrorReporter reporter)
    {
        if (length <= 0) return;

        try
        {
            var inputs = new List<NativeMethods.INPUT>(length * 2 + 2)
            {
                VirtualKey(NativeMethods.VK_SHIFT, up: false, extended: false),
            };
            for (var i = 0; i < length; i++)
            {
                inputs.Add(VirtualKey(NativeMethods.VK_LEFT, up: false, extended: true));
                inputs.Add(VirtualKey(NativeMethods.VK_LEFT, up: true, extended: true));
            }
            inputs.Add(VirtualKey(NativeMethods.VK_SHIFT, up: true, extended: false));

            var array = inputs.ToArray();
            var sent = NativeMethods.SendInput(
                (uint)array.Length, array, System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.INPUT>());

            // A truncated send can leave Shift latched down for the user.
            if (sent != array.Length) ReleaseShift();
        }
        catch (Exception ex)
        {
            reporter.Report("TextInjector.SelectPrecedingWithKeys", ex);
            ReleaseShift();
        }
    }

    /// <summary>Best-effort Shift key-up, used to clean up after a truncated send.</summary>
    private static void ReleaseShift()
    {
        try
        {
            var up = new[] { VirtualKey(NativeMethods.VK_SHIFT, up: true, extended: false) };
            NativeMethods.SendInput(1, up, System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.INPUT>());
        }
        catch
        {
            // Nothing useful to do if even this fails.
        }
    }

    /// <summary>
    /// Blocks until no modifier key is physically down, or the timeout expires. Runs on the
    /// conversion worker, so sleeping here stalls nothing the user can see.
    /// </summary>
    private static bool WaitForModifiersReleased()
    {
        var deadline = DateTime.UtcNow + ModifierReleaseTimeout;
        while (AnyModifierDown())
        {
            if (DateTime.UtcNow >= deadline) return false;
            Thread.Sleep(ModifierPollMs);
        }
        return true;
    }

    private static bool AnyModifierDown()
    {
        foreach (var vk in Modifiers)
        {
            // High bit set means the key is down right now (as opposed to bit 0, "pressed since
            // the last call", which we explicitly do not want).
            if ((NativeMethods.GetAsyncKeyState(vk) & 0x8000) != 0) return true;
        }
        return false;
    }

    /// <summary>
    /// A KEYEVENTF_UNICODE event, which delivers a literal character regardless of the target's
    /// active keyboard layout — the only way to type Hebrew into a window whose layout we just
    /// switched away from. <c>wVk</c> must be zero for the flag to take effect.
    /// </summary>
    private static NativeMethods.INPUT UnicodeKey(char c, bool up) => new()
    {
        type = NativeMethods.INPUT_KEYBOARD,
        u = new NativeMethods.INPUTUNION
        {
            ki = new NativeMethods.KEYBDINPUT
            {
                wVk = 0,
                wScan = c,
                dwFlags = NativeMethods.KEYEVENTF_UNICODE | (up ? NativeMethods.KEYEVENTF_KEYUP : 0),
                time = 0,
                dwExtraInfo = NativeMethods.InjectedSignature,
            },
        },
    };

    private static NativeMethods.INPUT VirtualKey(int vk, bool up, bool extended) => new()
    {
        type = NativeMethods.INPUT_KEYBOARD,
        u = new NativeMethods.INPUTUNION
        {
            ki = new NativeMethods.KEYBDINPUT
            {
                wVk = (ushort)vk,
                wScan = 0,
                dwFlags = (extended ? NativeMethods.KEYEVENTF_EXTENDEDKEY : 0)
                        | (up ? NativeMethods.KEYEVENTF_KEYUP : 0),
                time = 0,
                dwExtraInfo = NativeMethods.InjectedSignature,
            },
        },
    };
}
