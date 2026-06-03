using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using OopsType.Models;

namespace OopsType.ViewModels;

/// <summary>
/// Side-effect-free mapping between the editable <see cref="LabelStyleRow"/> tables shown in the
/// settings page and (a) the persisted per-language <see cref="LabelLangStyle"/> map, and (b) the
/// live-preview chip look. Extracted from <c>SettingsViewModel</c> so the VM no longer owns this
/// projection algorithm — it just calls in. The caret and mouse tables share every rule here.
/// </summary>
internal static class LabelRowMapper
{
    // Built-in dark-chip look: the appearance a freshly-added language starts from, the normalised
    // value written when a field is left blank, and the preview fallback when no row is usable yet.
    private const string DefaultBackgroundHex = "#CC222222";
    private const string DefaultForegroundHex = "#FFFFFFFF";
    private const string DefaultBorderHex = "#FF000000";

    private static readonly Brush PreviewDefaultBackground = LabelStyleBrushes.ParseQuiet(DefaultBackgroundHex)!;
    private static readonly Brush PreviewDefaultForeground = LabelStyleBrushes.ParseQuiet(DefaultForegroundHex)!;
    private static readonly Brush PreviewTransparent = LabelStyleBrushes.ParseQuiet("#00000000")!;

    /// <summary>Repopulates <paramref name="rows"/> in-place (Clear + Add, so CollectionChanged
    /// subscriptions survive) with one editable row per entry in the persisted style map.</summary>
    public static void Reload(ObservableCollection<LabelStyleRow> rows, Dictionary<string, LabelLangStyle> source)
    {
        rows.Clear();
        foreach (var kv in source)
            rows.Add(new LabelStyleRow
            {
                Code = kv.Key,
                Background = kv.Value.Background,
                Foreground = kv.Value.Foreground,
                BorderColor = kv.Value.BorderColor,
                BorderThickness = kv.Value.BorderThickness,
            });
    }

    /// <summary>Projects the editable rows back into the persisted style map, normalising blank
    /// fields to the built-in defaults and clamping border width. Rows with no code are skipped.</summary>
    public static void Apply(ObservableCollection<LabelStyleRow> rows, Dictionary<string, LabelLangStyle> target)
    {
        target.Clear();
        foreach (var row in rows)
        {
            var code = (row.Code ?? "").Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(code)) continue;
            target[code] = new LabelLangStyle
            {
                Background = string.IsNullOrWhiteSpace(row.Background) ? DefaultBackgroundHex : row.Background.Trim(),
                Foreground = string.IsNullOrWhiteSpace(row.Foreground) ? DefaultForegroundHex : row.Foreground.Trim(),
                BorderColor = string.IsNullOrWhiteSpace(row.BorderColor) ? DefaultBorderHex : row.BorderColor.Trim(),
                BorderThickness = Math.Clamp(row.BorderThickness, 0, 20),
            };
        }
    }

    /// <summary>Adds a language row from the picker. Silently ignores blanks and duplicates (the same
    /// code twice would just shadow itself), so the picker never produces a broken table.</summary>
    public static void Add(ObservableCollection<LabelStyleRow> rows, string? code)
    {
        var c = (code ?? "").Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(c)) return;
        foreach (var r in rows)
            if (string.Equals((r.Code ?? "").Trim(), c, StringComparison.OrdinalIgnoreCase)) return;
        rows.Add(NewRow(c));
    }

    // ---- live-preview projections ----
    // The preview chip uses the FIRST configured row (mirrors the strip preview's "first row wins"
    // rule), falling back to the built-in dark chip + "EN" when no usable row exists yet.

    public static string PreviewCode(ObservableCollection<LabelStyleRow> rows)
        => FirstUsableRow(rows)?.Code is { Length: > 0 } code ? code.Trim().ToUpperInvariant() : "EN";

    public static Brush PreviewBackground(ObservableCollection<LabelStyleRow> rows)
        => PreviewBrush(rows, r => r.Background, PreviewDefaultBackground);

    public static Brush PreviewForeground(ObservableCollection<LabelStyleRow> rows)
        => PreviewBrush(rows, r => r.Foreground, PreviewDefaultForeground);

    public static Brush PreviewBorderBrush(ObservableCollection<LabelStyleRow> rows)
    {
        var row = FirstUsableRow(rows);
        if (row == null || row.BorderThickness <= 0) return PreviewTransparent;
        return LabelStyleBrushes.ParseQuiet(row.BorderColor) ?? PreviewTransparent;
    }

    public static Thickness PreviewBorderThickness(ObservableCollection<LabelStyleRow> rows)
    {
        var row = FirstUsableRow(rows);
        if (row == null || row.BorderThickness <= 0) return new Thickness(0);
        // Only show the border in the preview if the color actually parses, so a bad hex doesn't
        // leave a phantom border with no visible stroke.
        return LabelStyleBrushes.ParseQuiet(row.BorderColor) == null ? new Thickness(0) : new Thickness(row.BorderThickness);
    }

    // New rows start from the built-in dark-chip look so a freshly-added language is immediately
    // visible (rather than a blank/transparent chip the user then has to figure out).
    private static LabelStyleRow NewRow(string code) => new()
    {
        Code = code,
        Background = DefaultBackgroundHex,
        Foreground = DefaultForegroundHex,
        BorderColor = DefaultBorderHex,
        BorderThickness = 0,
    };

    private static LabelStyleRow? FirstUsableRow(ObservableCollection<LabelStyleRow> rows)
    {
        foreach (var r in rows)
            if (!string.IsNullOrWhiteSpace(r.Code)) return r;
        return null;
    }

    private static Brush PreviewBrush(ObservableCollection<LabelStyleRow> rows, Func<LabelStyleRow, string> pick, Brush fallback)
    {
        var row = FirstUsableRow(rows);
        if (row == null) return fallback;
        return LabelStyleBrushes.ParseQuiet(pick(row)) ?? fallback;
    }
}
