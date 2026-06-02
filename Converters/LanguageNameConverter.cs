using System;
using System.Globalization;
using System.Windows.Data;

namespace OopsType.Converters;

/// <summary>
/// Converts a two-letter language code (e.g. "en", "he") to a display name for the per-language
/// color rows. ConverterParameter "english" returns the English name; anything else (the default)
/// returns the capitalized native name. Falls back to the upper-cased code for codes that don't
/// resolve to a culture, so the row never renders blank.
/// </summary>
public sealed class LanguageNameConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var code = (value as string)?.Trim();
        if (string.IsNullOrEmpty(code)) return "";
        try
        {
            var ci = new CultureInfo(code);
            var english = string.Equals(parameter as string, "english", StringComparison.OrdinalIgnoreCase);
            var name = english ? ci.EnglishName : ci.NativeName;
            return string.IsNullOrEmpty(name)
                ? code.ToUpperInvariant()
                : char.ToUpper(name[0]) + name[1..];
        }
        catch
        {
            return code.ToUpperInvariant();
        }
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}
