using OopsType.Infrastructure;
using OopsType.Models;
using OopsType.Services;

namespace OopsType.ViewModels;

/// <summary>
/// VM for the cursor-following label overlay. Reads the <see cref="AppSettings.MouseLabel"/> section;
/// shares all behavior with <see cref="LabelOverlayViewModel"/>, so font/size/color changes for one
/// overlay never bleed into the other (each reads its own settings section).
/// </summary>
public sealed class MouseLabelViewModel : LabelOverlayViewModel
{
    public MouseLabelViewModel(ISettingsService settings, IKeyboardLayoutService layout, IErrorReporter reporter)
        : base(settings, layout, reporter) { }

    protected override ILabelStyleSettings GetSettings(AppSettings settings) => settings.MouseLabel;
    protected override string ReportSource => nameof(MouseLabelViewModel);
}
