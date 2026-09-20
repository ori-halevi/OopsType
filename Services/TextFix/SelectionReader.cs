using System;
using System.Windows.Automation;
using OopsType.Infrastructure;
using OopsType.Native;

namespace OopsType.Services.TextFix;

/// <summary>What a read of the focused control's selection produced.</summary>
/// <param name="Found">True only when we got real selected text from an editable target.</param>
/// <param name="Text">The selected text, capped at the caller's limit plus one.</param>
/// <param name="OverLimit">
/// True when the selection is longer than the caller's cap — the caller declines rather than
/// converting a truncated prefix.
/// </param>
public readonly record struct SelectionSnapshot(bool Found, string Text, bool OverLimit)
{
    public static readonly SelectionSnapshot Empty = new(false, string.Empty, false);
}

/// <summary>
/// Reads the text the user currently has selected in whatever app has focus, via UI Automation.
///
/// <para>Windows exposes no direct "give me the selection" API, and the usual workaround — press
/// Ctrl+C and read the clipboard — is deliberately NOT used here: a utility that runs all day has no
/// business overwriting the user's clipboard, and restoring every clipboard format faithfully is a
/// bug waiting to happen. UI Automation's <c>TextPattern</c> covers Win32 edit controls, WinForms,
/// WPF, WinUI, Office and every Chromium-based app (browsers, Electron), which is the overwhelming
/// majority of where text actually gets typed.</para>
///
/// <para><b>Threading.</b> Every call here is cross-process COM and can block for seconds against a
/// hung app. Call only from the dedicated MTA worker in <see cref="ConvertSelectionService"/> —
/// never from the UI thread and never from a hook callback.</para>
///
/// <para><b>Fail-closed, unlike the caret chip.</b> <c>CaretLocationService</c> runs a similar
/// read-only test and deliberately fails OPEN, because the worst case there is a chip shown where it
/// should not be. Here the worst case is typing into something the user cannot undo, so every
/// ambiguity, provider fault or missing signal resolves to "do nothing".</para>
/// </summary>
internal static class SelectionReader
{
    /// <summary>
    /// Reads the focused element's selection, or <see cref="SelectionSnapshot.Empty"/> when there is
    /// nothing safe to convert: no focus, no text pattern, a read-only target, or an empty selection.
    /// </summary>
    /// <param name="maxChars">Conversion cap; one extra character is fetched to detect overflow.</param>
    /// <param name="expectedForeground">
    /// Window the conversion was triggered for. UI Automation reports the system-wide focused
    /// element, which is not necessarily inside the window we are about to type into — if focus
    /// moved between the trigger and this read, we would convert one app's selection and inject the
    /// result into another. Bail instead.
    /// </param>
    internal static SelectionSnapshot Read(int maxChars, IntPtr expectedForeground, IErrorReporter reporter)
    {
        try
        {
            var focused = AutomationElement.FocusedElement;
            if (focused == null) return SelectionSnapshot.Empty;

            if (!BelongsTo(focused, expectedForeground)) return SelectionSnapshot.Empty;

            if (!focused.TryGetCurrentPattern(TextPattern.Pattern, out var patternObj)
                || patternObj is not TextPattern text)
                return SelectionSnapshot.Empty;

            if (!IsEditable(focused)) return SelectionSnapshot.Empty;

            var selection = text.GetSelection();
            if (selection == null || selection.Length == 0) return SelectionSnapshot.Empty;

            // Multiple ranges means a disjoint selection (a table column, a multi-cursor editor).
            // Replacing that with one linear string would scramble it, so we decline.
            if (selection.Length > 1) return SelectionSnapshot.Empty;

            // Fetch one past the cap: enough to know the selection is too long without marshalling
            // an entire document across the process boundary after a stray Ctrl+A.
            var raw = selection[0].GetText(maxChars + 1);
            if (string.IsNullOrEmpty(raw)) return SelectionSnapshot.Empty;

            return new SelectionSnapshot(true, raw, raw.Length > maxChars);
        }
        catch (Exception ex)
        {
            // Providers fault routinely when the target window dies mid-query. Report (the reporter
            // throttles per source) and treat it as "no selection".
            reporter.Report("SelectionReader.Read", ex);
            return SelectionSnapshot.Empty;
        }
    }

    /// <summary>
    /// True when the focused element lives in the same process as the window we intend to type into.
    /// A process comparison rather than an HWND one because modern UI stacks (WinUI, Chromium) route
    /// focus to child windows that do not match the top-level handle, while still — correctly —
    /// reporting their host process.
    /// </summary>
    private static bool BelongsTo(AutomationElement element, IntPtr foreground)
    {
        if (foreground == IntPtr.Zero) return false;

        try
        {
            NativeMethods.GetWindowThreadProcessId(foreground, out var foregroundPid);
            if (foregroundPid == 0) return false;
            return element.Current.ProcessId == (int)foregroundPid;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// True only with positive evidence that keystrokes sent to this element would land as text.
    ///
    /// <para>Two signals, both required:
    /// <list type="bullet">
    ///   <item><c>IsKeyboardFocusable</c> and <c>IsEnabled</c> — static page content (article
    ///   paragraphs, captions, labels) cannot take keyboard focus, so typed characters would never
    ///   become text there. In a browser they would instead trigger single-key page shortcuts, which
    ///   is far worse than doing nothing.</item>
    ///   <item><c>ValuePattern.IsReadOnly</c> — the explicit flag, set by inputs with the readonly
    ///   attribute and by Chromium for non-editable documents. Absence of ValuePattern is tolerated:
    ///   rich editors (Word, VS Code, contenteditable) often expose only TextPattern.</item>
    /// </list></para>
    /// </summary>
    private static bool IsEditable(AutomationElement element)
    {
        try
        {
            if (!element.Current.IsEnabled) return false;
            if (!element.Current.IsKeyboardFocusable) return false;

            if (element.TryGetCurrentPattern(ValuePattern.Pattern, out var valueObj)
                && valueObj is ValuePattern value
                && value.Current.IsReadOnly)
                return false;

            return true;
        }
        catch
        {
            // Element vanished or the provider faulted — we have no evidence it is editable, and
            // "no evidence" must mean "do not type into it".
            return false;
        }
    }
}
