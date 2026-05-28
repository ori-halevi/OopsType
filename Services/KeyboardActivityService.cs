using System;
using OopsType.Native;

namespace OopsType.Services;

public sealed class KeyboardActivityService : IKeyboardActivityService
{
    private LowLevelKeyboardHook? _hook;

    public event Action? KeyPressed;
    public DateTime LastKeyTimeUtc { get; private set; } = DateTime.UtcNow;

    public void Start()
    {
        if (_hook != null) return;
        _hook = new LowLevelKeyboardHook();
        _hook.KeyPressed += OnKey;
    }

    private void OnKey()
    {
        LastKeyTimeUtc = DateTime.UtcNow;
        KeyPressed?.Invoke();
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
