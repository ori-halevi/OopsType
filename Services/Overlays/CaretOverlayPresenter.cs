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

    // Fallback label height in DIPs used before the overlay has measured itself once.
    private const double DefaultLabelHeightDip = 18;

    private readonly ISettingsService _settings;
    private readonly ICaretLocationService _caret;
    private readonly IKeyboardActivityService _activity;
    private readonly IErrorReporter _reporter;
    private readonly Func<CaretLabelViewModel> _vmFactory;
    private readonly Func<CaretLabelOverlay> _viewFactory;

    private CaretLabelOverlay? _overlay;
    private CaretLabelViewModel? _viewModel;
    private DispatcherTimer? _followTimer;

    public CaretOverlayPresenter(
        ISettingsService settings,
        ICaretLocationService caret,
        IKeyboardActivityService activity,
        IErrorReporter reporter,
        Func<CaretLabelViewModel> vmFactory,
        Func<CaretLabelOverlay> viewFactory)
    {
        _settings = settings;
        _caret = caret;
        _activity = activity;
        _reporter = reporter;
        _vmFactory = vmFactory;
        _viewFactory = viewFactory;

        // Re-position immediately on every keypress so the chip "snaps" to the caret as you type.
        // Wrapped — this handler runs on the LL hook callback thread (via KeyboardActivityService);
        // we MUST NOT throw back into the hook chain.
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

    private void OnKeyPressed() =>
        Safe.Invoke(_reporter, "CaretOverlayPresenter.OnKeyPressed", UpdatePosition);

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

        // Anchor: top of caret minus the chip's height — i.e. just above the caret line. User
        // offsets (OffsetX/Y) are layered on top so the chip can sit, e.g., to the side instead.
        var x = info.ScreenRect.X + settings.OffsetX;
        var y = info.ScreenRect.Y - labelHeight + settings.OffsetY;

        _overlay.PositionInScreenPixels(x, y);
        if (_overlay.Visibility != Visibility.Visible)
            _overlay.Visibility = Visibility.Visible;
        _overlay.EnsureTopmost();
    }

    public void Dispose()
    {
        _activity.KeyPressed -= OnKeyPressed;
        EnsureDestroyed();
    }
}
