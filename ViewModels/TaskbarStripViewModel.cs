using System;
using System.Windows.Media;
using OopsType.Models;
using OopsType.Services;
using Prism.Mvvm;
using WpfColor = System.Windows.Media.Color;

namespace OopsType.ViewModels;

/// <summary>
/// VM for the taskbar color strip — exposes a single <see cref="Brush"/> that the strip renders.
/// The brush is derived from the user-configured per-language palette in settings.
/// </summary>
public sealed class TaskbarStripViewModel : BindableBase, IDisposable
{
    // Neutral gray fallback used when settings have no color for the current language or when the
    // configured hex string fails to parse. Frozen so it can cross threads without contention.
    private static readonly Brush FallbackBrush = MakeFrozen(WpfColor.FromArgb(160, 128, 128, 128));

    private readonly ISettingsService _settings;
    private readonly IKeyboardLayoutService _layout;

    private Brush _color = FallbackBrush;
    public Brush Color { get => _color; private set => SetProperty(ref _color, value); }

    public TaskbarStripViewModel(ISettingsService settings, IKeyboardLayoutService layout)
    {
        _settings = settings;
        _layout = layout;

        _settings.Changed += OnSettingsChanged;
        _layout.LanguageChanged += OnLanguageChanged;

        OnLanguageChanged(_layout.Current);
    }

    private void OnLanguageChanged(LanguageInfo info)
    {
        var colors = _settings.Current.TaskbarStrip.Colors;
        if (colors != null
            && colors.TryGetValue(info.TwoLetterCode.ToLowerInvariant(), out var hex)
            && TryParseHex(hex, out var brush))
        {
            Color = brush;
            return;
        }

        Color = FallbackBrush;
    }

    // Colors are stored on the settings, so on settings.Changed we re-resolve against the current
    // language without waiting for the next layout change.
    private void OnSettingsChanged() => OnLanguageChanged(_layout.Current);

    private static bool TryParseHex(string hex, out Brush brush)
    {
        brush = FallbackBrush;
        try
        {
            var c = (WpfColor)ColorConverter.ConvertFromString(hex);
            brush = MakeFrozen(c);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static Brush MakeFrozen(WpfColor c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    public void Dispose()
    {
        _settings.Changed -= OnSettingsChanged;
        _layout.LanguageChanged -= OnLanguageChanged;
    }
}
