using System.Windows;
using Prism.Mvvm;

namespace OopsType.ViewModels;

/// <summary>One editable row in a caret/mouse label color table: a language code mapped to the
/// chip's background, text and border appearance. <see cref="BorderThickness"/> of 0 = no border.</summary>
public sealed class LabelStyleRow : BindableBase
{
    private string _code = "";
    private string _background = "#CC222222";
    private string _foreground = "#FFFFFFFF";
    private string _borderColor = "#FF000000";
    private double _borderThickness;
    public string Code { get => _code; set => SetProperty(ref _code, value); }
    public string Background { get => _background; set => SetProperty(ref _background, value); }
    public string Foreground { get => _foreground; set => SetProperty(ref _foreground, value); }
    public string BorderColor { get => _borderColor; set => SetProperty(ref _borderColor, value); }
    public double BorderThickness
    {
        get => _borderThickness;
        set { if (SetProperty(ref _borderThickness, value)) RaisePropertyChanged(nameof(BorderThicknessUniform)); }
    }

    /// <summary>Uniform <see cref="Thickness"/> projection of <see cref="BorderThickness"/> for the
    /// swatch's BorderThickness binding (Border expects a Thickness, not a bare double).</summary>
    public Thickness BorderThicknessUniform => new(_borderThickness);
}
