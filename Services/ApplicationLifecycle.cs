using System;
using OopsType.Infrastructure;

namespace OopsType.Services;

/// <summary>
/// Default <see cref="IApplicationLifecycle"/>. Encapsulates the previously inlined Start/Stop
/// sequence so the WPF <c>App</c> class only resolves a single dependency.
/// </summary>
public sealed class ApplicationLifecycle : IApplicationLifecycle
{
    private readonly ISettingsService _settings;
    private readonly IKeyboardLayoutService _layout;
    private readonly IKeyboardActivityService _activity;
    private readonly IIdleResetService _idle;
    private readonly IOverlayCoordinator _overlays;
    private readonly ITrayPresenter _tray;
    private readonly ISettingsDialog _settingsDialog;
    private readonly IStartupService _startup;
    private readonly IErrorReporter _reporter;

    public ApplicationLifecycle(
        ISettingsService settings,
        IKeyboardLayoutService layout,
        IKeyboardActivityService activity,
        IIdleResetService idle,
        IOverlayCoordinator overlays,
        ITrayPresenter tray,
        ISettingsDialog settingsDialog,
        IStartupService startup,
        IErrorReporter reporter)
    {
        _settings = settings;
        _layout = layout;
        _activity = activity;
        _idle = idle;
        _overlays = overlays;
        _tray = tray;
        _settingsDialog = settingsDialog;
        _startup = startup;
        _reporter = reporter;
    }

    public void Start()
    {
        // Order matters: layout/activity feed events into idle reset and overlays.
        // One service's startup failure must not prevent the rest — degraded operation beats
        // a black-screen launch (no tray icon, no way to quit).
        SafeStart("KeyboardLayoutService", _layout.Start);
        SafeStart("KeyboardActivityService", _activity.Start);
        SafeStart("IdleResetService", _idle.Start);
        SafeStart("OverlayCoordinator", _overlays.Start);
        SafeStart("TrayPresenter", _tray.Start);

        if (_settings.IsFirstLaunch)
        {
            // Default to launching with Windows on first run. Only applied once — if the user
            // later unticks the box, we must not silently re-enable it on the next launch.
            SafeStart("StartupService.EnableDefault", () => _startup.SetEnabled(true));
            SafeStart("FirstRunDialog", _settingsDialog.Show);
        }
    }

    public void Stop()
    {
        // Reverse-order teardown so listeners are gone before publishers raise their last events.
        SafeStop("OverlayCoordinator", _overlays.Shutdown);
        SafeStop("IdleResetService", _idle.Dispose);
        SafeStop("KeyboardActivityService", _activity.Dispose);
        SafeStop("KeyboardLayoutService", _layout.Dispose);
        SafeStop("TrayPresenter", _tray.Dispose);
    }

    private void SafeStart(string name, Action action)
    {
        try { action(); }
        catch (Exception ex) { _reporter.Report($"Lifecycle.Start/{name}", ex); }
    }

    private void SafeStop(string name, Action action)
    {
        try { action(); }
        catch (Exception ex) { _reporter.Report($"Lifecycle.Stop/{name}", ex); }
    }
}
