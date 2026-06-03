using System;
using System.Windows;
using System.Windows.Threading;
using OopsType.Infrastructure;
using OopsType.ViewModels;
using OopsType.Views;

namespace OopsType.Services.Overlays;

/// <summary>
/// Presenter for the "caret label" overlay — a small floating chip pinned above the text caret
/// of whichever app currently has focus. Hidden when no caret is detectable.
/// </summary>
public sealed class CaretOverlayPresenter : IOverlayPresenter
{
    // Polling interval for chasing the caret. The caret can move between keystrokes (e.g. user
    // clicks into a different field), so we re-query on a timer rather than relying solely on
    // keyboard activity.
    private static readonly TimeSpan FollowInterval = TimeSpan.FromMilliseconds(120);

    // Fallback label height/width in DIPs used before the overlay has measured itself once.
    private const double DefaultLabelHeightDip = 18;
    private const double DefaultLabelWidthDip = 28;

    private readonly ISettingsService _settings;
    private readonly ICaretLocationService _caret;
    private readonly IKeyboardActivityService _activity;
    private readonly IKeyboardLayoutService _layout;
    private readonly IErrorReporter _reporter;
    private readonly Func<CaretLabelViewModel> _vmFactory;
    private readonly Func<CaretLabelOverlay> _viewFactory;

    private CaretLabelOverlay? _overlay;
    private CaretLabelViewModel? _viewModel;
    private DispatcherTimer? _followTimer;

    // Coalesces a burst of keypresses into a single deferred UpdatePosition — see OnKeyPressed.
    private bool _keyUpdateQueued;

    public CaretOverlayPresenter(
        ISettingsService settings,
        ICaretLocationService caret,
        IKeyboardActivityService activity,
        IKeyboardLayoutService layout,
        IErrorReporter reporter,
        Func<CaretLabelViewModel> vmFactory,
        Func<CaretLabelOverlay> viewFactory)
    {
        _settings = settings;
        _caret = caret;
        _activity = activity;
        _layout = layout;
        _reporter = reporter;
        _vmFactory = vmFactory;
        _viewFactory = viewFactory;

        // Re-position immediately on every keypress so the chip "snaps" to the caret as you type.
        _activity.KeyPressed += OnKeyPressed;
    }

    public void ApplySettings()
    {
        if (_settings.Current.CaretLabel.Enabled)
            EnsureCreated();
        else
            EnsureDestroyed();
    }

    public void Heartbeat()
    {
        _overlay?.EnsureTopmost();
    }

    /// <summary>
    /// KeyPressed fires synchronously from inside the LL keyboard hook callback. UpdatePosition
    /// calls into UI Automation (cross-process COM) which can block for seconds when a target
    /// app is unresponsive. If we ran it on the hook thread, blowing the LowLevelHooksTimeout
    /// (~300ms) would cause Windows to silently unhook us — killing all keyboard tracking
    /// permanently with no recovery. We defer the work to the dispatcher so the hook callback
    /// returns to the OS in microseconds, and coalesce concurrent posts so a fast typist doesn't
    /// flood the queue.
    /// </summary>
    private void OnKeyPressed()
    {
        if (_overlay == null || _keyUpdateQueued) return;

        var dispatcher = _overlay.Dispatcher ?? Application.Current?.Dispatcher;
        if (dispatcher == null) return;

        _keyUpdateQueued = true;
        dispatcher.BeginInvoke(new Action(() =>
        {
            _keyUpdateQueued = false;
            Safe.Invoke(_reporter, "CaretOverlayPresenter.OnKeyPressed", UpdatePosition);
        }), DispatcherPriority.Background);
    }

    private void EnsureCreated()
    {
        if (_overlay != null) return;

        _viewModel = _vmFactory();
        _overlay = _viewFactory();
        _overlay.DataContext = _viewModel;
        _overlay.Show();

        // Hide until we have a caret to anchor to — Show() flashes a frame at (-32000,-32000)
        // otherwise (the offscreen sentinel set by OverlayWindowBase).
        _overlay.Visibility = Visibility.Hidden;
        _overlay.EnsureTopmost();
        UpdatePosition();

        _followTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = FollowInterval };
        _followTimer.Tick += (_, _) => Safe.Invoke(_reporter, "CaretOverlayPresenter.FollowTick", UpdatePosition);
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

        var info = _caret.GetCaretRect();
        if (!info.Found)
        {
            if (_overlay.Visibility == Visibility.Visible)
                _overlay.Visibility = Visibility.Hidden;
            return;
        }

        var settings = _settings.Current.CaretLabel;
        var labelHeight = _overlay.ActualHeight > 0 ? _overlay.ActualHeight : DefaultLabelHeightDip;
        var labelWidth = _overlay.ActualWidth > 0 ? _overlay.ActualWidth : DefaultLabelWidthDip;
        var caret = info.ScreenRect;

        // The chip is anchored by its CENTRE: offset (0,0) centres it on the caret, and the user
        // offsets move that centre — X positive-right, Y positive-up (subtracted). Horizontal "auto"
        // mode is the exception: it parks the chip beside the caret by language instead (see ComputeX).
        var x = ComputeX(caret, settings, labelWidth);
        var y = caret.Y + caret.Height / 2 - labelHeight / 2 - settings.OffsetY;

        _overlay.PositionInScreenPixels(x, y);
        if (_overlay.Visibility != Visibility.Visible)
            _overlay.Visibility = Visibility.Visible;
        _overlay.EnsureTopmost();
    }

    /// <summary>
    /// Horizontal anchor for the chip's left edge.
    ///   "auto" — choose the side from the active keyboard language so the chip stays clear of the
    ///            text: an RTL language (Hebrew/Arabic) puts it to the LEFT of the caret, an LTR
    ///            language to the RIGHT, separated by the configured (non-negative) distance. This
    ///            lets the user switch languages without the chip ever covering what they type.
    ///   "offset" — the chip is centred on the caret, then shifted by the signed OffsetX.
    /// </summary>
    private double ComputeX(Rect caret, Models.CaretLabelSettings settings, double labelWidth)
    {
        if (!string.Equals(settings.HorizontalMode, "auto", StringComparison.OrdinalIgnoreCase))
            // Centre the chip on the caret, then apply the signed offset (positive moves it right).
            return caret.X + caret.Width / 2 - labelWidth / 2 + settings.OffsetX;

        var distance = Math.Max(0, settings.HorizontalDistance);
        if (_layout.Current.IsRtl)
        {
            // Chip to the LEFT of the caret: its right edge sits `distance` left of the caret, so
            // its left edge is a further label-width to the left.
            return caret.X - distance - labelWidth;
        }

        // LTR: chip to the RIGHT of the caret, starting `distance` past the caret's right edge.
        return caret.X + caret.Width + distance;
    }

    public void Dispose()
    {
        _activity.KeyPressed -= OnKeyPressed;
        EnsureDestroyed();
    }
}
