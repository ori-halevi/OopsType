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

    public ApplicationLifecycle(
        ISettingsService settings,
        IKeyboardLayoutService layout,
        IKeyboardActivityService activity,
        IIdleResetService idle,
        IOverlayCoordinator overlays,
        ITrayPresenter tray,
        ISettingsDialog settingsDialog)
    {
        _settings = settings;
        _layout = layout;
        _activity = activity;
        _idle = idle;
        _overlays = overlays;
        _tray = tray;
        _settingsDialog = settingsDialog;
    }

    public void Start()
    {
        // Order matters: layout/activity feed events into idle reset and overlays.
        _layout.Start();
        _activity.Start();
        _idle.Start();
        _overlays.Start();
        _tray.Start();

        if (_settings.IsFirstLaunch)
            _settingsDialog.Show();
    }

    public void Stop()
    {
        // Reverse-order teardown so listeners are gone before publishers raise their last events.
        _overlays.Shutdown();
        _idle.Dispose();
        _activity.Dispose();
        _layout.Dispose();
        _tray.Dispose();
    }
}
