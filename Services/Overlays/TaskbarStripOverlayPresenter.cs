using System;
using System.Windows;
using OopsType.ViewModels;
using OopsType.Views;

namespace OopsType.Services.Overlays;

/// <summary>
/// Presenter for the taskbar color strip — a colored bar drawn on top of (or behind) the taskbar
/// to give an at-a-glance hint of the current keyboard layout. The strip itself has no follow
/// loop: its position only changes when the taskbar moves, which we re-check on every heartbeat.
/// </summary>
public sealed class TaskbarStripOverlayPresenter : IOverlayPresenter
{
    // Strip thickness presets in DIPs. "full" maps to the taskbar's own height at render time.
    private const double ThicknessLargeDip = 16;
    private const double ThicknessMediumDip = 8;
    private const double ThicknessSmallDip = 3;

    private readonly ISettingsService _settings;
    private readonly ITaskbarService _taskbar;
    private readonly Func<TaskbarStripViewModel> _vmFactory;
    private readonly Func<TaskbarStripOverlay> _viewFactory;

    private TaskbarStripOverlay? _overlay;
    private TaskbarStripViewModel? _viewModel;

    public TaskbarStripOverlayPresenter(
        ISettingsService settings,
        ITaskbarService taskbar,
        Func<TaskbarStripViewModel> vmFactory,
        Func<TaskbarStripOverlay> viewFactory)
    {
        _settings = settings;
        _taskbar = taskbar;
        _vmFactory = vmFactory;
        _viewFactory = viewFactory;
    }

    public void ApplySettings()
    {
        if (_settings.Current.TaskbarStrip.Enabled)
        {
            EnsureCreated();
            UpdatePosition();
        }
        else
        {
            EnsureDestroyed();
        }
    }

    public void Heartbeat() => UpdatePosition();

    private void EnsureCreated()
    {
        if (_overlay != null) return;

        _viewModel = _vmFactory();
        _overlay = _viewFactory();
        _overlay.DataContext = _viewModel;
        // The strip is a persistent indicator that must survive "Show Desktop" (Win+D); the
        // caret/mouse overlays don't opt in because they hide themselves per-frame anyway.
        _overlay.KeepVisibleThroughShowDesktop = true;
        _overlay.ShowOverlay();
        _overlay.EnsureTopmost();
    }

    private void EnsureDestroyed()
    {
        if (_overlay == null) return;

        _overlay.Close();
        _overlay = null;

        _viewModel?.Dispose();
        _viewModel = null;
    }

    private void UpdatePosition()
    {
        if (_overlay == null) return;

        if (!_taskbar.TryGetPrimaryTaskbarRect(out var taskbar))
        {
            if (_overlay.Visibility == Visibility.Visible)
                _overlay.SetVisible(false);
            return;
        }

        var settings = _settings.Current.TaskbarStrip;
        var thickness = ResolveThickness(settings.Thickness, taskbar.Height);
        var anchorBottom = string.Equals(settings.VerticalPosition, "bottom", StringComparison.OrdinalIgnoreCase);
        var y = anchorBottom ? taskbar.Bottom - thickness : taskbar.Y;

        _overlay.Opacity = settings.OpacityEnabled ? Math.Clamp(settings.Opacity, 0.0, 1.0) : 1.0;
        _overlay.PositionInScreenPixels(taskbar.X, y, taskbar.Width, thickness);

        if (_overlay.Visibility != Visibility.Visible)
            _overlay.SetVisible(true);

        // "behind" relies on the Win11 acrylic letting the strip's color bleed through.
        var behind = string.Equals(settings.Placement, "behind", StringComparison.OrdinalIgnoreCase);
        if (behind) _overlay.EnsureBehindTaskbar();
        else _overlay.EnsureTopmost();
    }

    private static double ResolveThickness(string thickness, double taskbarHeight) =>
        (thickness ?? "small").ToLowerInvariant() switch
        {
            "full"   => Math.Max(1, taskbarHeight),
            "large"  => ThicknessLargeDip,
            "medium" => ThicknessMediumDip,
            _        => ThicknessSmallDip,
        };

    public void Dispose() => EnsureDestroyed();
}
