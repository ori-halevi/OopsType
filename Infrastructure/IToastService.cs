namespace OopsType.Infrastructure;

/// <summary>
/// Reliable in-app toast surface. Unlike <c>NotifyIcon.ShowBalloonTip</c> — which Windows
/// frequently silences via Focus Assist / Action Center settings — our toast is a topmost
/// WPF window we control, so the user always sees it.
/// </summary>
public interface IToastService
{
    /// <summary>
    /// Show a toast in the bottom-right of the primary work area. Auto-fades after a few seconds.
    /// Safe to call from any thread — implementation marshals to the UI thread.
    /// </summary>
    /// <param name="copy">When non-null, the toast shows a Copy button that places
    /// <see cref="ToastCopyAction.Text"/> on the clipboard. The toast also pauses its auto-fade
    /// while the pointer is over it, so the button stays reachable.</param>
    void Show(string title, string message, ToastKind kind, ToastCopyAction? copy = null);
}

public enum ToastKind { Info, Warning, Error }

/// <summary>
/// Optional clipboard action attached to a toast. <see cref="Text"/> is the full payload to copy
/// (may differ from the possibly-truncated message shown). <see cref="Label"/> / <see cref="CopiedLabel"/>
/// are localized button captions supplied by the caller, keeping <see cref="IToastService"/> free of
/// any localization dependency.
/// </summary>
public sealed record ToastCopyAction(string Text, string Label, string CopiedLabel);
