using System;
using OopsType.Models;
using OopsType.Services;
using Prism.Mvvm;

namespace OopsType.ViewModels;

/// <summary>
/// VM for the cursor-following label overlay. Mirrors <see cref="CaretLabelViewModel"/> but reads
/// its own settings section, so font/size changes for one overlay don't bleed into the other.
/// </summary>
public sealed class MouseLabelViewModel : BindableBase, IDisposable
{
    private const string DefaultFontFamily = "Segoe UI";
    private const double DefaultFontSize = 11;

    private readonly ISettingsService _settings;
    private readonly IKeyboardLayoutService _layout;

    private string _code = LanguageInfo.Unknown.DisplayLabel;
    private string _fontFamily = DefaultFontFamily;
    private double _fontSize = DefaultFontSize;

    public string Code { get => _code; private set => SetProperty(ref _code, value); }
    public string FontFamily { get => _fontFamily; private set => SetProperty(ref _fontFamily, value); }
    public double FontSize { get => _fontSize; private set => SetProperty(ref _fontSize, value); }

    public MouseLabelViewModel(ISettingsService settings, IKeyboardLayoutService layout)
    {
        _settings = settings;
        _layout = layout;

        _settings.Changed += OnSettingsChanged;
        _layout.LanguageChanged += OnLanguageChanged;

        RefreshFromSettings();
        OnLanguageChanged(_layout.Current);
    }

    private void OnLanguageChanged(LanguageInfo info) => Code = info.DisplayLabel;

    private void OnSettingsChanged() => RefreshFromSettings();

    private void RefreshFromSettings()
    {
        var s = _settings.Current.MouseLabel;
        FontFamily = string.IsNullOrWhiteSpace(s.Font) ? DefaultFontFamily : s.Font;
        FontSize = s.Size <= 0 ? DefaultFontSize : s.Size;
    }

    public void Dispose()
    {
        _settings.Changed -= OnSettingsChanged;
        _layout.LanguageChanged -= OnLanguageChanged;
    }
}
