using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using OopsType.Native;
using OopsType.ViewModels;
using OopsType.Views;

namespace OopsType.Services.Overlays;

/// <summary>
/// Presenter for the "mouse label" overlay — a chip that follows the mouse cursor.
///
/// Architecture (see <c>MouseLabel_Overlay_Optimization_Spec.md</c>):
///   1. A global low-level mouse hook (<see cref="LowLevelMouseHook"/>) reports cursor motion.
///      Its only job is to mark "the cursor moved" and wake the render loop. Event-driven, so
///      zero work when the cursor is idle.
///   2. The actual window move happens inside <see cref="CompositionTarget.Rendering"/> — once
///      per WPF frame, synchronized with the compositor. This is what eliminates the jitter the
///      previous 40Hz DispatcherTimer produced.
///   3. After <see cref="IdleTimeout"/> without motion we unsubscribe from Rendering and return
///      to true idle (no per-frame tick). The next hook event re-subscribes.
///
/// The hook callback must NEVER move the window itself — that would happen on the hook thread's
/// schedule, not synchronized to a frame, reintroducing the jitter. Hook only wakes; Rendering
/// moves.
/// </summary>
public sealed class MouseOverlayPresenter : IOverlayPresenter
{
    // Time without a mouse event after which we drop the Rendering subscription and go idle.
    // Short enough that re-subscribing on the next move is invisible (first frame of motion
    // lands within ~16ms anyway), long enough to avoid churn during tiny pauses mid-drag.
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromMilliseconds(150);

    private readonly ISettingsService _settings;
    private readonly Func<MouseLabelViewModel> _vmFactory;
    private readonly Func<MouseLabelOverlay> _viewFactory;

    private MouseLabelOverlay? _overlay;
    private MouseLabelViewModel? _viewModel;
    private LowLevelMouseHook? _hook;

    private bool _renderingSubscribed;
    private long _lastMoveTicks;
    private bool _maxSmoothness;

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
        var s = _settings.Current.MouseLabel;
        if (!s.Enabled)
        {
            EnsureDestroyed();
            return;
        }

        EnsureCreated();
        _maxSmoothness = string.Equals(s.TrackingMode, "max-smoothness", StringComparison.OrdinalIgnoreCase);

        // In max-smoothness mode we hold the Rendering subscription permanently so the very
        // first frame of motion has zero re-subscribe latency. In economy mode we leave it to
        // the hook to wake us.
        if (_maxSmoothness) EnsureSubscribed();
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

        _lastMoveTicks = Stopwatch.GetTimestamp();
        UpdatePosition();

        _hook = new LowLevelMouseHook();
        _hook.MouseMoved += OnMouseMoved;
    }

    private void EnsureDestroyed()
    {
        if (_overlay == null) return;

        EnsureUnsubscribed();

        if (_hook != null)
        {
            _hook.MouseMoved -= OnMouseMoved;
            _hook.Dispose();
            _hook = null;
        }

        _overlay.Close();
        _overlay = null;

        _viewModel?.Dispose();
        _viewModel = null;
    }

    // Runs on the thread that installed the hook (the WPF UI thread — the hook is installed in
    // EnsureCreated). Must stay trivial: Windows enforces LowLevelHooksTimeout and a slow callback
    // degrades mouse responsiveness system-wide.
    private void OnMouseMoved()
    {
        _lastMoveTicks = Stopwatch.GetTimestamp();
        EnsureSubscribed();
    }

    private void EnsureSubscribed()
    {
        if (_renderingSubscribed) return;
        _renderingSubscribed = true;
        CompositionTarget.Rendering += OnRendering;
    }

    private void EnsureUnsubscribed()
    {
        if (!_renderingSubscribed) return;
        _renderingSubscribed = false;
        CompositionTarget.Rendering -= OnRendering;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        UpdatePosition();

        if (_maxSmoothness) return;

        if (Stopwatch.GetElapsedTime(_lastMoveTicks) > IdleTimeout)
            EnsureUnsubscribed();
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
