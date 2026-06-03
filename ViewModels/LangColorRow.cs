using Prism.Mvvm;

namespace OopsType.ViewModels;

/// <summary>One editable row in the taskbar-strip color table: a language code mapped to a single
/// hex color. Edited in the strip settings page; persisted into <c>TaskbarStrip.Colors</c>.</summary>
public sealed class LangColorRow : BindableBase
{
    private string _code = "";
    private string _color = "#888888";
    public string Code { get => _code; set => SetProperty(ref _code, value); }
    public string Color { get => _color; set => SetProperty(ref _color, value); }
}
