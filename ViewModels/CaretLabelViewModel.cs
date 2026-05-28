using System;
using OopsType.Models;
using OopsType.Services;
using Prism.Mvvm;

namespace OopsType.ViewModels;

/// <summary>
/// VM for the caret-following label overlay. Subscribes directly to the layout and settings
/// services so it stays in sync without the presenter having to push updates — a cleaner MVVM
/// separation than the previous "external <c>Update()</c>" pattern.
/// </summary>
public sealed class CaretLabelViewModel : BindableBase, IDisposable
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

    public CaretLabelViewModel(ISettingsService settings, IKeyboardLayoutService layout)
    {
        _settings = settings;
        _layout = layout;

        _settings.Changed += OnSettingsChanged;
        _layout.LanguageChanged += OnLanguageChanged;

        // Seed initial state so the overlay never renders the placeholder "??" on first show.
        RefreshFromSettings();
        OnLanguageChanged(_layout.Current);
    }

    private void OnLanguageChanged(LanguageInfo info) => Code = info.DisplayLabel;

    private void OnSettingsChanged() => RefreshFromSettings();

    private void RefreshFromSettings()
    {
        var s = _settings.Current.CaretLabel;
        FontFamily = string.IsNullOrWhiteSpace(s.Font) ? DefaultFontFamily : s.Font;
        FontSize = s.Size <= 0 ? DefaultFontSize : s.Size;
    }

    public void Dispose()
    {
        _settings.Changed -= OnSettingsChanged;
        _layout.LanguageChanged -= OnLanguageChanged;
    }
}
