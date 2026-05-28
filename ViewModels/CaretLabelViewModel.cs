using OopsType.Models;
using OopsType.Services;
using Prism.Mvvm;

namespace OopsType.ViewModels;

public sealed class CaretLabelViewModel : BindableBase
{
    private readonly ISettingsService _settings;
    private string _code = "??";
    private string _fontFamily = "Segoe UI";
    private double _fontSize = 11;

    public string Code { get => _code; set => SetProperty(ref _code, value); }
    public string FontFamily { get => _fontFamily; set => SetProperty(ref _fontFamily, value); }
    public double FontSize { get => _fontSize; set => SetProperty(ref _fontSize, value); }

    public CaretLabelViewModel(ISettingsService settings)
    {
        _settings = settings;
        RefreshFromSettings();
    }

    public void Update(LanguageInfo info)
    {
        Code = info.DisplayLabel;
    }

    public void RefreshFromSettings()
    {
        var s = _settings.Current.CaretLabel;
        FontFamily = string.IsNullOrWhiteSpace(s.Font) ? "Segoe UI" : s.Font;
        FontSize = s.Size <= 0 ? 11 : s.Size;
    }
}
