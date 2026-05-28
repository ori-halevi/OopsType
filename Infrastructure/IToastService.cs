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
    void Show(string title, string message, ToastKind kind);
}

public enum ToastKind { Info, Warning, Error }
