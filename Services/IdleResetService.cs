using System;
using System.Windows.Threading;

namespace OopsType.Services;

public sealed class IdleResetService : IIdleResetService
{
    private readonly ISettingsService _settings;
    private readonly IKeyboardActivityService _activity;
    private readonly IKeyboardLayoutService _layout;
    private readonly DispatcherTimer _tick;
    private bool _alreadyReset;

    public IdleResetService(ISettingsService settings, IKeyboardActivityService activity, IKeyboardLayoutService layout)
    {
        _settings = settings;
        _activity = activity;
        _layout = layout;
        _tick = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1),
        };
        _tick.Tick += (_, _) => Check();
    }

    public void Start()
    {
        _activity.KeyPressed += () => _alreadyReset = false;
        _tick.Start();
    }

    public void Reconfigure() { /* settings read on each tick — nothing to do */ }

    private void Check()
    {
        var s = _settings.Current.IdleReset;
        if (!s.Enabled) return;
        if (_alreadyReset) return;
        var idle = (DateTime.UtcNow - _activity.LastKeyTimeUtc).TotalSeconds;
        if (idle < s.IdleSeconds) return;

        // already in target? skip.
        if (string.Equals(_layout.Current.TwoLetterCode, s.TargetLang, StringComparison.OrdinalIgnoreCase))
        {
            _alreadyReset = true;
            return;
        }
        if (_layout.RequestLanguage(s.TargetLang))
            _alreadyReset = true;
    }

    public void Dispose() => _tick.Stop();
}
