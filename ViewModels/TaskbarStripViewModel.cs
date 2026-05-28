using System.Windows.Media;
using OopsType.Models;
using OopsType.Services;
using Prism.Mvvm;
using WpfColor = System.Windows.Media.Color;

namespace OopsType.ViewModels;

public sealed class TaskbarStripViewModel : BindableBase
{
    private static readonly Brush FallbackBrush = MakeFrozen(WpfColor.FromArgb(160, 128, 128, 128));

    private readonly ISettingsService _settings;
    private Brush _color = FallbackBrush;

    public Brush Color { get => _color; set => SetProperty(ref _color, value); }

    public TaskbarStripViewModel(ISettingsService settings) => _settings = settings;

    public void Update(LanguageInfo info)
    {
        var colors = _settings.Current.TaskbarStrip.Colors;
        if (colors != null
            && colors.TryGetValue(info.TwoLetterCode.ToLowerInvariant(), out var hex)
            && TryParse(hex, out var brush))
        {
            Color = brush;
            return;
        }
        Color = FallbackBrush;
    }

    private static bool TryParse(string hex, out Brush brush)
    {
        brush = FallbackBrush;
        try
        {
            var c = (WpfColor)ColorConverter.ConvertFromString(hex);
            brush = MakeFrozen(c);
            return true;
        }
        catch { return false; }
    }

    private static Brush MakeFrozen(WpfColor c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
}
