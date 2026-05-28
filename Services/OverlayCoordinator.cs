using System;
using System.Collections.Generic;
using System.Windows.Threading;
using OopsType.Services.Overlays;

namespace OopsType.Services;

/// <summary>
/// Thin orchestrator that owns a single shared heartbeat timer and forwards lifecycle calls to
/// the individual <see cref="IOverlayPresenter"/>s. Each presenter is responsible for one overlay
/// (caret/mouse/strip), so this class has no per-overlay logic anymore — it just coordinates.
/// </summary>
public sealed class OverlayCoordinator : IOverlayCoordinator
{
    // Single low-frequency timer used by every presenter to re-assert window state (topmost,
    // strip position when the taskbar moves, etc.). Having one shared timer keeps WPF dispatcher
    // load down compared to one timer per overlay.
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromMilliseconds(1500);

    private readonly ISettingsService _settings;
    private readonly IReadOnlyList<IOverlayPresenter> _presenters;
    private DispatcherTimer? _heartbeat;

    public OverlayCoordinator(
        ISettingsService settings,
        CaretOverlayPresenter caret,
        MouseOverlayPresenter mouse,
        TaskbarStripOverlayPresenter strip)
    {
        _settings = settings;
        _presenters = new IOverlayPresenter[] { caret, mouse, strip };
    }

    public void Start()
    {
        _settings.Changed += ApplySettings;
        ApplySettings();

        _heartbeat = new DispatcherTimer(DispatcherPriority.Background) { Interval = HeartbeatInterval };
        _heartbeat.Tick += (_, _) => RunHeartbeat();
        _heartbeat.Start();
    }

    public void ApplySettings()
    {
        foreach (var p in _presenters) p.ApplySettings();
    }

    private void RunHeartbeat()
    {
        foreach (var p in _presenters) p.Heartbeat();
    }

    public void Shutdown()
    {
        _heartbeat?.Stop();
        _heartbeat = null;
        _settings.Changed -= ApplySettings;

        foreach (var p in _presenters) p.Dispose();
    }
}
