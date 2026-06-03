using OopsType.Infrastructure;
using OopsType.Models;
using OopsType.Services;

namespace OopsType.ViewModels;

/// <summary>
/// VM for the caret-following label overlay. Reads the <see cref="AppSettings.CaretLabel"/> section;
/// all the layout/settings subscription and brush projection lives in <see cref="LabelOverlayViewModel"/>.
/// </summary>
public sealed class CaretLabelViewModel : LabelOverlayViewModel
{
    public CaretLabelViewModel(ISettingsService settings, IKeyboardLayoutService layout, IErrorReporter reporter)
        : base(settings, layout, reporter) { }

    protected override ILabelStyleSettings GetSettings(AppSettings settings) => settings.CaretLabel;
    protected override string ReportSource => nameof(CaretLabelViewModel);
}
