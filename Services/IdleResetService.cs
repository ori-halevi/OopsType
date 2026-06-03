using System;
using System.Windows.Threading;
using OopsType.Infrastructure;

namespace OopsType.Services;

/// <summary>
/// Watches keyboard idle time and, when the user goes quiet long enough, switches the
/// foreground window's keyboard layout to a configured "default" language. Useful for
/// people who frequently leave their layout in the wrong state after typing.
/// </summary>
public sealed class IdleResetService : IIdleResetService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(1);

    private readonly ISettingsService _settings;
    private readonly IKeyboardActivityService _activity;
    private readonly IKeyboardLayoutService _layout;
    private readonly IErrorReporter _reporter;
    private readonly DispatcherTimer _tick;

    // Latch: once we've reset for this idle stretch, don't reset again until the user types.
    // Otherwise we'd fight the user every second they continue to idle.
    private bool _alreadyReset;

    public IdleResetService(
        ISettingsService settings,
        IKeyboardActivityService activity,
        IKeyboardLayoutService layout,
        IErrorReporter reporter)
    {
        _settings = settings;
        _activity = activity;
        _layout = layout;
        _reporter = reporter;

        _tick = new DispatcherTimer(DispatcherPriority.Background) { Interval = TickInterval };
        _tick.Tick += (_, _) => Safe.Invoke(_reporter, "IdleResetService.Tick", Check);
    }

    public void Start()
    {
        _activity.KeyPressed += OnKeyPressed;
        _tick.Start();
    }

    public void Dispose()
    {
        _activity.KeyPressed -= OnKeyPressed;
        _tick.Stop();
    }

    private void OnKeyPressed() => _alreadyReset = false;

    private void Check()
    {
        var cfg = _settings.Current.IdleReset;
        if (!cfg.Enabled || _alreadyReset) return;

        // TimeSinceLastKey is a monotonic Stopwatch delta — wall-clock jumps (NTP, DST, manual
        // time change) can't trip us into an unwanted reset, and can't suppress a real one
        // forever the way DateTime-based math would.
        if (_activity.TimeSinceLastKey.TotalSeconds < cfg.IdleSeconds) return;

        // Already in the target language: nothing to do, but latch so we don't re-check on the
        // next tick (cheap, but tidier).
        if (string.Equals(_layout.Current.TwoLetterCode, cfg.TargetLang, StringComparison.OrdinalIgnoreCase))
        {
            _alreadyReset = true;
            return;
        }

        if (_layout.RequestLanguage(cfg.TargetLang))
            _alreadyReset = true;
    }
}
