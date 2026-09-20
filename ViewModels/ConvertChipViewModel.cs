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
/// The chip that appears for a moment after a selection is converted.
///
/// <para>Unlike <see cref="LabelOverlayViewModel"/> this one subscribes to nothing: it is pushed a
/// language by the conversion service at the instant of a conversion and then fades. Reacting to
/// live layout changes would be wrong here — the chip reports what just happened, not what is
/// currently active.</para>
///
/// <para>It borrows the caret label's typography and per-language palette on purpose. The user has
/// already learned that green means Hebrew and red means English from the indicator they see all
/// day; reusing that vocabulary makes the chip read as "OopsType did this" without a word of text.</para>
/// </summary>
public sealed class ConvertChipViewModel : BindableBase
{
    // The swap glyph does double duty: it marks that a conversion happened, and it hints that the
    // conversion is symmetric — switch language again and the text comes back.
    private const string SwapGlyph = "⇄";

    private static readonly Brush DefaultBackground = LabelStyleBrushes.Freeze(WpfColor.FromArgb(0xCC, 0x22, 0x22, 0x22));
    private static readonly Brush DefaultForeground = LabelStyleBrushes.Freeze(Colors.White);
    private static readonly Brush DefaultBorderBrush = LabelStyleBrushes.Freeze(Colors.Transparent);

    private readonly ISettingsService _settings;
    private readonly IErrorReporter _reporter;

    private string _text = SwapGlyph;
    private string _fontFamily = "Segoe UI";
    private double _fontSize = 11;
    private Brush _background = DefaultBackground;
    private Brush _foreground = DefaultForeground;
    private Brush _borderBrush = DefaultBorderBrush;
    private Thickness _borderThickness;
    private FontWeight _fontWeight = FontWeights.Bold;

    public string Text { get => _text; private set => SetProperty(ref _text, value); }
    public string FontFamily { get => _fontFamily; private set => SetProperty(ref _fontFamily, value); }
    public double FontSize { get => _fontSize; private set => SetProperty(ref _fontSize, value); }
    public Brush Background { get => _background; private set => SetProperty(ref _background, value); }
    public Brush Foreground { get => _foreground; private set => SetProperty(ref _foreground, value); }
    public Brush BorderBrush { get => _borderBrush; private set => SetProperty(ref _borderBrush, value); }
    public Thickness BorderThickness { get => _borderThickness; private set => SetProperty(ref _borderThickness, value); }
    public FontWeight FontWeight { get => _fontWeight; private set => SetProperty(ref _fontWeight, value); }

    public ConvertChipViewModel(ISettingsService settings, IErrorReporter reporter)
    {
        _settings = settings;
        _reporter = reporter;
    }

    /// <summary>Re-skins the chip for the language a selection was just converted INTO. UI thread only.</summary>
    public void Show(LanguageInfo target)
    {
        Text = $"{SwapGlyph} {target.DisplayLabel}";

        var label = _settings.Current.CaretLabel;
        FontFamily = string.IsNullOrWhiteSpace(label.Font) ? "Segoe UI" : label.Font;
        FontSize = label.Size <= 0 ? 11 : label.Size;
        FontWeight = LabelStyleBrushes.ParseFontWeight(label.FontWeight);

        var style = LabelStyleBrushes.Resolve(label.Colors, target.TwoLetterCode, _reporter, nameof(ConvertChipViewModel));
        Background = style.Background ?? DefaultBackground;
        Foreground = style.Foreground ?? DefaultForeground;
        BorderBrush = style.BorderBrush ?? DefaultBorderBrush;
        BorderThickness = style.BorderThickness;
    }
}
