using System;
using OopsType.Infrastructure;
using OopsType.Native;

namespace OopsType.Services;

public sealed class KeyboardActivityService : IKeyboardActivityService
{
    private readonly IErrorReporter _reporter;
    private LowLevelKeyboardHook? _hook;

    public event Action? KeyPressed;
    public DateTime LastKeyTimeUtc { get; private set; } = DateTime.UtcNow;

    public KeyboardActivityService(IErrorReporter reporter) => _reporter = reporter;

    public void Start()
    {
        if (_hook != null) return;
        _hook = new LowLevelKeyboardHook(_reporter);
        _hook.KeyPressed += OnKey;
    }

    private void OnKey()
    {
        // Runs on the hook thread; LastKeyTimeUtc is read on the UI/timer thread, but DateTime
        // writes are atomic on 64-bit and a one-tick staleness here has no observable effect.
        LastKeyTimeUtc = DateTime.UtcNow;

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
