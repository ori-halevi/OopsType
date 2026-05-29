using System;
using System.Diagnostics;
using OopsType.Infrastructure;
using OopsType.Native;

namespace OopsType.Services;

public sealed class KeyboardActivityService : IKeyboardActivityService
{
    private readonly IErrorReporter _reporter;
    private LowLevelKeyboardHook? _hook;

    // Monotonic — see interface remarks. Initialized to "just now" so the first idle check
    // doesn't immediately trip before any user input has been seen.
    private long _lastKeyTicks = Stopwatch.GetTimestamp();

    public event Action? KeyPressed;

    public TimeSpan TimeSinceLastKey => Stopwatch.GetElapsedTime(_lastKeyTicks);

    public KeyboardActivityService(IErrorReporter reporter) => _reporter = reporter;

    public void Start()
    {
        if (_hook != null) return;
        _hook = new LowLevelKeyboardHook(_reporter);
        _hook.KeyPressed += OnKey;
    }

    public void ResetIdle() => _lastKeyTicks = Stopwatch.GetTimestamp();

    public void EnsureInstalled() => _hook?.EnsureInstalled();

    private void OnKey()
    {
        // Runs on the hook thread; _lastKeyTicks is read on the UI/timer thread. long writes are
        // atomic on 64-bit and a one-tick staleness here has no observable effect.
        _lastKeyTicks = Stopwatch.GetTimestamp();

        // Subscriber throws would be caught by the hook itself, but being explicit here keeps
        // the LL hook timeout budget tight even if a downstream handler is slow.
        try { KeyPressed?.Invoke(); }
        catch (Exception ex) { _reporter.Report("KeyboardActivityService.OnKey", ex); }
    }

    public void Dispose()
    {
        if (_hook != null)
        {
            _hook.KeyPressed -= OnKey;
            _hook.Dispose();
            _hook = null;
        }
    }
}
