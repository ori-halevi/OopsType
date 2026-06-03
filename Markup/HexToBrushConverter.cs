using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace OopsType.Markup;

/// <summary>
/// One-way converter from a hex color string ("#RGB" / "#RRGGBB" / "#AARRGGBB" / named) to a
/// <see cref="Brush"/> for display bindings (swatches, preview chips). WPF's implicit string→Brush
/// conversion is unreliable for ElementName/RelativeSource bindings, so swatches bound that way can
/// silently render empty — using this converter explicitly makes the conversion deterministic.
/// Invalid/empty input yields <see cref="Brushes.Transparent"/> rather than throwing.
/// </summary>
public sealed class HexToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string s && !string.IsNullOrWhiteSpace(s))
        {
            try
            {
                var c = (Color)ColorConverter.ConvertFromString(s);
                var brush = new SolidColorBrush(c);
                brush.Freeze();
                return brush;
            }
            catch
            {
                // Half-typed or malformed hex while the user edits — fall through to Transparent.
            }
        }
        return Brushes.Transparent;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}
