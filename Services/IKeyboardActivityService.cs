using System;

namespace OopsType.Services;

public interface IKeyboardActivityService : IDisposable
{
    event Action? KeyPressed;

    /// <summary>
    /// Time elapsed since the last keypress, measured from a monotonic clock (<see cref="System.Diagnostics.Stopwatch"/>).
    /// Safe against wall-clock jumps (NTP correction, DST, manual time change) — for an app meant
    /// to run 24/7, idle detection must not depend on <see cref="DateTime.UtcNow"/>.
    /// </summary>
    TimeSpan TimeSinceLastKey { get; }

    void Start();

    /// <summary>
    /// Treat now as "just had a keypress" — used on resume from sleep so an 8-hour suspend
    /// doesn't immediately trip the idle-reset latch the moment the user logs back in.
    /// </summary>
    void ResetIdle();

    /// <summary>
    /// If the underlying LL hook is not currently installed (initial install failed, or Windows
    /// silently unhooked us due to LowLevelHooksTimeout), try to install it again. Cheap when
    /// already installed — safe to call from a watchdog tick.
    /// </summary>
    void EnsureInstalled();
}
