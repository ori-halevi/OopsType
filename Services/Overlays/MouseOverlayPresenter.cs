using System;
using System.Diagnostics;
using System.Threading;
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
///   1. A raw-input <see cref="MouseMotionWatcher"/> reports cursor motion. Its only job is to mark
///      "the cursor moved" and wake the render loop. Event-driven, so zero work when the cursor is
///      idle. (It deliberately does NOT use a WH_MOUSE_LL hook — that would sit in the critical path
///      of every mouse event and stutter the system pointer under load; raw input only observes.)
///   2. The actual window move happens inside <see cref="CompositionTarget.Rendering"/> — once
///      per WPF frame, synchronized with the compositor. This is what eliminates the jitter the
///      previous 40Hz DispatcherTimer produced.
///   3. After <see cref="IdleTimeout"/> without motion we unsubscribe from Rendering and return
///      to true idle (no per-frame tick). The next hook event re-subscribes.
///
/// The motion callback must NEVER move the window itself — that would happen on the watcher
/// thread's schedule, not synchronized to a frame, reintroducing the jitter. The watcher only
/// wakes; Rendering moves.
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
/// (c) motion resumes (<see cref="SubscribeOnUi"/> stops the timer). The visibility-poll
/// constants are intentionally not user-configurable.
///
/// Threading: the motion callback (<see cref="OnMouseMoved"/>) runs on the watcher's dedicated
/// pump thread, never the UI thread. It only stamps the move time and posts a single subscribe
/// request to the UI thread; all window movement and Rendering (un)subscription happen on the UI
/// thread.
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

    // Fallback chip width in DIPs used before the overlay has measured itself once. Only matters for
    // the first frame or two; the real ActualWidth takes over as soon as the chip renders.
    private const double DefaultLabelWidthDip = 28;

    private readonly ISettingsService _settings;
    private readonly IErrorReporter _reporter;
    private readonly Func<MouseLabelViewModel> _vmFactory;
    private readonly Func<MouseLabelOverlay> _viewFactory;

    private MouseLabelOverlay? _overlay;
    private MouseLabelViewModel? _viewModel;
    private MouseMotionWatcher? _motion;
    private DispatcherTimer? _visibilityPollTimer;
    private long _visibilityPollStartTicks;

    // Actual CompositionTarget.Rendering subscription state. Touched only on the UI thread.
    private bool _renderingSubscribed;

    // Cross-thread coalescing guard for the hook→UI wake. 0 = idle, no wake outstanding; 1 = a wake
    // has been posted or we are already following. The hook thread flips 0→1 and posts exactly one
    // BeginInvoke per motion burst; the UI thread resets it to 0 only when it returns to idle, so a
    // stream of moves during active following never floods the dispatcher.
    private int _wakeRequested;

    // Stopwatch timestamp of the last mouse move. Written on the hook thread, read on the UI thread
    // — go through Interlocked/Volatile so the reader never sees a torn or stale value.
    private long _lastMoveTicks;

    // UI dispatcher captured at creation; the hook thread marshals subscription onto it.
    private Dispatcher? _dispatcher;

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
        if (_maxSmoothness)
        {
            // Pin the coalescing guard so the hook never posts a (redundant) wake while we already
            // hold a permanent subscription.
            Interlocked.Exchange(ref _wakeRequested, 1);
            SubscribeOnUi();
        }
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

        // Capture the UI dispatcher before wiring the hook, so the hook thread always has a valid
        // target to marshal subscription onto once events start firing.
        _dispatcher = _overlay.Dispatcher;

        _lastMoveTicks = Stopwatch.GetTimestamp();
        UpdatePosition();

        _motion = new MouseMotionWatcher(_reporter);
        _motion.MouseMoved += OnMouseMoved;
    }

    private void EnsureDestroyed()
    {
        if (_overlay == null) return;

        StopVisibilityPoll();

        // Tear the watcher down first so no further wakes are posted. Dispose joins its pump
        // thread, so OnMouseMoved cannot run after this returns; any BeginInvoke it already queued
        // will no-op once _overlay is null (this method runs to completion on the UI thread before
        // any queued continuation can).
        if (_motion != null)
        {
            _motion.MouseMoved -= OnMouseMoved;
            _motion.Dispose();
            _motion = null;
        }

        if (_renderingSubscribed)
        {
            CompositionTarget.Rendering -= OnRendering;
            _renderingSubscribed = false;
        }
        Interlocked.Exchange(ref _wakeRequested, 0);
        _dispatcher = null;

        _overlay.Close();
        _overlay = null;

        _viewModel?.Dispose();
        _viewModel = null;
    }

    // Runs on the watcher's dedicated pump thread (see MouseMotionWatcher), NOT the UI thread.
    // Raw input does not gate the system cursor, so this is never in the pointer's critical path;
    // we keep it trivial anyway. We only stamp the move time and, on the transition out of idle,
    // post a single subscribe request to the UI thread. The CompareExchange guarantees exactly one
    // BeginInvoke per motion burst — without it, a stream of high-frequency moves before the UI
    // thread flips _renderingSubscribed would each queue a redundant dispatcher item.
    private void OnMouseMoved()
    {
        Interlocked.Exchange(ref _lastMoveTicks, Stopwatch.GetTimestamp());
        if (Interlocked.CompareExchange(ref _wakeRequested, 1, 0) == 0)
            _dispatcher?.BeginInvoke(new Action(SubscribeOnUi));
    }

    // UI thread only (the dispatcher the hook marshals to, or a direct call in max-smoothness).
    private void SubscribeOnUi()
    {
        if (_overlay == null || _renderingSubscribed) return;
        _renderingSubscribed = true;
        StopVisibilityPoll();
        CompositionTarget.Rendering += OnRendering;
    }

    // UI thread only. Drops the per-frame tick and returns to rest, with a race-closing re-check.
    private void UnsubscribeOnUi()
    {
        if (!_renderingSubscribed) return;

        // Clear the coalescing guard BEFORE the final freshness re-check. This closes the tiny race
        // with the hook thread: a move that lands right after the idle check either (a) updated the
        // timestamp before our re-read — we see it's fresh and stay subscribed — or (b) lands after,
        // in which case its CompareExchange now succeeds (guard is 0) and posts a fresh wake that
        // re-subscribes us. Either way a just-resumed motion is never silently dropped.
        Interlocked.Exchange(ref _wakeRequested, 0);
        if (Stopwatch.GetElapsedTime(Volatile.Read(ref _lastMoveTicks)) <= IdleTimeout)
        {
            Interlocked.Exchange(ref _wakeRequested, 1);
            return;
        }

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

            if (Stopwatch.GetElapsedTime(Volatile.Read(ref _lastMoveTicks)) > IdleTimeout)
                UnsubscribeOnUi();
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
        var labelWidth = _overlay.ActualWidth > 0 ? _overlay.ActualWidth : DefaultLabelWidthDip;

        // Horizontally the chip is CENTRED on the cursor hotspot; vertically its TOP edge sits at the
        // hotspot when OffsetY is 0, so the chip hangs below the tip like a tooltip. Offsets move it
        // from there — X positive-right, Y positive-up (subtracted).
        _overlay.PositionInScreenPixels(
            p.X - labelWidth / 2 + settings.OffsetX,
            p.Y - settings.OffsetY);
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
