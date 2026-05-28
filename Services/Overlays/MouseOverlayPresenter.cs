using System;
using System.Windows;
using System.Windows.Threading;
using OopsType.Native;
using OopsType.ViewModels;
using OopsType.Views;

namespace OopsType.Services.Overlays;

/// <summary>
/// Presenter for the "mouse label" overlay — a chip that follows the mouse cursor and shows the
/// current keyboard layout. Uses a fast polling cadence because cursor movement isn't pumped to us
/// by Windows for free, and we want the chip to feel attached to the cursor without lag.
/// </summary>
public sealed class MouseOverlayPresenter : IOverlayPresenter
{
    // 25ms ≈ 40 Hz — fast enough to feel attached to the cursor, cheap enough to be invisible
    // in Task Manager.
    private static readonly TimeSpan FollowInterval = TimeSpan.FromMilliseconds(25);

    private readonly ISettingsService _settings;
    private readonly Func<MouseLabelViewModel> _vmFactory;
    private readonly Func<MouseLabelOverlay> _viewFactory;

    private MouseLabelOverlay? _overlay;
    private MouseLabelViewModel? _viewModel;
    private DispatcherTimer? _followTimer;

    public MouseOverlayPresenter(
        ISettingsService settings,
        Func<MouseLabelViewModel> vmFactory,
        Func<MouseLabelOverlay> viewFactory)
    {
        _settings = settings;
        _vmFactory = vmFactory;
        _viewFactory = viewFactory;
    }

    public void ApplySettings()
    {
        if (_settings.Current.MouseLabel.Enabled)
            EnsureCreated();
        else
            EnsureDestroyed();
    }

    public void Heartbeat() => _overlay?.EnsureTopmost();

    private void EnsureCreated()
    {
        if (_overlay != null) return;

        _viewModel = _vmFactory();
        _overlay = _viewFactory();
        _overlay.DataContext = _viewModel;
        _overlay.Show();
        _overlay.EnsureTopmost();
        UpdatePosition();

        _followTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = FollowInterval };
        _followTimer.Tick += (_, _) => UpdatePosition();
        _followTimer.Start();
    }

    private void EnsureDestroyed()
    {
        if (_overlay == null) return;

        _followTimer?.Stop();
        _followTimer = null;

        _overlay.Close();
        _overlay = null;

        _viewModel?.Dispose();
        _viewModel = null;
    }

    private void UpdatePosition()
    {
        if (_overlay == null) return;
        if (!NativeMethods.GetCursorPos(out var p)) return;

        var settings = _settings.Current.MouseLabel;
        _overlay.PositionInScreenPixels(p.X + settings.OffsetX, p.Y + settings.OffsetY);
        if (_overlay.Visibility != Visibility.Visible)
            _overlay.Visibility = Visibility.Visible;
    }

    public void Dispose() => EnsureDestroyed();
}
