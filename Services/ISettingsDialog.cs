namespace OopsType.Services;

/// <summary>
/// Mediator that hides WPF Window plumbing from non-view callers (tray, lifecycle).
/// Lets services request the settings UI without taking a hard dependency on the concrete
/// <c>SettingsWindow</c> class.
/// </summary>
public interface ISettingsDialog
{
    /// <summary>
    /// Shows the settings window. If one is already open, activates it instead of creating
    /// a second instance.
    /// </summary>
    void Show();
}
