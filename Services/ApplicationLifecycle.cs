using System;
using Microsoft.Win32;
using OopsType.Infrastructure;

namespace OopsType.Services;

/// <summary>
/// Default <see cref="IApplicationLifecycle"/>. Encapsulates the previously inlined Start/Stop
/// sequence so the WPF <c>App</c> class only resolves a single dependency.
///
/// <para>Also owns subscription to <see cref="SystemEvents.PowerModeChanged"/> — for a tray
/// utility that's expected to be running 24/7 across the user's daily sleep cycle, resume from
/// hibernate is the single most common "weird state" we need to handle: hooks may have been torn
/// down, the wall clock has jumped, and the idle-reset latch is at the mercy of whatever
/// happened to be on screen 8 hours ago. We turn that into an explicit hint to the rest of the
/// app instead of letting it surface as inexplicable glitches.</para>
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
    private readonly ILogger _logger;

    private PowerModeChangedEventHandler? _powerHandler;

    public ApplicationLifecycle(
        ISettingsService settings,
        IKeyboardLayoutService layout,
        IKeyboardActivityService activity,
        IIdleResetService idle,
        IOverlayCoordinator overlays,
        ITrayPresenter tray,
        ISettingsDialog settingsDialog,
        IStartupService startup,
        IErrorReporter reporter,
        ILogger logger)
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
        _logger = logger;
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

        // Subscribe LAST so we don't fire a resume hint into half-initialized services if Windows
        // happens to raise PowerModeChanged during startup (rare but possible on slow boots).
        _powerHandler = OnPowerModeChanged;
        try { SystemEvents.PowerModeChanged += _powerHandler; }
        catch (Exception ex) { _reporter.Report("Lifecycle.PowerSubscribe", ex); }

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
        // Unsubscribe from SystemEvents first — once we're tearing down, a late Resume notice
        // could try to poke services that are already disposed.
        if (_powerHandler != null)
        {
            try { SystemEvents.PowerModeChanged -= _powerHandler; }
            catch (Exception ex) { _reporter.Report("Lifecycle.PowerUnsubscribe", ex); }
            _powerHandler = null;
        }

        // Reverse-order teardown so listeners are gone before publishers raise their last events.
        SafeStop("OverlayCoordinator", _overlays.Shutdown);
        SafeStop("IdleResetService", _idle.Dispose);
        SafeStop("KeyboardActivityService", _activity.Dispose);
        SafeStop("KeyboardLayoutService", _layout.Dispose);
        SafeStop("TrayPresenter", _tray.Dispose);

        _logger.Info("OopsType stopped.");
    }

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        // SystemEvents raises on the SystemEvents pump thread, NOT the WPF UI thread. Services
        // touched here (Reset/EnsureInstalled) are thread-tolerant — they only flip flags and
        // either no-op or post to the dispatcher. UI mutation must not happen here.
        Safe.Invoke(_reporter, "Lifecycle.PowerModeChanged", () =>
        {
            switch (e.Mode)
            {
                case PowerModes.Suspend:
                    _logger.Info("Power: entering suspend.");
                    break;

                case PowerModes.Resume:
                    _logger.Info("Power: resumed from suspend.");
                    // After an 8-hour sleep the LastKey wall-clock delta would trip idle reset
                    // on the very first user keystroke (which could be a password prompt). Reset
                    // to "just now" so the user types undisturbed for at least IdleSeconds.
                    _activity.ResetIdle();
                    // Hooks may have been silently torn down across the suspend; re-arm now
                    // instead of waiting up to a heartbeat tick (~30s in worst case) to notice.
                    _overlays.OnSystemResume();
                    break;

                case PowerModes.StatusChange:
                    // Battery/AC transition — nothing to do, but logging it once helps correlate
                    // user-visible glitches to a power event in after-the-fact forensics.
                    break;
            }
        });
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
