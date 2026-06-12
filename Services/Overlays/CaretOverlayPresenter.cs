using System;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using OopsType.Infrastructure;
using OopsType.Models;
using OopsType.ViewModels;
using OopsType.Views;

namespace OopsType.Services.Overlays;

/// <summary>
/// Presenter for the "caret label" overlay — a small floating chip pinned above the text caret
/// of whichever app currently has focus. Hidden when no caret is detectable.
///
/// <para><b>Threading:</b> resolving the caret rect goes through UI Automation, a cross-process COM
/// call that <i>can block for seconds</i> when the focused app is busy. Running it on the UI thread
/// would stall every overlay's per-frame work — and, because the mouse hook used to share that
/// thread, froze the whole system's pointer under load. So the query runs on a dedicated MTA worker
/// (<see cref="CaretWorkerLoop"/>, the apartment UI Automation clients are meant to use); the worker
/// hands the plain-value <see cref="CaretInfo"/> back to the UI thread, which does only the cheap
/// positioning. The follow timer and keypresses merely <see cref="SignalQuery">wake the worker</see>,
/// and the <see cref="AutoResetEvent"/> coalesces a burst into a single query.</para>
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

    // Dedicated MTA worker that runs the (potentially blocking) UI Automation query off the UI
    // thread, plus the signal that wakes it and the UI dispatcher it marshals results back onto.
    private Thread? _worker;
    private AutoResetEvent? _workSignal;
    private volatile bool _workerStop;
    private Dispatcher? _dispatcher;

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

        // Re-position on every keypress so the chip "snaps" to the caret as you type.
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
    /// KeyPressed fires synchronously from inside the LL keyboard hook callback. Keep it trivial:
    /// just wake the UIA worker. The cross-process query then runs on that worker — never on the
    /// hook thread (a slow call there would blow LowLevelHooksTimeout and get us unhooked) nor on
    /// the UI thread (which it would stall). The AutoResetEvent coalesces a fast typist's burst into
    /// a single query.
    /// </summary>
    private void OnKeyPressed() => SignalQuery();

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

        _dispatcher = _overlay.Dispatcher;
        StartWorker();
        SignalQuery();  // initial position (resolved off-thread, applied back on the UI thread)

        _followTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = FollowInterval };
        _followTimer.Tick += (_, _) => SignalQuery();
        _followTimer.Start();
    }

    private void EnsureDestroyed()
    {
        if (_overlay == null) return;

        _followTimer?.Stop();
        _followTimer = null;

        StopWorker();
        _dispatcher = null;

        _overlay.Close();
        _overlay = null;

        _viewModel?.Dispose();
        _viewModel = null;
    }

    private void StartWorker()
    {
        _workerStop = false;
        _workSignal = new AutoResetEvent(false);
        _worker = new Thread(CaretWorkerLoop)
        {
            IsBackground = true,
            Name = "OopsType.CaretUia",
        };
        // UI Automation clients are meant to call from an MTA thread, not the app's STA UI thread.
        _worker.SetApartmentState(ApartmentState.MTA);
        _worker.Start();
    }

    private void StopWorker()
    {
        var worker = _worker;
        var signal = _workSignal;

        _workerStop = true;
        signal?.Set();

        // Only dispose the signal once the worker has actually exited. If it's mid-query (UIA can
        // block for seconds) the Join times out; the worker is a background thread that re-checks
        // _workerStop right after its WaitOne and exits on its own. Disposing the signal out from
        // under a live worker would throw on its next WaitOne and take the process down, so we leak
        // the one handle (reclaimed at process exit) rather than risk that.
        if (worker != null && worker.Join(TimeSpan.FromSeconds(2)))
        {
            signal?.Dispose();
            _workSignal = null;
        }

        _worker = null;
    }

    private void SignalQuery() => _workSignal?.Set();

    /// <summary>
    /// Runs on the dedicated MTA worker thread. Blocks on the signal, runs the (possibly slow) UI
    /// Automation query when woken, and marshals the plain-value result back to the UI thread.
    /// </summary>
    private void CaretWorkerLoop()
    {
        var signal = _workSignal;
        if (signal == null) return;

        while (true)
        {
            signal.WaitOne();
            if (_workerStop) return;

            CaretInfo info;
            try
            {
                info = _caret.GetCaretRect();
            }
            catch (Exception ex)
            {
                _reporter.Report("CaretOverlayPresenter.CaretWorker", ex);
                continue;
            }

            // CaretInfo is a pure value (bool + Rect + enum) — nothing UIA-bound crosses threads.
            _dispatcher?.BeginInvoke(new Action(() => ApplyCaretInfo(info)));
        }
    }

    /// <summary>Positions the overlay from an already-resolved caret rect. UI thread only.</summary>
    private void ApplyCaretInfo(CaretInfo info)
    {
        if (_overlay == null) return;

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
