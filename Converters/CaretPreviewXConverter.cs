using System;
using System.Globalization;
using System.Windows.Data;

namespace OopsType.Converters;

/// <summary>
/// Horizontal translate for the caret preview chip, mirroring the live overlay's two horizontal
/// modes (see CaretOverlayPresenter.ComputeX). The chip is positioned with its left edge at the
/// caret centre, so:
///   • manual ("offset") mode — the chip is CENTRED on the caret, so we shift it left by half its
///     width and then by the signed offset → <c>offset - width/2</c>.
///   • auto mode — the chip sits BESIDE the caret by a (rightward) distance, left-edge anchored, so
///     no half-width term → just <c>distance</c>.
/// Values, in order: [0] CaretHorizontalAuto (bool), [1] chip ActualWidth, [2] CaretPreviewOffsetX.
/// </summary>
public sealed class CaretPreviewXConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        var auto = values.Length > 0 && values[0] is bool b && b;
        var width = values.Length > 1 ? ToDouble(values[1]) : 0;
        var offset = values.Length > 2 ? ToDouble(values[2]) : 0;
        return auto ? offset : offset - width / 2;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    // A MultiBinding slot can arrive as DependencyProperty.UnsetValue (runtime type
    // MS.Internal.NamedObject) before its source resolves — e.g. the VM-bound values during the
    // window's first layout, or the chip's ActualWidth before measure. NamedObject is not
    // IConvertible, so Convert.ToDouble would throw "Unable to cast ... NamedObject ... to
    // System.IConvertible". The `is IConvertible` test also covers null. Fall back to 0 → the
    // translate is a no-op until the real value flows in.
    private static double ToDouble(object? v)
        => v is IConvertible ? System.Convert.ToDouble(v, CultureInfo.InvariantCulture) : 0;
}
