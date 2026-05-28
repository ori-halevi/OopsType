using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using OopsType.Infrastructure;
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
///
/// Cursor-visibility handling: when the OS hides the cursor (video player auto-hide, touch/pen
/// input), the label must hide too. During motion, <see cref="UpdatePosition"/> already calls
/// <see cref="NativeMethods.IsCursorVisible"/> on every frame — free. The hard case is the idle
/// state: no events fire, but the cursor can still get hidden a few seconds after the user stops
/// moving. To stay true to "no work at rest", we run a bounded poll instead of a perpetual timer:
/// it starts when motion ends, ticks at <see cref="CursorVisibilityPollInterval"/>, and stops on
/// the first of (a) cursor seen hidden — overlay collapsed, nothing more to do until the next
/// move, (b) <see cref="CursorVisibilityPollDuration"/> elapsed with the cursor still visible —
/// we accept the rare miss where a video player hides the cursor much later than expected, or
/// (c) motion resumes (<see cref="EnsureSubscribed"/> stops the timer). The visibility-poll
/// constants are intentionally not user-configurable.
/// </summary>
public sealed class MouseOverlayPresenter : IOverlayPresenter
{
    // Time without a mouse event after which we drop the Rendering subscription and go idle.
    // Short enough that re-subscribing on the next move is invisible (first frame of motion
    // lands within ~16ms anyway), long enough to avoid churn during tiny pauses mid-drag.
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromMilliseconds(150);

    // Bounded cursor-visibility poll while idle. 200ms is fast enough that a video player's
    // auto-hide is noticed within a frame or two of human perception, slow enough to be ~5
    // P/Invokes per second of cost. 5 seconds is generous headroom over typical auto-hide
    // delays (~3s) so we almost always catch the transition before giving up.
    private static readonly TimeSpan CursorVisibilityPollInterval = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan CursorVisibilityPollDuration = TimeSpan.FromSeconds(5);

    private readonly ISettingsService _settings;
    private readonly IErrorReporter _reporter;
    private readonly Func<MouseLabelViewModel> _vmFactory;
    private readonly Func<MouseLabelOverlay> _viewFactory;

    private MouseLabelOverlay? _overlay;
    private MouseLabelViewModel? _viewModel;
    private LowLevelMouseHook? _hook;
    private DispatcherTimer? _visibilityPollTimer;
    private long _visibilityPollStartTicks;

    private bool _renderingSubscribed;
    private long _lastMoveTicks;
    private bool _maxSmoothness;

    public MouseOverlayPresenter(
        ISettingsService settings,
        IErrorReporter reporter,
        Func<MouseLabelViewModel> vmFactory,
        Func<MouseLabelOverlay> viewFactory)
    {
        _settings = settings;
        _reporter = reporter;
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

        _hook = new LowLevelMouseHook(_reporter);
        _hook.MouseMoved += OnMouseMoved;
    }

    private void EnsureDestroyed()
    {
        if (_overlay == null) return;

        StopVisibilityPoll();
        if (_renderingSubscribed)
        {
            CompositionTarget.Rendering -= OnRendering;
            _renderingSubscribed = false;
        }

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
    // degrades mouse responsiveness system-wide. The hook itself already swallows our throws,
    // but assigning to _lastMoveTicks and calling EnsureSubscribed are infallible.
    private void OnMouseMoved()
    {
        _lastMoveTicks = Stopwatch.GetTimestamp();
        EnsureSubscribed();
    }

    private void EnsureSubscribed()
    {
        if (_renderingSubscribed) return;
        _renderingSubscribed = true;
        StopVisibilityPoll();
        CompositionTarget.Rendering += OnRendering;
    }

    private void EnsureUnsubscribed()
    {
        if (!_renderingSubscribed) return;
        _renderingSubscribed = false;
        CompositionTarget.Rendering -= OnRendering;
        StartVisibilityPoll();
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        // CompositionTarget.Rendering is invoked by the WPF compositor — an unhandled throw here
        // bubbles to DispatcherUnhandledException and (if we hadn't wired a global handler) could
        // kill the render loop. Local catch keeps the source label precise.
        try
        {
            UpdatePosition();

            if (_maxSmoothness) return;

            if (Stopwatch.GetElapsedTime(_lastMoveTicks) > IdleTimeout)
                EnsureUnsubscribed();
        }
        catch (Exception ex)
        {
            _reporter.Report("MouseOverlayPresenter.OnRendering", ex);
        }
    }

    private void UpdatePosition()
    {
        if (_overlay == null) return;

        if (!NativeMethods.IsCursorVisible())
        {
            if (_overlay.Visibility != Visibility.Collapsed)
                _overlay.Visibility = Visibility.Collapsed;
            return;
        }

        if (!NativeMethods.GetCursorPos(out var p)) return;

        var settings = _settings.Current.MouseLabel;
        _overlay.PositionInScreenPixels(p.X + settings.OffsetX, p.Y + settings.OffsetY);
        if (_overlay.Visibility != Visibility.Visible)
            _overlay.Visibility = Visibility.Visible;
    }

    private void StartVisibilityPoll()
    {
        if (_overlay == null) return;
        _visibilityPollStartTicks = Stopwatch.GetTimestamp();
        if (_visibilityPollTimer == null)
        {
            _visibilityPollTimer = new DispatcherTimer { Interval = CursorVisibilityPollInterval };
            _visibilityPollTimer.Tick += OnVisibilityPollTick;
        }
        _visibilityPollTimer.Start();
    }

    private void StopVisibilityPoll() => _visibilityPollTimer?.Stop();

    private void OnVisibilityPollTick(object? sender, EventArgs e)
    {
        try
        {
            if (_overlay == null) { StopVisibilityPoll(); return; }

            if (!NativeMethods.IsCursorVisible())
            {
                if (_overlay.Visibility != Visibility.Collapsed)
                    _overlay.Visibility = Visibility.Collapsed;
                StopVisibilityPoll();
                return;
            }

            if (Stopwatch.GetElapsedTime(_visibilityPollStartTicks) > CursorVisibilityPollDuration)
                StopVisibilityPoll();
        }
        catch (Exception ex)
        {
            _reporter.Report("MouseOverlayPresenter.VisibilityPoll", ex);
            // Stop the timer on persistent failure so we don't repeatedly log the same error.
            StopVisibilityPoll();
        }
    }

    public void Dispose() => EnsureDestroyed();
}
