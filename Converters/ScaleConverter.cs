using System;
using System.Globalization;
using System.Windows.Data;

namespace OopsType.Converters;

/// <summary>
/// Multiplies a numeric value by a constant factor passed as ConverterParameter (default -0.5).
/// Used by the label previews to centre an auto-sized chip on an anchor point: binding a
/// TranslateTransform to the chip's own ActualWidth/ActualHeight with factor -0.5 shifts it left/up
/// by half its size, so its centre lands on the anchor before the user offset is applied. Lives on
/// RenderTransform (not layout), so reading ActualWidth here can't cause a measure loop.
/// </summary>
public sealed class ScaleConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        // A bound source can arrive as DependencyProperty.UnsetValue (runtime type
        // MS.Internal.NamedObject) before it resolves — e.g. an ActualWidth/Height or a
        // RelativeSource that isn't ready during the window's first layout. NamedObject is not
        // IConvertible, so Convert.ToDouble would throw "Unable to cast ... NamedObject ... to
        // System.IConvertible". Treat anything non-convertible as 0 → the transform is a no-op
        // until the real value flows in (the binding re-fires once the source resolves).
        if (value is not IConvertible) return 0d;
        var v = System.Convert.ToDouble(value, CultureInfo.InvariantCulture);
        var factor = parameter is IConvertible
            ? System.Convert.ToDouble(parameter, CultureInfo.InvariantCulture)
            : -0.5;
        return v * factor;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}
