using System;
using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using OopsType.Infrastructure;
using OopsType.Models;
using OopsType.Native;
using OopsType.ViewModels;
using OopsType.Views;

namespace OopsType.Services.Overlays;

/// <summary>
/// The momentary chip shown after a selection is converted.
///
/// <para>Unlike the other presenters this overlay is event-driven rather than always-on: it is
/// created once the feature is enabled and then sits hidden until <see cref="Flash"/> is called.
/// It is a separate window from the caret label on purpose — the caret label has its own enable
/// toggle, and a user who runs the mouse label instead would otherwise get conversions with no
/// feedback at all, which is the one combination we cannot allow.</para>
/// </summary>
public sealed class ConvertChipPresenter : IOverlayPresenter
{
    private static readonly TimeSpan HoldDuration = TimeSpan.FromMilliseconds(700);
    private static readonly TimeSpan FadeDuration = TimeSpan.FromMilliseconds(250);

    // Gap in DIPs between the caret (or cursor) and the chip. The chip sits ABOVE the anchor so it
    // never covers the text it is reporting on.
    private const double VerticalGapDip = 6;
    private const double DefaultChipHeightDip = 20;
    private const double DefaultChipWidthDip = 44;

    private readonly ISettingsService _settings;
    private readonly IErrorReporter _reporter;
    private readonly Func<ConvertChipViewModel> _vmFactory;
    private readonly Func<ConvertChipOverlay> _viewFactory;

    private ConvertChipOverlay? _overlay;
    private ConvertChipViewModel? _viewModel;
    private DispatcherTimer? _holdTimer;

    public ConvertChipPresenter(
        ISettingsService settings,
        IErrorReporter reporter,
        Func<ConvertChipViewModel> vmFactory,
        Func<ConvertChipOverlay> viewFactory)
    {
        _settings = settings;
        _reporter = reporter;
        _vmFactory = vmFactory;
        _viewFactory = viewFactory;
    }

    public void ApplySettings()
    {
        var cfg = _settings.Current.ConvertSelection;
        if (cfg.Enabled && cfg.ShowChip)
            EnsureCreated();
        else
            EnsureDestroyed();
    }

    public void Heartbeat()
    {
        // Only worth re-asserting while actually on screen; the window spends nearly all its life
        // hidden, and a hidden window's z-order is nobody's problem.
        if (_overlay is { Visibility: Visibility.Visible }) _overlay.EnsureTopmost();
    }

    /// <summary>
    /// Shows the chip at <paramref name="caret"/> for a moment, then fades it out. UI thread only.
    /// Falls back to the mouse cursor when the caret could not be resolved — after a successful
    /// conversion there is certainly a text field in play, but not every app exposes its caret.
    /// </summary>
    public void Flash(CaretInfo caret, LanguageInfo target)
    {
        try
        {
            EnsureCreated();
            if (_overlay == null || _viewModel == null) return;

            _viewModel.Show(target);

            // Measure before positioning: the chip's width depends on the label we just set, and
            // SizeToContent only resolves it once the layout pass has run.
            _overlay.UpdateLayout();
            var width = _overlay.ActualWidth > 0 ? _overlay.ActualWidth : DefaultChipWidthDip;
            var height = _overlay.ActualHeight > 0 ? _overlay.ActualHeight : DefaultChipHeightDip;

            if (!TryResolveAnchor(caret, out var anchor)) return;

            // The anchor is in SCREEN PIXELS (that is what the caret service reports) while the
            // measurements above are in DIPs, so they cannot be mixed before PositionInScreenPixels
            // divides the total back out by the DPI. Scaling the chip's own dimensions up first keeps
            // the gap a real gap at 150%/200% instead of shrinking it until the chip sits on the text.
            var dpi = _overlay.DpiScale;
            var widthPx = width * dpi.X;
            var heightPx = height * dpi.Y;
            var gapPx = VerticalGapDip * dpi.Y;

            _overlay.PositionInScreenPixels(
                anchor.X + anchor.Width / 2 - widthPx / 2,
                anchor.Y - heightPx - gapPx);

            _holdTimer?.Stop();
            _overlay.BeginAnimation(UIElement.OpacityProperty, null);
            _overlay.Opacity = 1.0;
            _overlay.Visibility = Visibility.Visible;
            _overlay.EnsureTopmost();

            _holdTimer ??= CreateHoldTimer();
            _holdTimer.Start();
        }
        catch (Exception ex)
        {
            _reporter.Report("ConvertChipPresenter.Flash", ex);
        }
    }

    private DispatcherTimer CreateHoldTimer()
    {
        var timer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = HoldDuration };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            Safe.Invoke(_reporter, "ConvertChipPresenter.Fade", BeginFadeOut);
        };
        return timer;
    }

    private void BeginFadeOut()
    {
        if (_overlay == null) return;

        var overlay = _overlay;
        var fade = new DoubleAnimation(1.0, 0.0, new Duration(FadeDuration));
        fade.Completed += (_, _) =>
        {
            // The window may have been torn down (settings toggled off) while the fade was running.
            if (overlay != _overlay) return;
            overlay.Visibility = Visibility.Hidden;
            overlay.BeginAnimation(UIElement.OpacityProperty, null);
            overlay.Opacity = 1.0;
        };
        overlay.BeginAnimation(UIElement.OpacityProperty, fade);
    }

    /// <summary>
    /// Screen-pixel rect the chip hangs above: the caret when UI Automation found one, otherwise the
    /// mouse cursor, which is a cheap always-available anchor near where the user is looking.
    /// </summary>
    private static bool TryResolveAnchor(CaretInfo caret, out Rect anchor)
    {
        if (caret.Found && caret.ScreenRect.Height > 0)
        {
            anchor = caret.ScreenRect;
            return true;
        }

        if (NativeMethods.GetCursorPos(out var pt))
        {
            anchor = new Rect(pt.X, pt.Y, 0, 0);
            return true;
        }

        anchor = default;
        return false;
    }

    private void EnsureCreated()
    {
        if (_overlay != null) return;

        _viewModel = _vmFactory();
        _overlay = _viewFactory();
        _overlay.DataContext = _viewModel;
        _overlay.Show();

        // Show() parks the window at the offscreen sentinel; keep it hidden until the first flash
        // so no stray frame appears at (-32000, -32000).
        _overlay.Visibility = Visibility.Hidden;
        _overlay.EnsureTopmost();
    }

    private void EnsureDestroyed()
    {
        if (_overlay == null) return;

        _holdTimer?.Stop();
        _holdTimer = null;

        _overlay.BeginAnimation(UIElement.OpacityProperty, null);
        _overlay.Close();
        _overlay = null;
        _viewModel = null;
    }

    public void Dispose() => EnsureDestroyed();
}
