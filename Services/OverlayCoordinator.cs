using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows.Threading;
using OopsType.Infrastructure;
using OopsType.Services.Overlays;

namespace OopsType.Services;

/// <summary>
/// Thin orchestrator that owns a single shared heartbeat timer and forwards lifecycle calls to
/// the individual <see cref="IOverlayPresenter"/>s. Each presenter is responsible for one overlay
/// (caret/mouse/strip), so this class has no per-overlay logic anymore — it just coordinates.
///
/// <para>Also drives two 24/7-stability tasks the presenters can't do themselves:
/// <list type="bullet">
///   <item>Hook watchdog: every <see cref="HookWatchdogInterval"/> we ask the layout and keyboard
///   services to re-install their underlying hooks if Windows silently unhooked them (e.g., a
///   downstream LowLevelHooksTimeout violation). Without this, a single slow UIA call on day 3
///   permanently kills keyboard tracking.</item>
///   <item>Uptime heartbeat: every <see cref="UptimeHeartbeatInterval"/> we emit an INFO log
///   so a sustained run leaves breadcrumbs ("still alive at hour 42") for after-the-fact
///   forensics on whatever might eventually go wrong.</item>
/// </list></para>
/// </summary>
public sealed class OverlayCoordinator : IOverlayCoordinator
{
    // Single low-frequency timer used by every presenter to re-assert window state (topmost,
    // strip position when the taskbar moves, etc.). Having one shared timer keeps WPF dispatcher
    // load down compared to one timer per overlay.
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromMilliseconds(1500);

    // Periodicity for hook re-install check and uptime breadcrumbs. Both are sampled inside
    // the 1.5s heartbeat tick using elapsed-time gates so we don't run separate timers for them.
    private static readonly TimeSpan HookWatchdogInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan UptimeHeartbeatInterval = TimeSpan.FromHours(1);

    private readonly ISettingsService _settings;
    private readonly IErrorReporter _reporter;
    private readonly ILogger _logger;
    private readonly IKeyboardLayoutService _layout;
    private readonly IKeyboardActivityService _activity;
    private readonly IReadOnlyList<IOverlayPresenter> _presenters;

    private DispatcherTimer? _heartbeat;

    // Stopwatch ticks of the last time each periodic task ran. Zero means "never run yet".
    private long _startupTicks;
    private long _lastWatchdogTicks;
    private long _lastUptimeLogTicks;

    public OverlayCoordinator(
        ISettingsService settings,
        IErrorReporter reporter,
        ILogger logger,
        IKeyboardLayoutService layout,
        IKeyboardActivityService activity,
        CaretOverlayPresenter caret,
        MouseOverlayPresenter mouse,
        TaskbarStripOverlayPresenter strip)
    {
        _settings = settings;
        _reporter = reporter;
        _logger = logger;
        _layout = layout;
        _activity = activity;
        _presenters = new IOverlayPresenter[] { caret, mouse, strip };
    }

    public void Start()
    {
        _settings.Changed += ApplySettings;
        ApplySettings();

        _startupTicks = Stopwatch.GetTimestamp();
        _lastWatchdogTicks = _startupTicks;
        _lastUptimeLogTicks = _startupTicks;

        _logger.Info("OopsType started.");

        _heartbeat = new DispatcherTimer(DispatcherPriority.Background) { Interval = HeartbeatInterval };
        _heartbeat.Tick += (_, _) => Safe.Invoke(_reporter, "OverlayCoordinator.Heartbeat", RunHeartbeat);
        _heartbeat.Start();
    }

    public void ApplySettings()
    {
        // One presenter throwing must not skip the others — isolate each.
        foreach (var p in _presenters)
            Safe.Invoke(_reporter, $"OverlayCoordinator.ApplySettings/{p.GetType().Name}", p.ApplySettings);
    }

    private void RunHeartbeat()
    {
        foreach (var p in _presenters)
            Safe.Invoke(_reporter, $"OverlayCoordinator.Heartbeat/{p.GetType().Name}", p.Heartbeat);

        if (Stopwatch.GetElapsedTime(_lastWatchdogTicks) >= HookWatchdogInterval)
        {
            _lastWatchdogTicks = Stopwatch.GetTimestamp();
            Safe.Invoke(_reporter, "OverlayCoordinator.HookWatchdog", RunHookWatchdog);
        }

        if (Stopwatch.GetElapsedTime(_lastUptimeLogTicks) >= UptimeHeartbeatInterval)
        {
            _lastUptimeLogTicks = Stopwatch.GetTimestamp();
            Safe.Invoke(_reporter, "OverlayCoordinator.UptimeLog", LogUptime);
        }
    }

    /// <summary>
    /// Asks the services that own LL/WinEvent hooks to re-install if the OS has dropped them.
    /// EnsureInstalled is a cheap no-op when the hook is still alive, so calling every 30s
    /// has no measurable cost; the win is that we recover automatically from silent unhook.
    /// </summary>
    private void RunHookWatchdog()
    {
        _layout.EnsureInstalled();
        _activity.EnsureInstalled();
    }

    private void LogUptime()
    {
        var uptime = Stopwatch.GetElapsedTime(_startupTicks);
        // Format as integer hours — sub-hour precision adds noise without informing forensics.
        _logger.Info($"alive uptime={(int)uptime.TotalHours}h");
    }

    /// <summary>
    /// Called by the lifecycle service when the OS reports a resume from sleep/hibernate. Forces
    /// the watchdog tasks to run on the very next heartbeat instead of waiting out their gate.
    /// </summary>
    public void OnSystemResume()
    {
        // Reset the gates so the next RunHeartbeat re-arms hooks immediately and emits a fresh
        // "alive" line — useful to see in the log right next to the resume notice.
        _lastWatchdogTicks = 0;
        _lastUptimeLogTicks = 0;
    }

    public void Shutdown()
    {
        _heartbeat?.Stop();
        _heartbeat = null;
        _settings.Changed -= ApplySettings;

        foreach (var p in _presenters)
        {
            try { p.Dispose(); }
            catch (Exception ex) { _reporter.Report($"OverlayCoordinator.Shutdown/{p.GetType().Name}", ex); }
        }
    }
}
