using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using OopsType.Models;
using OopsType.ViewModels;
using OopsType.Views;

namespace OopsType.Services.Overlays;

/// <summary>
/// Presenter for the taskbar color strip — a colored bar drawn on top of (or behind) each taskbar
/// to give an at-a-glance hint of the current keyboard layout. One strip is shown per taskbar: the
/// primary one plus every secondary-monitor taskbar Windows currently has (i.e. whenever "Show my
/// taskbar on all displays" is enabled). The strips have no follow loop; their set and positions are
/// re-synced on every heartbeat, which is also when newly attached/removed monitors are picked up.
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

    // One overlay window per taskbar, keyed by the taskbar's HWND. All windows share a single view
    // model: the strip color reflects the current keyboard layout, which is global to the session,
    // so there is nothing per-monitor to track.
    private readonly Dictionary<IntPtr, TaskbarStripOverlay> _overlays = new();
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
            _viewModel ??= _vmFactory();
            Sync();
        }
        else
        {
            EnsureDestroyed();
        }
    }

    public void Heartbeat()
    {
        // No-op while disabled: _viewModel stays null until ApplySettings enables the feature.
        if (_viewModel == null) return;
        Sync();
    }

    /// <summary>
    /// Reconcile the live set of overlays with the taskbars Windows currently reports: create a
    /// strip for any new taskbar, destroy strips whose taskbar has vanished (monitor unplugged or
    /// "show on all displays" turned off), and reposition the survivors.
    /// </summary>
    private void Sync()
    {
        var taskbars = _taskbar.GetAllTaskbars();

        if (taskbars.Count == 0)
        {
            // The shell briefly has no tray windows while explorer restarts. Hide rather than
            // destroy, so the strips snap back the instant the taskbars return.
            foreach (var o in _overlays.Values)
                if (o.Visibility == Visibility.Visible) o.SetVisible(false);
            return;
        }

        var live = new HashSet<IntPtr>();
        foreach (var tb in taskbars) live.Add(tb.Hwnd);

        // Destroy overlays whose taskbar is gone. Snapshot the keys first — we mutate the
        // dictionary inside the loop.
        foreach (var hwnd in _overlays.Keys.Where(h => !live.Contains(h)).ToList())
        {
            _overlays[hwnd].Close();
            _overlays.Remove(hwnd);
        }

        var settings = _settings.Current.TaskbarStrip;
        foreach (var tb in taskbars)
        {
            if (!_overlays.TryGetValue(tb.Hwnd, out var overlay))
            {
                overlay = CreateOverlay();
                _overlays[tb.Hwnd] = overlay;
            }
            Position(overlay, tb, settings);
        }
    }

    private TaskbarStripOverlay CreateOverlay()
    {
        var overlay = _viewFactory();
        overlay.DataContext = _viewModel;
        // The strip is a persistent indicator that must survive "Show Desktop" (Win+D); the
        // caret/mouse overlays don't opt in because they hide themselves per-frame anyway.
        overlay.KeepVisibleThroughShowDesktop = true;
        overlay.ShowOverlay();
        overlay.EnsureTopmost();
        return overlay;
    }

    private static void Position(TaskbarStripOverlay overlay, TaskbarInfo tb, TaskbarStripSettings settings)
    {
        var thickness = ResolveThickness(settings.Thickness, tb.Rect.Height);
        var anchorBottom = string.Equals(settings.VerticalPosition, "bottom", StringComparison.OrdinalIgnoreCase);
        var y = anchorBottom ? tb.Rect.Bottom - thickness : tb.Rect.Y;

        overlay.Opacity = settings.OpacityEnabled ? Math.Clamp(settings.Opacity, 0.0, 1.0) : 1.0;
        overlay.PositionInScreenPixels(tb.Rect.X, y, tb.Rect.Width, thickness);

        if (overlay.Visibility != Visibility.Visible)
            overlay.SetVisible(true);

        // "behind" relies on the Win11 acrylic letting the strip's color bleed through.
        var behind = string.Equals(settings.Placement, "behind", StringComparison.OrdinalIgnoreCase);
        if (behind) overlay.EnsureBehindTaskbar(tb.Hwnd);
        else overlay.EnsureTopmost();
    }

    private void EnsureDestroyed()
    {
        if (_overlays.Count == 0 && _viewModel == null) return;

        foreach (var o in _overlays.Values) o.Close();
        _overlays.Clear();

        _viewModel?.Dispose();
        _viewModel = null;
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
