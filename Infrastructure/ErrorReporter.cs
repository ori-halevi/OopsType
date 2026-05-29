using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace OopsType.Infrastructure;

/// <summary>
/// Default <see cref="IErrorReporter"/>. Always logs; throttles user-facing notifications to at
/// most one per <see cref="ThrottleWindow"/> per source, so a per-frame failure (e.g. a broken
/// caret query firing 8×/sec) can't carpet-bomb the user with tray balloons.
///
/// <para>Throttle uses <see cref="Stopwatch"/> (monotonic) instead of <see cref="DateTime.UtcNow"/>:
/// an NTP correction, manual clock change, or DST flip won't suppress notifications until the
/// wall clock catches up to a "future" timestamp we wrote earlier.</para>
/// </summary>
public sealed class ErrorReporter : IErrorReporter
{
    // Per-source cooldown. 30s is generous — a chronic failure still surfaces ~twice a minute,
    // but a one-off blip in a tick handler produces exactly one balloon.
    private static readonly TimeSpan ThrottleWindow = TimeSpan.FromSeconds(30);

    private readonly ILogger _logger;
    private readonly object _gate = new();
    private readonly Dictionary<string, long> _lastNotifyTicks =
        new(StringComparer.OrdinalIgnoreCase);

    public event Action<ErrorNotification>? Notified;

    public ErrorReporter(ILogger logger) => _logger = logger;

    public void Report(string source, Exception ex)
    {
        _logger.Error(source, ex);
        MaybeNotify(source, ex.Message);
    }

    public void Report(string source, string message)
    {
        _logger.Warn(source, message);
        MaybeNotify(source, message);
    }

    private void MaybeNotify(string source, string message)
    {
        bool shouldFire;
        lock (_gate)
        {
            var now = Stopwatch.GetTimestamp();
            if (_lastNotifyTicks.TryGetValue(source, out var last)
                && Stopwatch.GetElapsedTime(last) < ThrottleWindow)
            {
                shouldFire = false;
            }
            else
            {
                _lastNotifyTicks[source] = now;
                shouldFire = true;
            }
        }
        if (!shouldFire) return;

        // A buggy listener must not crash the reporter (it's used inside global handlers).
        try { Notified?.Invoke(new ErrorNotification(source, message)); }
        catch (Exception ex) { _logger.Error("ErrorReporter.Notified", ex); }
    }
}
