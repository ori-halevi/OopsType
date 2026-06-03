using System;
using System.Windows;
using System.Windows.Media;
using OopsType.Infrastructure;
using OopsType.Models;
using OopsType.Services;
using Prism.Mvvm;
using WpfColor = System.Windows.Media.Color;

namespace OopsType.ViewModels;

/// <summary>
/// VM for the cursor-following label overlay. Mirrors <see cref="CaretLabelViewModel"/> but reads
/// its own settings section, so font/size/color changes for one overlay don't bleed into the other.
/// </summary>
public sealed class MouseLabelViewModel : BindableBase, IDisposable
{
    private const string DefaultFontFamily = "Segoe UI";
    private const double DefaultFontSize = 11;

    private static readonly Brush DefaultBackground = LabelStyleBrushes.Freeze(WpfColor.FromArgb(0xCC, 0x22, 0x22, 0x22));
    private static readonly Brush DefaultForeground = LabelStyleBrushes.Freeze(Colors.White);
    private static readonly Brush DefaultBorderBrush = LabelStyleBrushes.Freeze(Colors.Transparent);

    private readonly ISettingsService _settings;
    private readonly IKeyboardLayoutService _layout;
    private readonly IErrorReporter _reporter;

    private string _code = LanguageInfo.Unknown.DisplayLabel;
    private string _fontFamily = DefaultFontFamily;
    private double _fontSize = DefaultFontSize;
    private Brush _background = DefaultBackground;
    private Brush _foreground = DefaultForeground;
    private Brush _borderBrush = DefaultBorderBrush;
    private Thickness _borderThickness;
    private double _labelOpacity = 1.0;
    private FontWeight _fontWeight = FontWeights.Bold;
    private double _textOpacity = 1.0;

    private LanguageInfo _current = LanguageInfo.Unknown;

    public string Code { get => _code; private set => SetProperty(ref _code, value); }
    public string FontFamily { get => _fontFamily; private set => SetProperty(ref _fontFamily, value); }
    public double FontSize { get => _fontSize; private set => SetProperty(ref _fontSize, value); }
    public Brush Background { get => _background; private set => SetProperty(ref _background, value); }
    public Brush Foreground { get => _foreground; private set => SetProperty(ref _foreground, value); }
    public Brush BorderBrush { get => _borderBrush; private set => SetProperty(ref _borderBrush, value); }
    public Thickness BorderThickness { get => _borderThickness; private set => SetProperty(ref _borderThickness, value); }
    public double LabelOpacity { get => _labelOpacity; private set => SetProperty(ref _labelOpacity, value); }

    /// <summary>Weight of the label text glyphs. Resolved from <see cref="MouseLabelSettings.FontWeight"/>.</summary>
    public FontWeight FontWeight { get => _fontWeight; private set => SetProperty(ref _fontWeight, value); }

    /// <summary>Opacity of the label TEXT only (0 = invisible glyph, chip stays). Multiplies on top
    /// of <see cref="LabelOpacity"/>.</summary>
    public double TextOpacity { get => _textOpacity; private set => SetProperty(ref _textOpacity, value); }

    public MouseLabelViewModel(ISettingsService settings, IKeyboardLayoutService layout, IErrorReporter reporter)
    {
        _settings = settings;
        _layout = layout;
        _reporter = reporter;

        _settings.Changed += OnSettingsChanged;
        _layout.LanguageChanged += OnLanguageChanged;

        OnLanguageChanged(_layout.Current);
    }

    private void OnLanguageChanged(LanguageInfo info)
    {
        _current = info;
        Code = info.DisplayLabel;
        RefreshFromSettings();
    }

    private void OnSettingsChanged() => RefreshFromSettings();

    private void RefreshFromSettings()
    {
        var s = _settings.Current.MouseLabel;
        FontFamily = string.IsNullOrWhiteSpace(s.Font) ? DefaultFontFamily : s.Font;
        FontSize = s.Size <= 0 ? DefaultFontSize : s.Size;
        LabelOpacity = Math.Clamp(s.Opacity, 0.0, 1.0);
        FontWeight = LabelStyleBrushes.ParseFontWeight(s.FontWeight);
        TextOpacity = Math.Clamp(s.TextOpacity, 0.0, 1.0);

        var style = LabelStyleBrushes.Resolve(s.Colors, _current.TwoLetterCode, _reporter, "MouseLabelViewModel");
        Background = style.Background ?? DefaultBackground;
        Foreground = style.Foreground ?? DefaultForeground;
        BorderBrush = style.BorderBrush ?? DefaultBorderBrush;
        BorderThickness = style.BorderThickness;
    }

    public void Dispose()
    {
        _settings.Changed -= OnSettingsChanged;
        _layout.LanguageChanged -= OnLanguageChanged;
    }
}
