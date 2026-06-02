using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using OopsType.Infrastructure;
using OopsType.Models;
using WpfColor = System.Windows.Media.Color;

namespace OopsType.ViewModels;

/// <summary>
/// Shared helper that turns a per-language <see cref="LabelLangStyle"/> palette into frozen WPF
/// brushes for the caret/mouse label chips. Centralised so both overlay VMs resolve colors and
/// report malformed hex identically (mirrors the parsing in TaskbarStripViewModel).
/// </summary>
internal static class LabelStyleBrushes
{
    /// <summary>Resolved brushes for one chip. A null brush means "no configured value — use the
    /// caller's built-in default", so a partially-filled row never blanks out the whole chip.</summary>
    internal readonly record struct Resolved(Brush? Background, Brush? Foreground, Brush? BorderBrush, Thickness BorderThickness);

    internal static Brush Freeze(WpfColor c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    internal static Resolved Resolve(
        IReadOnlyDictionary<string, LabelLangStyle>? colors,
        string twoLetterCode,
        IErrorReporter reporter,
        string source)
    {
        if (colors == null
            || string.IsNullOrEmpty(twoLetterCode)
            || !colors.TryGetValue(twoLetterCode.ToLowerInvariant(), out var style)
            || style == null)
        {
            return default; // all-null brushes + zero thickness → caller substitutes its defaults
        }

        var bg = TryBrush(style.Background, reporter, source);
        var fg = TryBrush(style.Foreground, reporter, source);

        // A border only contributes when it has positive width AND a parseable color; otherwise we
        // leave the brush null and the thickness zero so the chip renders borderless.
        Brush? border = null;
        var thickness = new Thickness(0);
        if (style.BorderThickness > 0)
        {
            border = TryBrush(style.BorderColor, reporter, source);
            if (border != null) thickness = new Thickness(style.BorderThickness);
        }

        return new Resolved(bg, fg, border, thickness);
    }

    private static Brush? TryBrush(string? hex, IErrorReporter reporter, string source)
    {
        if (string.IsNullOrWhiteSpace(hex)) return null;
        var brush = ParseQuiet(hex);
        if (brush == null)
        {
            // Surface malformed user input (or a hand-edited settings.json typo). The reporter
            // throttles per source so a stuck bad value can't flood the log.
            reporter.Report(source, new FormatException($"Invalid color '{hex}'"));
        }
        return brush;
    }

    /// <summary>Parse a hex color into a frozen brush, returning null on any failure WITHOUT
    /// reporting. Used by the settings preview, which re-parses on every keystroke while the user
    /// is still typing — reporting there would spam the log with transient half-typed values.</summary>
    internal static Brush? ParseQuiet(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return null;
        try
        {
            var c = (WpfColor)ColorConverter.ConvertFromString(hex);
            return Freeze(c);
        }
        catch
        {
            return null;
        }
    }
}
