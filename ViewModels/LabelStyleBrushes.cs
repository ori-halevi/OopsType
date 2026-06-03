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

    /// <summary>The font-weight names offered in the settings UI (and accepted in settings.json),
    /// in ascending visual weight. "Regular" maps to <see cref="FontWeights.Normal"/>.</summary>
    internal static readonly string[] FontWeightNames = { "Light", "Regular", "Medium", "SemiBold", "Bold" };

    /// <summary>Maps a weight name (case-insensitive) to a WPF <see cref="FontWeight"/>. Unrecognised
    /// or null values fall back to <see cref="FontWeights.Bold"/> — the product default — so a
    /// hand-edited typo in settings.json can never blank the label's weight.</summary>
    internal static FontWeight ParseFontWeight(string? name) => name?.Trim().ToLowerInvariant() switch
    {
        "thin" => FontWeights.Thin,
        "extralight" => FontWeights.ExtraLight,
        "light" => FontWeights.Light,
        "regular" or "normal" => FontWeights.Normal,
        "medium" => FontWeights.Medium,
        "semibold" => FontWeights.SemiBold,
        "bold" => FontWeights.Bold,
        "extrabold" => FontWeights.ExtraBold,
        "black" => FontWeights.Black,
        _ => FontWeights.Bold,
    };

    /// <summary>Snaps an arbitrary stored/loaded weight to the canonical name shown in the UI combo
    /// (so the ComboBox SelectedItem matches an item). Anything unrecognised normalises to "Bold".</summary>
    internal static string NormalizeFontWeightName(string? name)
    {
        if (!string.IsNullOrWhiteSpace(name))
            foreach (var n in FontWeightNames)
                if (string.Equals(n, name.Trim(), System.StringComparison.OrdinalIgnoreCase))
                    return n;
        return "Bold";
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
